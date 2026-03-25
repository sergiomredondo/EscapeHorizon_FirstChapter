using System;
using UnityEngine;

namespace Animation
{
    /// <summary>
    /// Bridge between the Animator Controller and the player's sub-systems.
    /// Encapsulates all animation parameter writes and serves as the routing
    /// point for animation events and action phase callbacks.
    ///
    /// Extended for the Layered Override Animation System:
    ///   + AnimatorOverrideController — replaces the Action layer clip at runtime
    ///     without modifying the base Animator Controller asset.
    ///   + Playback speed normalization — adjusts Animator.speed on the Action
    ///     layer so any clip duration matches SH_ActionData.TotalDuration exactly.
    ///   + Internal phase timer — fires OnStartupComplete, OnActiveBegin, and
    ///     OnRecoveryBegin callbacks driven by SH_ActionData timing values,
    ///     decoupling hitbox activation from Unity Animation Events.
    ///
    /// Responsibility boundaries:
    ///   OWNS: All Animator parameter writes and override controller management.
    ///   OWNS: Phase timer tick and callback dispatch.
    ///   DOES NOT OWN: Damage logic, hitbox scanning, state transitions.
    ///                 Those remain in SH_HitboxController and SH_ActionState.
    /// </summary>
    public class SH_AnimatorBridge : MonoBehaviour
    {
        #region Dependencies

        private Animator _animator;
        private AnimatorOverrideController _overrideController;

        // Precomputed parameter hashes
        private int _movementSpeedHash;
        private int _dashForceHash;
        private int _actionLayerIndex;

        #endregion

        #region Hit Impact Callback (legacy — kept for Animation Event compatibility)

        /// <summary>
        /// Callback registered by SH_PlayerCombatController.
        /// Invoked by OnHitImpact() when a Unity Animation Event fires.
        /// This path remains active as a fallback for clips that retain
        /// their original Animation Events. The primary activation path
        /// is now the internal phase timer via OnActiveBegin.
        /// </summary>
        private Action _hitImpactCallback;

        #endregion

        #region Phase Timer — Internal State

        /// <summary>
        /// Action clip duration used as the normalization reference.
        /// Set in PlayActionClip() from the clip assigned to the override slot.
        /// </summary>
        private float _clipDuration;

        /// <summary>
        /// Total gameplay duration of the current action (SH_ActionData.TotalDuration).
        /// The clip is accelerated or decelerated so its playback fits this window.
        /// </summary>
        private float _actionTotalDuration;

        /// <summary>
        /// Startup phase duration of the current action (SH_ActionData.startupTime).
        /// </summary>
        private float _startupTime;

        /// <summary>
        /// Active phase duration of the current action (SH_ActionData.activeTime).
        /// </summary>
        private float _activeTime;

        /// <summary>
        /// Elapsed time within the current action, advanced each Update tick
        /// while _phaseTimerRunning is true.
        /// </summary>
        private float _phaseTimer;

        private bool _phaseTimerRunning;
        private bool _startupFired;
        private bool _recoveryFired;

        #endregion

        #region Phase Callbacks — Public API

        /// <summary>
        /// Fired when the startup phase ends (at startupTime seconds into the action).
        /// SH_ActionState uses this to trigger VFX or audio anticipation cues.
        /// </summary>
        public event Action OnStartupComplete;

        /// <summary>
        /// Fired when the active phase begins (at startupTime seconds into the action).
        /// SH_ActionState uses this to enable hitbox scanning in SH_HitboxController,
        /// replacing the Unity Animation Event path.
        /// </summary>
        public event Action OnActiveBegin;

        /// <summary>
        /// Fired when the recovery phase begins (at startupTime + activeTime seconds).
        /// SH_ActionState uses this to disable hitbox scanning and open the cancel window.
        /// </summary>
        public event Action OnRecoveryBegin;

        /// <summary>
        /// Fired when the full action duration has elapsed.
        /// SH_ActionState uses this to return to Idle or Move state.
        /// </summary>
        public event Action OnActionComplete;

        #endregion

        #region Initialization

