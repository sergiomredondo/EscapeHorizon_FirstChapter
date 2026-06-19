using UnityEngine;

namespace Audio
{
    /// <summary>
    /// Zone-based trigger that drives SH_AmbientAudioManager state changes.
    ///
    /// Place this on any GameObject with a Collider (set Is Trigger = true).
    /// When the player enters the zone, the configured audio event fires.
    ///
    /// Use cases:
    ///   - New area introduction (level event stinger + new ambient layer)
    ///   - Safe zone / rest area (force Exploration regardless of enemy activity)
    ///   - Narrative cutscene trigger (freeze ambient, start level event audio)
    ///
    /// Responsibility boundary:
    ///   OWNS: Trigger detection and routing to SH_AmbientAudioManager.
    ///   DOES NOT OWN: Audio playback or state management.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SH_AmbientAudioTrigger : MonoBehaviour
    {
        // ─── Trigger Action Enum ──────────────────────────────────────────────

        public enum TriggerAction
        {
            /// <summary>Fires a LevelEvent — overrides all other ambient states.</summary>
            StartLevelEvent,

            /// <summary>Ends a LevelEvent — resumes normal state evaluation.</summary>
            EndLevelEvent,

            /// <summary>Resets enemy counters (useful at safe zone entries).</summary>
            ResetEnemyCounters
        }

        // ─── Configuration ────────────────────────────────────────────────────

        [Header("Trigger Behaviour")]

        [Tooltip("Action dispatched to SH_AmbientAudioManager when the player enters this trigger.")]
        [SerializeField] private TriggerAction _actionOnEnter = TriggerAction.StartLevelEvent;

        [Tooltip("If non-zero, an EndLevelEvent is sent automatically after this delay (seconds).\n" +
                 "Only applies when Action On Enter = StartLevelEvent.")]
        [Min(0f)]
        [SerializeField] private float _autoEndAfterSeconds = 0f;

        [Header("Trigger Filtering")]

        [Tooltip("Tag of the GameObject that can activate this trigger.\n" +
                 "Must match the player GameObject tag (e.g. 'Player').")]
        [SerializeField] private string _playerTag = "Player";

        [Tooltip("If true, this trigger can only fire once per scene load.")]
        [SerializeField] private bool _oneShot = true;

        // ─── Runtime ──────────────────────────────────────────────────────────

        private bool _hasFired = false;
        private Coroutine _autoEndCoroutine;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            // Ensure the collider is marked as a trigger.
            Collider col = GetComponent<Collider>();
            if (col != null && !col.isTrigger)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_AmbientAudioTrigger] '{gameObject.name}': " +
                                  "Collider is not set as Trigger. Setting automatically.");
#endif
                col.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_oneShot && _hasFired) return;
            if (!other.CompareTag(_playerTag)) return;

            _hasFired = true;
            Dispatch();
        }

        // ─── Dispatch Logic ───────────────────────────────────────────────────

        private void Dispatch()
        {
            SH_AmbientAudioManager manager = SH_AmbientAudioManager.Instance;

            if (manager == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_AmbientAudioTrigger] '{gameObject.name}': " +
                                  "SH_AmbientAudioManager.Instance is null. Is it in the scene?");
#endif
                return;
            }

            switch (_actionOnEnter)
            {
                case TriggerAction.StartLevelEvent:
                    manager.TriggerLevelEvent();

                    if (_autoEndAfterSeconds > 0f)
                    {
                        if (_autoEndCoroutine != null) StopCoroutine(_autoEndCoroutine);
                        _autoEndCoroutine = StartCoroutine(AutoEndAfterDelay(_autoEndAfterSeconds));
                    }
                    break;

                case TriggerAction.EndLevelEvent:
                    manager.EndLevelEvent();
                    break;

                case TriggerAction.ResetEnemyCounters:
                    manager.ResetEnemyCounters();
                    break;
            }
        }

        private System.Collections.IEnumerator AutoEndAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            SH_AmbientAudioManager.Instance?.EndLevelEvent();
        }

        // ─── Editor Validation ────────────────────────────────────────────────

        private void OnValidate()
        {
            _autoEndAfterSeconds = Mathf.Max(0f, _autoEndAfterSeconds);
        }
    }
}