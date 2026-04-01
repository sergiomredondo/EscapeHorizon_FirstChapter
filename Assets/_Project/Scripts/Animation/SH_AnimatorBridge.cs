using System;
using UnityEngine;

namespace Animation
{
    /// <summary>
    /// Runtime bridge between gameplay action dispatch and the Animator system.
    /// Receives action play requests from SH_ActionState, overrides the Action layer clip,
    /// and manages the internal phase timer that drives gameplay callbacks for startup, active,
    /// and recovery phases. Designed to be flexible and robust, allowing for missing clips while
    /// ensuring all gameplay logic executes correctly based on SH_ActionData timing, independent
    /// of the animation assets.
    /// 
    /// Responsibility boundary:
    ///  OWNS: The AnimatorOverrideController instance used for per-action clip replacement.
    ///  DOES NOT OWN: The Animator Controller asset itself, which defines the state machine
    ///       structure, parameters, and transitions. SH_AnimatorBridge operates at runtime to
    ///       inject clips and control playback speed, but does not modify the underlying asset.
    /// Designed for the player character (Bear) but can be adapted for enemies if they share a similar
    /// Animator Controller structure. For enemies with different animation needs, a separate bridge
    /// class can be created following the same principles of runtime clip injection and phase timer management.
    /// 
    /// Usage:
    /// - Attach SH_AnimatorBridge to the Bear GameObject.
    /// - Initialize it from SH_PlayerContext, passing the Animator component reference.
    /// - SH_ActionState calls PlayActionClip() with the resolved clip and timing parameters from SH_ActionData.
    /// - SH_AnimatorBridge handles the rest: it overrides the clip, normalizes playback speed, and fires the
    ///   appropriate callbacks at the correct times based on the internal phase timer, ensuring that gameplay logic
    ///   executes correctly regardless of the presence or duration of the animation clip. This decoupling allows for
    ///   flexible iteration on both gameplay and animation aspects without blocking each other, which is crucial
    ///   during the prototyping stage.
    /// Stage B additions:
    ///  + The internal phase timer now serves as the primary driver for hitbox activation and deactivation callbacks,
    ///    replacing the reliance on Unity Animation Events. This ensures that gameplay logic executes correctly even
    ///    if clips are missing or still carry their original events.
    ///  + The legacy OnHitImpact Animation Event and its callback registration remain in place as a fallback for clips
    ///    that have not yet been updated to remove their events, ensuring backward compatibility during the transition period.
    /// 
    /// Implementation plan:
    /// Etapa 1: Core functionality
    ///  - Step 1: Create SH_AnimatorBridge with the ability to override clips in the Action layer and manage an
    ///    internal phase timer based on SH_ActionData parameters.
    ///  - Step 2: Define public events for startup complete, active begin, recovery begin, and action complete,
    ///    and invoke them at the correct times based on the phase timer.
    ///  - Step 3: Set up the AnimatorOverrideController to replace the clip in the Action layer slot. This requires
    ///    that the Action state in the Animator Controller uses a placeholder clip (e.g., "Action_Base") that can be
    ///    overridden at runtime. The override controller allows us to inject different clips for different actions
    ///    without modifying the Animator Controller asset itself, maintaining a clean separation of concerns.
    /// Etapa 2: Stage B additions
    ///  - Step 1: Refactor SH_HitboxController to subscribe to the OnActiveBegin event from SH_AnimatorBridge instead
    ///    of relying on an Animation Event for hitbox activation. This ensures that hitbox logic executes correctly
    ///    based on the internal phase timer, independent of the presence or timing of animation events.
    ///  - Step 2: Retain the legacy OnHitImpact Animation Event and its callback registration in SH_AnimatorBridge
    ///    as a fallback for clips that have not yet been updated to remove their events. This allows for a smooth
    ///    transition period where both paths can coexist without breaking gameplay logic.
    /// Etapa 3: Future improvements (post-prototype)
    ///  - Step 1: Update the Animator Controller asset to include a dedicated Action layer if it does not already exist,
    ///    and ensure that the Action state uses a placeholder clip for runtime overriding. This will allow us to remove
    ///    the fallback logic for missing layers and parameters in SH_AnimatorBridge, simplifying the implementation and
    ///    improving robustness.
    ///  - Step 2: Add a dedicated float parameter (e.g., "ActionSpeed") to the Animator Controller for controlling the
    ///    speed of the Action layer independently of the Base layer. This will allow for more precise control over
    ///    playback speed normalization without affecting other layers, and we can remove the fallback to setting the
    ///    global Animator.speed in SH_AnimatorBridge.
    /// </summary>
    public class SH_AnimatorBridge : MonoBehaviour
    {
        // This region contains the core dependencies for SH_AnimatorBridge, including references to the Animator component,
        // the AnimatorOverrideController used for clip injection, and precomputed parameter hashes for efficient access.
        #region Dependencies