        /// <summary>
        /// Context-driven initialization. Called by SH_PlayerContext during orchestration.
        /// Caches the Animator, precomputes parameter hashes, and sets up the
        /// AnimatorOverrideController using the runtime controller as the base.
        /// </summary>
        /// <param name="animator">
        /// The Animator component on the entity. Must not be null.
        /// </param>
        public void Initialize(Animator animator)
        {
            if (animator == null)
            {
                Debug.LogError("[SH_AnimatorBridge] Initialize: Animator reference is null.");
                return;
            }

            _animator = animator;

            // Precompute parameter hashes — identical to the previous implementation.
            _movementSpeedHash = Animator.StringToHash("Movement_Blend");
            _dashForceHash = Animator.StringToHash("DashForce");

            // Locate the Action layer by name. Falls back to layer 0 with a warning
            // if the Animator Controller has not yet been updated to include it.
            _actionLayerIndex = _animator.GetLayerIndex("Action");
            if (_actionLayerIndex < 0)
            {
                Debug.LogWarning("[SH_AnimatorBridge] 'Action' layer not found in Animator Controller. " +
                                 "Speed normalization will target layer 0 until the layer is created. " +
                                 "See implementation plan — Etapa 3.");
                _actionLayerIndex = 0;
            }

            // Build the AnimatorOverrideController using the existing runtime controller
            // as the base. This preserves all existing states, parameters, and transitions
            // while allowing per-action clip replacement without modifying the asset.
            var baseController = animator.runtimeAnimatorController;
            if (baseController == null)
            {
                Debug.LogError("[SH_AnimatorBridge] Animator has no RuntimeAnimatorController assigned. " +
                               "Assign an Animator Controller to the Animator component on this entity.");
                return;
            }

            _overrideController = new AnimatorOverrideController(baseController);
            _animator.runtimeAnimatorController = _overrideController;

            Debug.Log($"[SH_AnimatorBridge] Initialized on '{gameObject.name}'. " +
                      $"Override controller built from '{baseController.name}'. " +
                      $"Action layer index: {_actionLayerIndex}.");
        }

        /// <summary>
        /// Registers the legacy Animation Event callback.
        /// Kept for backward compatibility with clips that retain their
        /// original OnHitImpact Animation Events.
        /// </summary>
        public void SetHitImpactCallback(Action callback)
        {
            _hitImpactCallback = callback;
        }

        #endregion

        #region Unity Lifecycle — Phase Timer Tick

        private void Update()
        {
            if (!_phaseTimerRunning) return;

            _phaseTimer += Time.deltaTime;

            // Startup → Active boundary
            if (!_startupFired && _phaseTimer >= _startupTime)
            {
                _startupFired = true;
                OnStartupComplete?.Invoke();
                OnActiveBegin?.Invoke();
            }

            // Active → Recovery boundary
            if (!_recoveryFired && _phaseTimer >= _startupTime + _activeTime)
            {
                _recoveryFired = true;
                OnRecoveryBegin?.Invoke();
            }

            // Full action complete
            if (_phaseTimer >= _actionTotalDuration)
            {
                StopPhaseTimer();
                OnActionComplete?.Invoke();
            }
        }

        #endregion

        #region Action Clip Override — Public API

        /// <summary>
        /// Overrides the clip in the Action layer slot and begins phase timer tracking.
        ///
        /// If the map provides a valid clip, it is injected into the override controller
        /// and the Animator crossfades into the Action state. Playback speed is normalized
        /// so the clip's full duration matches SH_ActionData.TotalDuration.
        ///
        /// If no clip is available (null), the phase timer still runs and all callbacks
        /// fire at the correct times, ensuring gameplay logic is never blocked by a
        /// missing animation asset.
        /// </summary>
        /// <param name="clip">
        /// The AnimationClip to inject. May be null during early prototyping — gameplay
        /// callbacks will still fire correctly without a visual clip.
        /// </param>
        /// <param name="totalDuration">SH_ActionData.TotalDuration — total action length in seconds.</param>
        /// <param name="startupTime">SH_ActionData.startupTime — startup phase length in seconds.</param>
        /// <param name="activeTime">SH_ActionData.activeTime — active phase length in seconds.</param>
        /// <param name="crossFadeDuration">
        /// Blend time in seconds for the CrossFade into the Action state.
        /// Default 0.08s is appropriate for most melee attacks.
        /// </param>
        public void PlayActionClip(
            AnimationClip clip,
            float totalDuration,
            float startupTime,
            float activeTime,
            float crossFadeDuration = 0.08f)
        {
            // --- Reset phase timer ---
            StopPhaseTimer();

            _actionTotalDuration = Mathf.Max(totalDuration, 0.01f);
            _startupTime = Mathf.Clamp(startupTime, 0f, _actionTotalDuration);
            _activeTime = Mathf.Clamp(activeTime, 0f, _actionTotalDuration - _startupTime);

            // --- Clip override ---
            if (clip != null && _overrideController != null)
            {
                // The override controller replaces the clip in the slot named "Action_Base".
                // This name must match the Motion field of the Action state in the
                // Animator Controller. See implementation plan — Etapa 1, step 3.
                _overrideController["Action_Base"] = clip;
                _clipDuration = clip.length;

                // Normalize playback speed so the clip's visual duration matches
                // the gameplay duration defined in SH_ActionData.
                // speed = clipDuration / totalDuration
                // Example: clip = 1.2s, totalDuration = 0.8s → speed = 1.5 (faster)
                // Example: clip = 0.4s, totalDuration = 0.8s → speed = 0.5 (slower)
                float normalizedSpeed = _clipDuration / _actionTotalDuration;
                _animator.SetFloat(_dashForceHash, 0f); // clear any residual dash blend
                SetActionLayerSpeed(normalizedSpeed);

                // When a state lives in a non-Base layer, Unity requires the fully
                // qualified name "LayerName.StateName" for CrossFadeInFixedTime to
                // resolve it correctly at runtime.
                _animator.CrossFadeInFixedTime("Action.Action_Base", crossFadeDuration, _actionLayerIndex, 0f);
            }
            else
            {
                // No clip available — reset layer speed and let the timer run.
                // Gameplay (hitbox, callbacks) works correctly without a visual clip.
                SetActionLayerSpeed(1f);

                if (clip == null)
                    Debug.LogWarning("[SH_AnimatorBridge] PlayActionClip: no clip provided. " +
                                     "Phase timer will run but no animation will play. " +
                                     "Assign a clip in the SH_ActionAnimationMap asset.");
            }

            // --- Start phase timer ---
            StartPhaseTimer();
        }

