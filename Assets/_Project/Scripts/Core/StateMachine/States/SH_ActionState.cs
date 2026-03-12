using UnityEngine;
using Actions.Data;

namespace Core.StateMachine.States
{
    /// <summary>
    /// Data-driven execution state for high-commitment actions (Attacks, Dashes, Skills).
    /// Orchestrates deterministic phase timing and integrates Newtonian impulse 
    /// application via the SH_PhysicsMotor.
    /// </summary>
    public class SH_ActionState : SH_BaseState
    {
        #region Private Execution Fields

        /// <summary> Source data defining the physical and temporal parameters of the action. </summary>
        private readonly SH_ActionData _actionData;

        /// <summary> Accumulated time since the state was entered. </summary>
        private float _elapsedTime;

        /// <summary> Flag to ensure discrete impulses are applied only once during the active phase. </summary>
        private bool _impulseApplied;

        // --- Phase Timestamps ---
        private float _startupEnd;
        private float _activeEnd;
        private float _recoveryEnd;

        private enum ActionPhase { Startup, Active, Recovery, Completed }
        private ActionPhase _phase;

        #endregion

        #region Properties

        /// <summary> 
        /// Action priority is derived directly from the ActionData asset. 
        /// This allows designers to tune which actions can interrupt others.
        /// </summary>
        public override int Priority => _actionData.priority;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes the Action state with context and specific action parameters.
        /// </summary>
        public SH_ActionState(SH_PlayerContext context, SH_PlayerStateMachine stateMachine, SH_ActionData actionData)
            : base(context, stateMachine)
        {
            if (context == null) { Debug.LogError($"[SH_actionState] Construction failed: SH_PlayerContext reference is null. Ensure that a valid context is passed when instantiating states."); return; }
            if (stateMachine == null) { Debug.LogError($"[SH_ActionState] Construction failed: SH_PlayerStateMachine reference is null. Ensure that a valid state machine is passed when instantiating states."); return; }
            if (actionData == null) { Debug.LogError($"[SH_ActionState] Construction failed: SH_ActionData reference is null. Ensure that a valid SH_ActionData asset is passed when instantiating action states."); return; }

            _actionData = actionData;
        }

        #endregion

        #region Execution Lifecycle

        /// <summary>
        /// Initializes the action timeline and triggers initial visual/logic locks.
        /// </summary>
        public override void Enter()
        {
            _elapsedTime = 0f;
            _impulseApplied = false;

            // Timeline assembly based on provided Data asset.
            _startupEnd = _actionData.startupTime;
            _activeEnd = _startupEnd + _actionData.activeTime;
            _recoveryEnd = _activeEnd + _actionData.recoveryTime;

            _phase = ActionPhase.Startup;

            // Suspension of locomotion logic if the action requires tactical commitment.
            if (_actionData.locksMovement)
            {
                _context.Physics.SetFrictionMultiplier(0f);
                _context.Locomotion.SetMovementLock(true);
            }
        }

        /// <summary>
        /// Updates the action's internal clock and manages phase transitions.
        /// </summary>
        public override void Update()
        {
            _elapsedTime += Time.deltaTime;
            UpdatePhase();

            // Trigger the appropriate animation state. The Animator Bridge will handle the transition to the correct animation based on the current action and phase.
            if (_context.AnimatorBridge == null) return;
            SyncAnimationWithPhysics();
            
        }

        /// <summary>
        /// Processes physics-based forces (Impulses or Sustained Forces) during the active phase.
        /// </summary>
        /// <param name="dt">Fixed delta time for physical consistency.</param>
        public override void PhysicsUpdate(float dt)
        {
            if (dt <= 0f) { Debug.LogError($"[SH_ActionState] PhysicsUpdate failed: Invalid delta time value ({dt}). Ensure that a positive, non-zero value is passed when calling PhysicsUpdate."); return; }

            // The Physics Motor must always tick to handle environmental forces (gravity/friction).
            _context.Physics.Tick(_context.Settings, dt);

            // Logic for applying the action's specific kinetic energy.
            HandleImpulsePhysics();
        }