        private Animator _animator;
        private AnimatorOverrideController _overrideController;
        private int _movementSpeedHash;
        private int _dashForceHash;
        private int _actionLayerIndex;

        #endregion

        // This region is reserved for any callbacks related to hit impacts that may still be triggered by legacy Animation Events.
        #region Hit Impact Callback

        private Action _hitImpactCallback;

        #endregion

        // This region manages the internal state of the phase timer, including the durations of each phase, the current timer value,
        // and flags to track whether each phase boundary has been crossed.
        #region Phase Timer — Internal State

        private float _clipDuration;
        private float _actionTotalDuration;
        private float _startupTime;
        private float _activeTime;
        private float _phaseTimer;
        private bool _phaseTimerRunning;
        private bool _startupFired;
        private bool _recoveryFired;

        #endregion

        // This region defines the public events that external systems can subscribe to in order to receive notifications
        // about the progression of the action phases.
        #region Phase Callbacks — Public API

        public event Action OnStartupComplete;
        public event Action OnActiveBegin;
        public event Action OnRecoveryBegin;
        public event Action OnActionComplete;

        #endregion

        #region Initialization

        /// <summary>
        /// Context-driven initialization. Called by SH_PlayerContext during orchestration.
        /// Caches the Animator, precomputes parameter hashes, and sets up the
        /// AnimatorOverrideController using the runtime controller as the base.
        /// 
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

            _movementSpeedHash = Animator.StringToHash("Movement_Blend");
            _dashForceHash = Animator.StringToHash("DashForce");

            _actionLayerIndex = _animator.GetLayerIndex("Action");
            if (_actionLayerIndex < 0)
            {
                Debug.LogWarning("[SH_AnimatorBridge] 'Action' layer not found in Animator Controller. " +
                                 "Speed normalization will target layer 0 until the layer is created. " +
                                 "See implementation plan — Etapa 3.");
                _actionLayerIndex = 0;
            }

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
        /// Kept for backward compatibility with clips that retain their original OnHitImpact Animation Events.
        /// The primary path for hitbox activation should now be the OnActiveBegin event driven by the internal
        /// phase timer, which ensures correct gameplay logic execution regardless of animation assets.
        /// This method allows external systems to register a callback for the OnHitImpact Animation Event,
        /// but it is recommended to transition to using the OnActiveBegin event for more robust and decoupled
        /// hitbox activation logic in the future.
        /// 
        /// </summary>
        /// <param name="callback">
        /// The Action delegate to invoke when the OnHitImpact Animation Event is fired. This is a legacy path and
        /// is expected to be phased out in favor of the OnActiveBegin event, but it remains available for clips that
        /// have not yet been updated to remove their events.
        /// </param>
        /// 
        /// <remarks>
        /// This method is designed to be flexible, allowing for a single callback to be registered for the OnHitImpact
        /// Animation Event. If multiple systems need to respond to hit impacts, consider implementing a more robust event
        /// system or using the OnActiveBegin event for better decoupling and reliability.
        /// </remarks>
        public void SetHitImpactCallback(Action callback)
        {
            _hitImpactCallback = callback;
        }

        #endregion

        #region Unity Lifecycle — Phase Timer Tick