        /// <summary>
        /// Stops the phase timer and resets the Action layer speed.
        /// The Base Layer locomotion never stopped — no crossfade back needed.
        /// The Action layer returns to its default state via Exit Time on Action_Base.
        /// </summary>
        public void StopActionClip()
        {
            StopPhaseTimer();
            SetActionLayerSpeed(1f);
        }

        #endregion

        #region Animation Parameter API (unchanged from previous implementation)

        /// <summary>
        /// Updates the Movement_Blend float parameter to drive the locomotion blend tree.
        /// Called every frame by SH_IdleState and SH_MoveState.
        /// </summary>
        public void UpdateMovement(float normalizedSpeed)
        {
            if (_animator == null) return;
            _animator.SetFloat(_movementSpeedHash, normalizedSpeed);
        }

        /// <summary>
        /// Updates DashForce for the dash animation blend.
        /// Called by SH_ActionState during the active dash phase.
        /// </summary>
        public void TriggerDash(float normalizedSpeed)
        {
            if (_animator == null) return;

            if (normalizedSpeed < 0f)
            {
                Debug.Log($"[SH_AnimatorBridge] TriggerDash: negative normalizedSpeed ({normalizedSpeed}).");
                return;
            }

            if (_animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.60f && normalizedSpeed > 0f)
                _animator.SetFloat(_dashForceHash,
                    _animator.GetCurrentAnimatorStateInfo(0).normalizedTime + 0.61f);
            else
                _animator.SetFloat(_dashForceHash, normalizedSpeed);
        }

        #endregion

        #region Animation Event Callbacks (legacy fallback)

        /// <summary>
        /// Called by a Unity Animation Event at the hit-impact frame.
        /// This path is now secondary — the primary hitbox activation path
        /// is the OnActiveBegin callback fired by the internal phase timer.
        /// Retained for clips that still carry their original Animation Events.
        /// </summary>
        public void OnHitImpact()
        {
            Debug.Log("[SH_AnimatorBridge] Combat Debug OnHitImpact Animation Event fired.");
            if (_hitImpactCallback != null)
            {
                _hitImpactCallback.Invoke();
            }
            else
            {
                Debug.LogWarning(
                    "[SH_AnimatorBridge] OnHitImpact Animation Event fired but no callback " +
                    "is registered. This is expected if SH_ActionState is now handling " +
                    "hitbox activation via the OnActiveBegin phase callback.");
            }
        }

        #endregion

        #region Internal Helpers

        private void StartPhaseTimer()
        {
            _phaseTimer = 0f;
            _startupFired = false;
            _recoveryFired = false;
            _phaseTimerRunning = true;
        }

        private void StopPhaseTimer()
        {
            _phaseTimerRunning = false;
            _phaseTimer = 0f;
        }

        private void SetActionLayerSpeed(float speed)
        {
            if (_animator == null) return;

            // AnimatorStateInfo.speed is read-only per-state; we set it via a dedicated
            // float parameter named "ActionSpeed" on the Animator Controller.
            // If the parameter does not yet exist (pre-Etapa 3), we fall back to setting
            // the global Animator.speed, which affects all layers equally.
            // This fallback is acceptable for the prototype stage.
            int actionSpeedHash = Animator.StringToHash("ActionSpeed");

            bool hasActionSpeedParam = false;
            foreach (var param in _animator.parameters)
            {
                if (param.nameHash == actionSpeedHash && param.type == AnimatorControllerParameterType.Float)
                {
                    hasActionSpeedParam = true;
                    break;
                }
            }

            if (hasActionSpeedParam)
                _animator.SetFloat(actionSpeedHash, speed);
            else
                _animator.speed = speed; // global fallback — all layers affected
        }

        #endregion
    }
}