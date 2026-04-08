using Actions.Data;
using Animation;
using Core.StateMachine.States;
using Game.Economy;
using Game.Economy.Data;
using UnityEngine;

namespace Core.StateMachine.States
{
    /// <summary>
    /// Data-driven execution state for high-commitment actions (attacks, dashes, skills).
    ///
    /// Extended for the Layered Override Animation System (Etapa 2):
    ///   + On Enter(), calls SH_AnimatorBridge.PlayActionClip() with the clip resolved
    ///     from SH_ActionAnimationMap and the timing values from SH_ActionData.
    ///     This replaces the TriggerAttack() / Animation Event path for attack actions.
    ///   + Subscribes to SH_AnimatorBridge phase callbacks:
    ///       OnActiveBegin   → enables hitbox scanning in SH_HitboxController.
    ///       OnRecoveryBegin → disables hitbox scanning (replaces DeactivateHitDetection
    ///                         call that previously had to wait for Exit()).
    ///       OnActionComplete → transitions back to Idle/Move state.
    ///   + On Exit(), unsubscribes all callbacks and calls StopActionClip() to ensure
    ///     the bridge is in a clean state if the action is interrupted before completion.
    ///
    /// Trigger routing convention (preserved for the Dash path):
    ///   "Dash" → TriggerDash() path (existing behavior, unchanged).
    ///   Any other action → PlayActionClip() path via SH_ActionAnimationMap.
    ///   ""     → No animation dispatched (free/passive actions).
    ///
    /// Responsibility boundaries:
    ///   OWNS: Action phase tracking, impulse physics, economic gate, movement lock.
    ///   OWNS: Subscribing to and reacting to bridge phase callbacks.
    ///   DOES NOT OWN: Clip selection (SH_ActionAnimationMap), timer tick (SH_AnimatorBridge),
    ///                 damage calculation (SH_DamageCalculator), overlap scan (SH_HitboxController).
    /// </summary>
    public class SH_ActionState : SH_BaseState
    {
        #region Private Fields

        private readonly SH_ActionData _actionData;
        private readonly SH_ActionAnimationMap _animationMap;

        private float _elapsedTime;
        private bool _impulseApplied;
        private bool _abortedDueToInsufficientEnergy;

        // Phase boundaries — computed once in Enter() from SH_ActionData values.
        private float _startupEnd;
        private float _activeEnd;
        private float _recoveryEnd;

        private enum ActionPhase { Startup, Active, Recovery, Completed }
        private ActionPhase _phase;

        /// <summary>
        /// True once the bridge has fired OnActionComplete or the state has been
        /// interrupted. Guards against double state transitions if both the bridge
        /// callback and the internal timer reach completion in the same frame.
        /// </summary>
        private bool _transitionRequested;

        /// <summary>
        /// True while the bridge phase callbacks are subscribed.
        /// Used to ensure Unsubscribe() is only called when subscriptions are active,
        /// preventing ArgumentNullException if Exit() is called before Enter() completes.
        /// </summary>
        private bool _callbacksSubscribed;

        #endregion

        #region Priority

        public override int Priority => _actionData.priority;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructs the action state with all required dependencies.
        /// </summary>
        /// <param name="context">The player context (SSOT).</param>
        /// <param name="stateMachine">The owning state machine.</param>
        /// <param name="actionData">The gameplay contract for this action.</param>
        /// <param name="animationMap">
        /// The presentation map used to resolve the AnimationClip for this action.
        /// May be null during early prototyping — gameplay will function correctly
        /// but no clip will be injected into the override controller.
        /// </param>
        public SH_ActionState(
            SH_PlayerContext context,
            SH_PlayerStateMachine stateMachine,
            SH_ActionData actionData,
            SH_ActionAnimationMap animationMap = null)
            : base(context, stateMachine)
        {
            if (actionData == null)
                Debug.LogError("[SH_ActionState] actionData is null.");

            _actionData = actionData;
            _animationMap = animationMap;
        }

        #endregion

        #region Lifecycle