        /// <summary>
        /// Ticks the internal phase timer when running, checking against the defined phase durations to fire
        /// the appropriate callbacks at the correct times. This method is called every frame by Unity.
        /// The phase timer is started by PlayActionClip() and stopped by StopActionClip(), ensuring that it
        /// only runs during the lifecycle of an action. The callbacks are fired based on the timing defined in SH_ActionData,
        /// ensuring that gameplay logic executes correctly regardless of the presence or timing of animation clips and events.
        /// The order of callbacks is guaranteed: OnStartupComplete → OnActiveBegin → OnRecoveryBegin → OnActionComplete,
        /// 
        /// Note: The legacy OnHitImpact Animation Event and its callback are still available as a fallback for clips that have
        /// not yet been updated to remove their events, but the primary path for hitbox activation should now be the OnActiveBegin
        /// event driven by this internal phase timer, which ensures correct gameplay logic execution regardless of animation assets.
        /// </summary>
        private void Update()
        {
            if (!_phaseTimerRunning) return;

            _phaseTimer += Time.deltaTime;

            if (!_startupFired && _phaseTimer >= _startupTime)
            {
                _startupFired = true;
                OnStartupComplete?.Invoke();
                OnActiveBegin?.Invoke();
            }

            if (!_recoveryFired && _phaseTimer >= _startupTime + _activeTime)
            {
                _recoveryFired = true;
                OnRecoveryBegin?.Invoke();
            }

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
        /// 
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
            StopPhaseTimer();

            _actionTotalDuration = Mathf.Max(totalDuration, 0.01f);
            _startupTime = Mathf.Clamp(startupTime, 0f, _actionTotalDuration);
            _activeTime = Mathf.Clamp(activeTime, 0f, _actionTotalDuration - _startupTime);

            if (clip != null && _overrideController != null)
            {
                _overrideController["Action_Base"] = clip;
                _clipDuration = clip.length;

                float normalizedSpeed = _clipDuration / _actionTotalDuration;
                _animator.SetFloat(_dashForceHash, 0f);
                SetActionLayerSpeed(normalizedSpeed);

                _animator.CrossFadeInFixedTime("Action.Action_Base", crossFadeDuration, _actionLayerIndex, 0f);
            }
            else
            {
                SetActionLayerSpeed(1f);

                if (clip == null)
                    Debug.LogWarning("[SH_AnimatorBridge] PlayActionClip: no clip provided. " +
                                     "Phase timer will run but no animation will play. " +
                                     "Assign a clip in the SH_ActionAnimationMap asset.");
            }

            StartPhaseTimer();
        }

        /// <summary>
        /// Stops the current action clip and resets the Action layer speed to default.
        /// This should be called when an action is interrupted or cancelled to ensure that
        /// the Animator returns to a neutral state and that the phase timer is stopped.
        /// If the action is allowed to complete naturally, the phase timer will stop itself
        /// and reset the speed in the OnActionComplete callback, so this method is primarily
        /// for handling interruptions.
        /// </summary>
        /// <remarks>
        /// This method ensures that if an action is interrupted (e.g., by a higher priority action or a stun),
        /// the Animator does not remain stuck in the Action state with an overridden clip and modified speed.
        /// 
        /// Note: If the action completes naturally, the OnActionComplete callback will handle stopping the phase
        /// timer and resetting the speed, so this method is specifically for handling cases where the action is
        /// interrupted before it reaches its natural completion.
        /// </remarks>
        public void StopActionClip()
        {
            StopPhaseTimer();
            SetActionLayerSpeed(1f);
        }

        #endregion

        #region Animation Parameter API (unchanged from previous implementation)

        /// <summary>
        /// Sets the movement speed parameter for blending locomotion animations on the Base layer.
        /// This method is called by SH_LocomotionController with the normalized movement speed (0 to 1)
        /// to update the Animator parameter that controls the blend tree for movement animations. The parameter
        /// is expected to be named "Movement_Blend" in the Animator Controller, and the precomputed hash is used
        /// for efficient access. This method is separate from the action clip management and can be called independently
        /// to update movement animations regardless of the current action state. If the Animator or parameter is not set
        /// up correctly, a warning is logged but the method fails gracefully without throwing exceptions, ensuring that
        /// the game remains playable even if the animation setup is incomplete during prototyping.
        /// </summary>
        /// <remarks>
        /// The movement speed parameter is expected to be a float that blends between different locomotion animations
        /// (e.g., idle, walk, run) based on the player's input. The SH_LocomotionController is responsible for calculating
        /// </remarks>
        /// <param name="normalizedSpeed"> A float value normalized to the range [0, 1] representing the player's current
        /// movement speed relative to their maximum speed. </param>
        /// <example>
        /// In SH_LocomotionController.Tick():
        ///   float normalizedSpeed = inputMagnitude; // Assuming inputMagnitude is already normalized to [0, 1]
        ///  animatorBridge.UpdateMovement(normalizedSpeed);
        /// </example>
        /// <remarks>
        public void UpdateMovement(float normalizedSpeed)
        {
            if (_animator == null) return;
            _animator.SetFloat(_movementSpeedHash, normalizedSpeed);
        }

        #endregion

        #region Animation Event Callbacks (legacy fallback)

        /// <summary>
        /// Legacy callback for hit impact, triggered by an Animation Event in the clip.
        /// This is a fallback path for clips that have not yet been updated to remove their events,
        /// and it is expected to be phased out in favor of the OnActiveBegin event driven by the
        /// internal phase timer. If the OnHitImpact Animation Event is fired, this method will invoke
        /// the registered callback if it exists, allowing external systems to respond to hit impacts
        /// as they did previously. However, for new development and future iterations, it is recommended
        /// to transition to using the OnActiveBegin event for more robust and decoupled hitbox activation
        /// logic that does not rely onanimation assets.
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
                _animator.speed = speed;
        }

        #endregion
    }
}