        /// <summary>
        /// Restores mecha systems to their default state before exiting.
        /// </summary>
        public override void Exit()
        {
            // Restores locomotion control to ensure the Mecha can move again after the action completes.
            if (_actionData.locksMovement)
            {
                _context.Physics.SetFrictionMultiplier(5f);
                _context.Locomotion.SetMovementLock(false);
            }
        }

        #endregion

        #region Phase Management

        /// <summary>
        /// Manages the transition between action phases and triggers state completion.
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

                // Registers the action's cooldown with the state machine to prevent immediate re-use.
                _stateMachine.RegisterActionCooldown(_actionData);

                // Triggers the appropriate animation state for the transition out of the action.
                _context.AnimatorBridge.TriggerDash(0f);

                // Return to Idle upon completion. The Idle state will then evaluate if it should switch to Move.
                _stateMachine.ChangeState(new SH_IdleState(_context, _stateMachine));
                return;
            }
        }

        #endregion

        #region Newtonian Impulse System

        /// <summary>
        /// Applies the action's physical impact to the Physics Motor based on the data definition.
        /// </summary>
        private void HandleImpulsePhysics()
        {
            _context.Physics.SetFrictionMultiplier(1f);
            if (_phase != ActionPhase.Active) return;
            if (_actionData.impulseMagnitude <= 0f) return;

            Vector3 direction = ResolveDirection();

            // Instant Impulse Application: Change in velocity (DeltaV = F/m).
            if (_actionData.impulseDuration <= 0f)
            {
                if (!_impulseApplied)
                {
                    _context.Physics.ApplyImpulse(_context.Settings, direction * _actionData.impulseMagnitude);
                    _impulseApplied = true;
                }
            }
            // Sustained Force Application: Applied continuously over the specified duration.
            else
            {
                _context.Physics.ApplyForce(_context.Settings, direction * _actionData.impulseMagnitude, _actionData.impulseDuration);
            }
        }

        /// <summary>
        /// Resolves the world-space direction vector based on the action's configured mode.
        /// </summary>
        private Vector3 ResolveDirection()
        {
            switch (_actionData.directionMode)
            {
                // Default forward direction of the Mecha.
                case DirectionMode.Forward:
                    return _context.Transform.forward;

                // Direction based on player input, transformed to world space. Falls back to forward if input is negligible.
                case DirectionMode.InputDirection:
                    Vector3 inputDir = _context.Perspective.GetWorldSpaceDirection(_context.Input.MoveInput);
                    return inputDir.sqrMagnitude > 0.01f ? inputDir : _context.Transform.forward;

                // Direction towards the current lock-on target. Falls back to forward if no target is locked.
                case DirectionMode.LockOnTarget:
                    return _context.Perspective.GetForward();

                // Custom direction defined in the ActionData, transformed to world space. Normalized to ensure consistent magnitude.
                case DirectionMode.Custom:
                    return _context.Transform.TransformDirection(_actionData.customDirection).normalized;

                // Fallback to forward direction if the mode is unrecognized (should not happen if data is validated).
                default:
                    return _context.Transform.forward;
            }
        }

        #endregion

        #region Internal Logic

        /// <summary>
        /// Maps the current physical horizontal velocity to the Animator's speed parameters.
        /// </summary>
        private void SyncAnimationWithPhysics()
        {
            if (_context.AnimatorBridge == null || _phase == ActionPhase.Completed) return;

            // Extract the horizontal components of the current velocity to calculate movement magnitude.
            Vector3 velocity = _context.Physics.CurrentVelocity;
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;

            float normalizedSpeed = 0f;
            // Maps the horizontal speed to a normalized value for the Animator. This allows the dash animation to reflect the actual movement speed,
            // enhancing visual feedback and reducing foot-sliding.
            if (horizontalSpeed <= _context.Settings.runSpeed)
            {
                normalizedSpeed = 0.5f;
            }
            else
            {
                normalizedSpeed = 1f;
            }
            
            // Updates the Animator with the normalized horizontal speed to drive the blend tree, ensuring that the visual
            // representation matches the physical movement and reduces foot-sliding.
            _context.AnimatorBridge.TriggerDash(normalizedSpeed);
        }

        #endregion
    }
}