        public override void Enter()
        {
            _abortedDueToInsufficientEnergy = false;
            _transitionRequested = false;
            _callbacksSubscribed = false;
            _elapsedTime = 0f;
            _impulseApplied = false;

            // --- Economic Gate ---
            if (_actionData.staminaCost > 0f)
            {
                SH_ResourceSystem resources = _context.Resources;
                if (resources == null)
                {
                    Debug.LogWarning(
                        $"[SH_ActionState] SH_ResourceSystem is null — skipping energy " +
                        $"check for '{_actionData.name}'.");
                }
                else
                {
                    bool consumed = resources.ConsumeResource(
                        ResourceType.EnergyCore, _actionData.staminaCost);

                    if (!consumed)
                    {
                        Debug.Log(
                            $"[SH_ActionState] '{_actionData.name}' aborted: " +
                            $"need {_actionData.staminaCost:F1} EC, " +
                            $"have {resources.CurrentEnergy:F1} EC.");

                        _abortedDueToInsufficientEnergy = true;
                        _context.HitboxController?.DeactivateHitDetection();
                        return;
                    }
                }
            }

            // --- Phase Boundaries ---
            _startupEnd = _actionData.startupTime;
            _activeEnd = _startupEnd + _actionData.activeTime;
            _recoveryEnd = _activeEnd + _actionData.recoveryTime;
            _phase = ActionPhase.Startup;

            // --- Movement Lock ---
            if (_actionData.locksMovement)
            {
                // Lock new locomotion input but preserve existing momentum.
                // In a hack and slash the character attacks while moving —
                // cancelling velocity would feel unresponsive and wrong.
                _context.Locomotion.SetMovementLock(true);
            }

            // --- Animation Dispatch ---
            DispatchAnimation();
        }

        public override void Update()
        {
            if (_abortedDueToInsufficientEnergy)
            {
                _stateMachine.ChangeState(new SH_MoveState(_context, _stateMachine));
                return;
            }
            // Sync Movement_Blend with actual physics velocity so the Animator
            // blend tree reflects real movement even during an action.
            // This prevents the Locomotion state from fighting Action_Base.
            SyncMovementAnimation();

            // Tick the combat controller to process any attack input buffered during this action.
            _elapsedTime += Time.deltaTime;

            // The internal phase tracker runs alongside the bridge timer.
            // It drives physics (HandleImpulsePhysics reads _phase) and
            // provides a safety fallback in case the bridge callbacks are not
            // available (e.g. AnimatorBridge not yet initialized).
            UpdatePhase();
        }

        public override void PhysicsUpdate(float dt)
        {
            if (_abortedDueToInsufficientEnergy) return;

            if (dt <= 0f)
            {
                Debug.LogError($"[SH_ActionState] PhysicsUpdate: invalid dt ({dt}).");
                return;
            }

            _context.Physics.Tick(_context.Settings, dt);
            HandleImpulsePhysics();
        }

        public override void Exit()
        {
            // Unsubscribe from bridge callbacks before any other cleanup to prevent
            // OnActionComplete from firing a state transition after Exit() has run.
            UnsubscribeFromBridgeCallbacks();

            // Stop the bridge phase timer and reset Action layer speed.
            // Called regardless of whether the action completed naturally or was
            // interrupted, ensuring the bridge is in a clean state for the next action.
            _context.AnimatorBridge?.StopActionClip();

            // Ensure the hitbox is never left active after the state exits,
            // regardless of which phase was reached.
            _context.HitboxController?.DeactivateHitDetection();

            // Always clear invulnerability on exit regardless of which phase was reached,
            // preventing permanent immunity if the action is interrupted before HandleRecoveryBegin fires.
            if (_actionData != null && _actionData.grantsInvulnerability)
                _context.Health?.SetInvulnerable(false);

            // Clear movement lock on exit to ensure the player can move again after the action ends,
            // regardless of which phase was reached. This also prevents permanent movement lock
            // if the action is interrupted before the lock would normally be lifted.
            if (_actionData != null && _actionData.locksMovement)
            {
                _context.Physics.SetFrictionMultiplier(5f);
                _context.Locomotion.SetMovementLock(false);
            }
        }

