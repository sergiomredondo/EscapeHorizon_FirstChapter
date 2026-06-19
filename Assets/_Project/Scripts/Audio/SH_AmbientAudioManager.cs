using Game.Enemy;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Audio
{
    /// <summary>
    /// Manages all ambient audio layers for ScapeHorizon.
    ///
    /// Responsibility boundary:
    ///   OWNS: Ambient audio state machine, layer crossfading, clip selection,
    ///         and enemy-state aggregation for audio priority resolution.
    ///   DOES NOT OWN: SFX (handled per-action by SH_ActionData prefabs),
    ///                 music stingers, or UI audio.
    ///
    /// Audio state priority (highest overrides lower):
    ///   Combat > Search > Level > Idle/Exploration
    ///
    /// Enemy aggregation rules:
    ///   - Any enemy in Attack   → CombatState (highest)
    ///   - Any enemy in Evade    → CombatState (evasion = player is powerful = still combat)
    ///   - Any enemy in Search   → SearchState (awareness without visual contact)
    ///   - No active enemies     → Level ambient (or Exploration if none configured)
    ///
    /// Usage:
    ///   1. Add this component to a persistent manager GameObject in the scene.
    ///   2. Assign AudioSource references (create two AudioSource children for crossfading).
    ///   3. Populate the clip arrays in each AudioLayer in the Inspector.
    ///   4. Call NotifyEnemyStateChanged() from SH_EnemyController when state transitions occur.
    ///   5. Use the TriggerZone component (SH_AmbientAudioTrigger) to drive zone changes.
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_AmbientAudioManager : MonoBehaviour
    {
        // ─── Enums ────────────────────────────────────────────────────────────

        /// <summary>
        /// Global ambient audio state, resolved from enemy activity and level events.
        /// </summary>
        public enum AmbientState
        {
            /// <summary>No enemies active, environment is calm. Plays exploration/idle layer.</summary>
            Exploration,

            /// <summary>One or more enemies are in Search mode (awareness state).</summary>
            Search,

            /// <summary>One or more enemies are in Attack or Evade mode.</summary>
            Combat,

            /// <summary>Manually triggered by a level event or trigger zone.</summary>
            LevelEvent
        }

        // ─── Internal Clip Bank ───────────────────────────────────────────────

        /// <summary>
        /// A set of AudioClips for a specific ambient state.
        /// Random selection with no-repeat logic mirrors SH_ActionAnimationMap.
        /// </summary>
        [System.Serializable]
        public class AmbientLayer
        {
            [Tooltip("Display name shown in the Inspector for clarity.")]
            public string layerName = "Layer";

            [Tooltip("Array of AudioClips. One is picked at random (no repeat) each time this layer plays.\n" +
                     "Leave empty to silence this state.")]
            public AudioClip[] clips;

            [Tooltip("Volume for this layer (0–1). Use lower values for subtle background ambience.")]
            [Range(0f, 1f)]
            public float volume = 0.6f;

            [Tooltip("If true, the selected clip loops continuously until the state changes.")]
            public bool loop = true;

            [Tooltip("Pitch variation range. 0 = no variation, 0.1 = ±5% variation.")]
            [Range(0f, 0.5f)]
            public float pitchVariation = 0f;

            // Non-serialized: tracks last played index to avoid repeats.
            [System.NonSerialized] public int lastIndex = -1;

            /// <summary>
            /// Returns a random clip from this layer avoiding immediate repetition.
            /// Returns null if the array is empty.
            /// </summary>
            public AudioClip GetRandomClip()
            {
                if (clips == null || clips.Length == 0) return null;
                if (clips.Length == 1) return clips[0];

                int newIndex;
                int attempts = 0;
                do
                {
                    newIndex = Random.Range(0, clips.Length);
                    attempts++;
                }
                while (newIndex == lastIndex && attempts < 10);

                lastIndex = newIndex;
                return clips[newIndex];
            }

            public bool HasClips => clips != null && clips.Length > 0;
        }

        // ─── Serialized Configuration ─────────────────────────────────────────

        [Header("Audio Sources — Crossfade Pair")]

        [Tooltip("Primary AudioSource (starts active). Used for the initial ambient layer.\n" +
                 "Configure: Play On Awake OFF, Loop ON, Spatial Blend 0 (2D).")]
        [SerializeField] private AudioSource _sourceA;

        [Tooltip("Secondary AudioSource for crossfading transitions.\n" +
                 "Configure same as Source A.")]
        [SerializeField] private AudioSource _sourceB;

        [Header("Ambient Layers — Per State")]

        [Tooltip("Audio played during calm exploration when no enemies are alerted.\n" +
                 "Ideal for environmental ambience: wind, machinery hum, distant sounds.")]
        [SerializeField] private AmbientLayer _explorationLayer = new AmbientLayer { layerName = "Exploration", volume = 0.5f };

        [Tooltip("Audio played when at least one enemy is in Search mode (investigating).\n" +
                 "Ideal for tense, low-intensity music or unsettling ambience.")]
        [SerializeField] private AmbientLayer _searchLayer = new AmbientLayer { layerName = "Search / Awareness", volume = 0.65f };

        [Tooltip("Audio played when at least one enemy is actively attacking or evading.\n" +
                 "Ideal for combat music or high-intensity ambience. Supports multiple clips for variety.")]
        [SerializeField] private AmbientLayer _combatLayer = new AmbientLayer { layerName = "Combat", volume = 0.8f };

        [Tooltip("Audio played when a level event trigger activates (e.g., entering a new zone,\n" +
                 "triggering a narrative beat, or activating a SH_AmbientAudioTrigger).")]
        [SerializeField] private AmbientLayer _levelEventLayer = new AmbientLayer { layerName = "Level Event", volume = 0.7f };

        [Header("Transition Settings")]

        [Tooltip("Duration (seconds) of the crossfade between ambient states.\n" +
                 "Shorter = snappier tension shifts. Longer = more cinematic.")]
        [Min(0.05f)]
        [SerializeField] private float _crossfadeDuration = 2.0f;

        [Tooltip("Duration (seconds) of the crossfade when returning from Combat to Exploration.\n" +
                 "Usually longer than _crossfadeDuration to convey relief.")]
        [Min(0.05f)]
        [SerializeField] private float _combatToExplorationFadeDuration = 4.0f;

        [Tooltip("Minimum seconds the system stays in Combat state before allowing a downgrade,\n" +
                 "even if all enemies have disengaged. Prevents rapid flickering.")]
        [Min(0f)]
        [SerializeField] private float _combatLingerDuration = 3.0f;

        [Tooltip("Minimum seconds the system stays in Search state before downgrading to Exploration.\n" +
                 "Gives enemies time to complete investigation routines before music relaxes.")]
        [Min(0f)]
        [SerializeField] private float _searchLingerDuration = 5.0f;

        [Header("Level Start")]

        [Tooltip("AudioClip played once (no loop) when the level begins, before the ambient layer starts.\n" +
                 "Leave empty to skip the level start sting.")]
        [SerializeField] private AudioClip _levelStartStinger;

        [Tooltip("Volume for the level start stinger (0–1).")]
        [Range(0f, 1f)]
        [SerializeField] private float _levelStartStingerVolume = 0.9f;

        [Tooltip("Delay (seconds) after the stinger finishes before the exploration ambient begins.")]
        [Min(0f)]
        [SerializeField] private float _stingerToAmbientDelay = 0.5f;

        [Header("State Transition Stingers")]

        [Tooltip("One-shot clips played when entering Combat state (e.g., alarm sound, impact hit).\n" +
                 "Played over the crossfade, not as a loop. Leave empty to skip.")]
        [SerializeField] private AudioClip[] _combatEntryStingers;

        [Tooltip("One-shot clips played when returning to Exploration from Combat (e.g., relief chord, silence break).\n" +
                 "Leave empty to skip.")]
        [SerializeField] private AudioClip[] _combatExitStingers;

        [Tooltip("Volume for all transition stingers.")]
        [Range(0f, 1f)]
        [SerializeField] private float _stingerVolume = 0.75f;

        [Header("Debug")]

        [Tooltip("If true, logs all state changes and clip selections to the Console in Play mode.")]
        [SerializeField] private bool _debugLog = false;

        // ─── Runtime State ────────────────────────────────────────────────────

        private AmbientState _currentState = AmbientState.Exploration;
        private AmbientState _targetState = AmbientState.Exploration;

        // Tracks how long we've been in the current state, for linger logic.
        private float _stateTimer = 0f;

        // Enemy state counters — updated by NotifyEnemyStateChanged().
        private int _enemiesInCombat = 0;    // Attack + Evade
        private int _enemiesInSearch = 0;    // Search

        // Crossfade coroutine handle — cancel on new transition.
        private Coroutine _crossfadeCoroutine;

        // Stinger AudioSource (one-shot, spawned at runtime).
        private AudioSource _stingerSource;

        // Which of the two sources is currently "active" (audible).
        private bool _sourceAIsActive = true;

        private AudioSource ActiveSource => _sourceAIsActive ? _sourceA : _sourceB;
        private AudioSource InactiveSource => _sourceAIsActive ? _sourceB : _sourceA;

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Notifies the manager that an enemy has changed state.
        /// Call this from SH_EnemyController.TransitionTo() or from a wrapper event.
        ///
        /// <para>
        /// Recommended integration:
        ///   In SH_EnemyController.TransitionTo(), after setting _state, call:
        ///     SH_AmbientAudioManager.Instance?.NotifyEnemyStateChanged(prevState, newState);
        /// </para>
        /// </summary>
        /// <param name="previousStateName">Previous enemy state name (e.g. "Patrol", "Attack").</param>
        /// <param name="newStateName">New enemy state name.</param>
        public void NotifyEnemyStateChanged(string previousStateName, string newStateName)
        {
            // Decrement counters for the previous state.
            AdjustCounters(previousStateName, -1);

            // Increment counters for the new state.
            AdjustCounters(newStateName, +1);

            // Clamp to avoid negatives from initialization edge cases.
            _enemiesInCombat = Mathf.Max(0, _enemiesInCombat);
            _enemiesInSearch = Mathf.Max(0, _enemiesInSearch);

            EvaluateTargetState();
        }

        /// <summary>
        /// Forces an immediate transition to LevelEvent state and plays the event layer.
        /// Call from SH_AmbientAudioTrigger or a narrative system.
        /// </summary>
        public void TriggerLevelEvent()
        {
            SetTargetState(AmbientState.LevelEvent);
        }

        /// <summary>
        /// Ends the LevelEvent state and re-evaluates the correct ambient state from enemy data.
        /// Call when the level event is over (e.g., cutscene ends, zone transition completes).
        /// </summary>
        public void EndLevelEvent()
        {
            EvaluateTargetState();
        }

        /// <summary>
        /// Resets all enemy counters. Call from SH_EnemyController.ResetEnemy() or
        /// SH_EnemyController.ResetSharedAlert() equivalents when the scene resets.
        /// </summary>
        public void ResetEnemyCounters()
        {
            _enemiesInCombat = 0;
            _enemiesInSearch = 0;
            EvaluateTargetState();
        }

        /// <summary>
        /// Read-only access to the currently active ambient state.
        /// </summary>
        public AmbientState CurrentState => _currentState;

        // ─── Singleton (lightweight — scene-scoped) ───────────────────────────

        public static SH_AmbientAudioManager Instance { get; private set; }

        // ─── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Create a dedicated AudioSource for one-shot stingers so they
            // never interrupt the crossfade sources.
            _stingerSource = gameObject.AddComponent<AudioSource>();
            _stingerSource.playOnAwake = false;
            _stingerSource.loop = false;
            _stingerSource.spatialBlend = 0f; // 2D

            ValidateSources();
        }

        private void Start()
        {
            StartCoroutine(LevelStartSequence());
        }

        private void Update()
        {
            // Advance linger timer to allow state downgrades.
            _stateTimer += Time.deltaTime;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ─── Level Start Sequence ─────────────────────────────────────────────

        private IEnumerator LevelStartSequence()
        {
            // Play one-shot level start stinger if assigned.
            if (_levelStartStinger != null)
            {
                _stingerSource.volume = _levelStartStingerVolume;
                _stingerSource.PlayOneShot(_levelStartStinger);

                // Wait for the stinger duration + extra delay before starting ambient.
                yield return new WaitForSeconds(_levelStartStinger.length + _stingerToAmbientDelay);
            }

            // Begin with Exploration ambient.
            PlayLayerImmediate(_explorationLayer, ActiveSource);
            _currentState = AmbientState.Exploration;
            _targetState = AmbientState.Exploration;
#if UNITY_EDITOR
            LogDebug($"[AmbientAudio] Level start — entering {_currentState}.");
#endif
        }

        // ─── State Evaluation ─────────────────────────────────────────────────

        private void EvaluateTargetState()
        {
            // LevelEvent is externally driven — don't auto-override it here.
            if (_targetState == AmbientState.LevelEvent) return;

            AmbientState desired;

            if (_enemiesInCombat > 0)
                desired = AmbientState.Combat;
            else if (_enemiesInSearch > 0)
                desired = AmbientState.Search;
            else
                desired = AmbientState.Exploration;

            // Apply linger rules: don't downgrade before the linger period expires.
            if (desired < _currentState)
            {
                float lingerRequired = _currentState == AmbientState.Combat
                    ? _combatLingerDuration
                    : _searchLingerDuration;

                if (_stateTimer < lingerRequired)
                    return; // Still lingering — keep current state.
            }

            SetTargetState(desired);
        }

        private void SetTargetState(AmbientState newState)
        {
            if (newState == _targetState && newState == _currentState) return;

            _targetState = newState;
            StartTransition(newState);
        }

        // ─── Crossfade Transition ─────────────────────────────────────────────

        private void StartTransition(AmbientState toState)
        {
            AmbientLayer targetLayer = GetLayer(toState);

            if (!targetLayer.HasClips)
            {
#if UNITY_EDITOR
                LogDebug($"[AmbientAudio] State {toState} has no clips — skipping transition.");
#endif
                _currentState = toState;
                return;
            }

            // Cancel any in-progress crossfade.
            if (_crossfadeCoroutine != null)
                StopCoroutine(_crossfadeCoroutine);

            // Resolve fade duration.
            float fadeDuration = (_currentState == AmbientState.Combat && toState == AmbientState.Exploration)
                ? _combatToExplorationFadeDuration
                : _crossfadeDuration;

            // Play transition stingers.
            PlayTransitionStinger(toState);

            // Start the crossfade.
            _crossfadeCoroutine = StartCoroutine(
                CrossfadeTo(targetLayer, fadeDuration, toState));
#if UNITY_EDITOR
            LogDebug($"[AmbientAudio] Transitioning {_currentState} → {toState} over {fadeDuration:F1}s.");
#endif
        }

        private IEnumerator CrossfadeTo(AmbientLayer layer, float duration, AmbientState targetState)
        {
            AudioSource fadeIn = InactiveSource;
            AudioSource fadeOut = ActiveSource;

            // Configure incoming source.
            AudioClip clip = layer.GetRandomClip();
            fadeIn.clip = clip;
            fadeIn.loop = layer.loop;
            fadeIn.volume = 0f;
            fadeIn.pitch = 1f + Random.Range(-layer.pitchVariation * 0.5f, layer.pitchVariation * 0.5f);
            fadeIn.Play();

            float elapsed = 0f;
            float startVolume = fadeOut.volume;
            float targetVolume = layer.volume;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime; // Unscaled: works during hitstop/time scale effects.
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);

                fadeIn.volume = Mathf.Lerp(0f, targetVolume, t);
                fadeOut.volume = Mathf.Lerp(startVolume, 0f, t);

                yield return null;
            }

            fadeIn.volume = targetVolume;
            fadeOut.volume = 0f;
            fadeOut.Stop();

            // Swap active/inactive.
            _sourceAIsActive = !_sourceAIsActive;

            _currentState = targetState;
            _stateTimer = 0f;

#if UNITY_EDITOR
            LogDebug($"[AmbientAudio] Now in state {_currentState}. Clip: {clip?.name ?? "none"}.");
#endif
            _crossfadeCoroutine = null;
        }

        // ─── Immediate Playback (no fade, for level start) ────────────────────

        private void PlayLayerImmediate(AmbientLayer layer, AudioSource source)
        {
            if (!layer.HasClips) return;

            AudioClip clip = layer.GetRandomClip();
            source.clip = clip;
            source.loop = layer.loop;
            source.volume = layer.volume;
            source.pitch = 1f + Random.Range(-layer.pitchVariation * 0.5f, layer.pitchVariation * 0.5f);
            source.Play();
        }

        // ─── Stinger Playback ─────────────────────────────────────────────────

        private void PlayTransitionStinger(AmbientState toState)
        {
            AudioClip[] pool = null;

            if (toState == AmbientState.Combat)
                pool = _combatEntryStingers;
            else if (toState == AmbientState.Exploration && _currentState == AmbientState.Combat)
                pool = _combatExitStingers;

            if (pool == null || pool.Length == 0) return;

            AudioClip stinger = pool[Random.Range(0, pool.Length)];
            if (stinger == null) return;

            _stingerSource.volume = _stingerVolume;
            _stingerSource.PlayOneShot(stinger);
        }

        // ─── Counter Helpers ──────────────────────────────────────────────────

        private void AdjustCounters(string stateName, int delta)
        {
            switch (stateName)
            {
                case "Attack":
                case "Evade":
                    _enemiesInCombat += delta;
                    break;

                case "Search":
                    _enemiesInSearch += delta;
                    break;

                    // Patrol / Retreat / Vulnerable / Dead → not counted as active threats.
            }
        }

        // ─── Layer Accessor ───────────────────────────────────────────────────

        private AmbientLayer GetLayer(AmbientState state) => state switch
        {
            AmbientState.Exploration => _explorationLayer,
            AmbientState.Search => _searchLayer,
            AmbientState.Combat => _combatLayer,
            AmbientState.LevelEvent => _levelEventLayer,
            _ => _explorationLayer
        };

        // ─── Validation ───────────────────────────────────────────────────────

        private void ValidateSources()
        {
            if (_sourceA == null || _sourceB == null)
            {
#if UNITY_EDITOR
                Debug.LogError("[SH_AmbientAudioManager] One or both AudioSources are not assigned. " +
                               "Create two AudioSource children and assign them in the Inspector.");
#endif
            }
        }

        // ─── Debug ────────────────────────────────────────────────────────────

        private void LogDebug(string message)
        {
#if UNITY_EDITOR
            if (_debugLog) Debug.Log(message);
#endif
        }

        // ─── Editor Validation ────────────────────────────────────────────────

        private void OnValidate()
        {
            _crossfadeDuration = Mathf.Max(0.05f, _crossfadeDuration);
            _combatToExplorationFadeDuration = Mathf.Max(0.05f, _combatToExplorationFadeDuration);
            _combatLingerDuration = Mathf.Max(0f, _combatLingerDuration);
            _searchLingerDuration = Mathf.Max(0f, _searchLingerDuration);
            _stingerToAmbientDelay = Mathf.Max(0f, _stingerToAmbientDelay);
        }
    }
}