        #endregion

        #region Animation Dispatch

        /// <summary>
        /// Routes the animation dispatch based on the action's trigger type.
        /// Dash actions use the legacy TriggerDash() path (unchanged behavior).
        /// All other actions use the new PlayActionClip() path via SH_ActionAnimationMap.
        /// </summary>
        private void DispatchAnimation()
        {
            if (_context.AnimatorBridge == null) return;

            string trigger = _actionData.animationTrigger;

            if (string.IsNullOrEmpty(trigger)) return;

            // --- Override Controller Path (attacks and all non-dash actions) ---

            // Resolve the clip from the animation map.
            // If the map is null or has no entry for this action, clip will be null.
            // PlayActionClip() handles null clips gracefully — the phase timer still
            // runs and all gameplay callbacks fire correctly without a visual clip.
            AnimationClip clip = _animationMap?.GetClip(_actionData);

            if (clip == null)
            {
                Debug.LogWarning(
                    $"[SH_ActionState] No clip found for action '{_actionData.name}' " +
                    $"in the assigned SH_ActionAnimationMap. " +
                    $"Assign a clip or a fallback clip to the map asset. " +
                    $"Gameplay callbacks will still fire correctly.");
            }

            // Subscribe to phase callbacks before PlayActionClip() so that
            // callbacks fired synchronously on the first Update() are not missed.
            SubscribeToBridgeCallbacks();

            _context.AnimatorBridge.PlayActionClip(
                clip,
                _actionData.TotalDuration,
                _actionData.startupTime,
                _actionData.activeTime);
            /*
            Debug.Log($"[SH_ActionState] Dispatched action '{_actionData.name}'. " +
                      $"Clip: '{(clip != null ? clip.name : "none")}'. " +
                      $"Total duration: {_actionData.TotalDuration:F2}s.");
            */
        }

        #endregion

        #region Bridge Callback Management

        private void SubscribeToBridgeCallbacks()
        {
            if (_callbacksSubscribed || _context.AnimatorBridge == null) return;

            _context.AnimatorBridge.OnActiveBegin += HandleActiveBegin;
            _context.AnimatorBridge.OnRecoveryBegin += HandleRecoveryBegin;
            _context.AnimatorBridge.OnActionComplete += HandleActionComplete;

            _callbacksSubscribed = true;
        }

        private void UnsubscribeFromBridgeCallbacks()
        {
            if (!_callbacksSubscribed || _context.AnimatorBridge == null) return;

            _context.AnimatorBridge.OnActiveBegin -= HandleActiveBegin;
            _context.AnimatorBridge.OnRecoveryBegin -= HandleRecoveryBegin;
            _context.AnimatorBridge.OnActionComplete -= HandleActionComplete;

            _callbacksSubscribed = false;
        }

        // ─── Callback Handlers ────────────────────────────────────────────

        /// <summary>
        /// Fired by SH_AnimatorBridge when startupTime has elapsed.
        /// Activates hit detection for the current committed attack type.
        /// This replaces the Unity Animation Event path for hitbox activation.
        /// </summary>
        private void HandleActiveBegin()
        {
            _context.CombatController?.ActivateHitDetection();

            if (_actionData.grantsInvulnerability)
                _context.Health?.SetInvulnerable(true);
        }

        /// <summary>
        /// Fired by SH_AnimatorBridge when startupTime + activeTime has elapsed.
        /// Deactivates hit detection and opens the cancel window.
        /// </summary>
        private void HandleRecoveryBegin()
        {
            _context.HitboxController?.DeactivateHitDetection();

            if (_actionData.grantsInvulnerability)
                _context.Health?.SetInvulnerable(false);
        }

        /// <summary>
        /// Fired by SH_AnimatorBridge when the full TotalDuration has elapsed.
        /// Transitions back to Idle to complete the action lifecycle.
        /// </summary>
        private void HandleActionComplete()
        {
            if (_transitionRequested) return;
            _transitionRequested = true;

            _stateMachine.RegisterActionCooldown(_actionData);
            _stateMachine.ChangeState(new SH_IdleState(_context, _stateMachine));
        }

        #endregion

        #region Phase Management (internal fallback tracker)

        /// <summary>
        /// Updates the internal phase based on elapsed time.
        /// This tracker is a safety fallback for physics and for scenarios where
        /// the bridge is unavailable. The authoritative activation callbacks are
        /// those fired by SH_AnimatorBridge.
        ///
        /// When the bridge is active, HandleActionComplete() will trigger the
        /// state transition. The Completed branch here only fires if the bridge
        /// callback has not already done so (guarded by _transitionRequested).
        /// </summary>
        private void UpdatePhase()
        {
            if (_elapsedTime < _startupEnd)
            {
                _phase = ActionPhase.Startup;
            }
            else if (_elapsedTime < _activeEnd)
            {
                _phase = ActionPhase.Active;
            }
            else if (_elapsedTime < _recoveryEnd)
            {
                _phase = ActionPhase.Recovery;
            }
            else
            {
                _phase = ActionPhase.Completed;

                // Fallback transition — only executes if the bridge callback has
                // not already requested the transition.
                if (!_transitionRequested)
                {
                    _transitionRequested = true;
                    _stateMachine.RegisterActionCooldown(_actionData);
                    _stateMachine.ChangeState(new SH_IdleState(_context, _stateMachine));
                }
            }
        }

        #endregion

        #region Newtonian Impulse

        private void HandleImpulsePhysics()
        {
            bool surgeActive = _context.CombatController != null && _context.CombatController.IsSurgeActive;
            _context.Physics.SetFrictionMultiplier(surgeActive ? 0.4f : 1f);

            if (_phase != ActionPhase.Active) return;
            if (_actionData.impulseMagnitude <= 0f) return;

            Vector3 direction = ResolveDirection();

            if (_actionData.impulseDuration <= 0f)
            {
                if (!_impulseApplied)
                {
                    _context.Physics.ApplyImpulse(
                        _context.Settings, direction * _actionData.impulseMagnitude);
                    _impulseApplied = true;
                }
            }
            else
            {
                _context.Physics.ApplyForce(
                    _context.Settings,
                    direction * _actionData.impulseMagnitude,
                    _actionData.impulseDuration);
            }
        }

        private Vector3 ResolveDirection()
        {
            switch (_actionData.directionMode)
            {
                case DirectionMode.Forward:
                    return _context.Transform.forward;
                case DirectionMode.InputDirection:
                    Vector3 inputDir = _context.Perspective.GetWorldSpaceDirection(
                        _context.Input.MoveInput);
                    return inputDir.sqrMagnitude > 0.01f
                        ? inputDir
                        : _context.Transform.forward;
                case DirectionMode.LockOnTarget:
                    return _context.Perspective.GetForward();
                case DirectionMode.Custom:
                    return _context.Transform
                        .TransformDirection(_actionData.customDirection)
                        .normalized;
                default:
                    return _context.Transform.forward;
            }
        }

        #endregion

        #region Animation Sync

        private void SyncMovementAnimation()
        {
            if (_context.AnimatorBridge == null) return;

            Vector3 velocity = _context.Physics.CurrentVelocity;
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            float normalizedSpeed = 0f;

            if (horizontalSpeed > _context.Settings.stopThreshold)
            {
                if (horizontalSpeed <= _context.Settings.walkSpeed)
                    normalizedSpeed = (horizontalSpeed / _context.Settings.walkSpeed) * 0.5f;
                else
                {
                    float t = Mathf.InverseLerp(
                        _context.Settings.walkSpeed,
                        _context.Settings.runSpeed,
                        horizontalSpeed);
                    normalizedSpeed = 0.5f + (t * 0.5f);
                }
            }

            _context.AnimatorBridge.UpdateMovement(normalizedSpeed);
        }

        #endregion
    }
}