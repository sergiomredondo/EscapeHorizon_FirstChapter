using UnityEngine;
using Actions.Data;
using Core.StateMachine;

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
        private readonly SH_ActionData _data;

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
        public override int Priority => _data.priority;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes the Action state with context and specific action parameters.
        /// </summary>
        public SH_ActionState(SH_PlayerContext context, SH_PlayerStateMachine stateMachine, SH_ActionData data)
            : base(context, stateMachine)
        {
            _data = data;
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
            _startupEnd = _data.startupTime;
            _activeEnd = _startupEnd + _data.activeTime;
            _recoveryEnd = _activeEnd + _data.recoveryTime;

            _phase = ActionPhase.Startup;

            // Suspension of locomotion logic if the action requires tactical commitment.
            if (_data.locksMovement)
            {
                _context.Locomotion.SetMovementLock(true);
            }

            // Visual synchronization: Trigger the specific animation defined in the data.
            if (!string.IsNullOrEmpty(_data.animationTrigger) && _context.Animator != null)
            {
                _context.Animator.SetTrigger(_data.animationTrigger);
            }
        }

        /// <summary>
        /// Updates the action's internal clock and manages phase transitions.
        /// </summary>
        public override void Update()
        {
            _elapsedTime += Time.deltaTime;
            UpdatePhase();
        }

        /// <summary>
        /// Processes physics-based forces (Impulses or Sustained Forces) during the active phase.
        /// </summary>
        /// <param name="dt">Fixed delta time for physical consistency.</param>
        public override void PhysicsUpdate(float dt)
        {
            // The Physics Motor must always tick to handle environmental forces (gravity/friction).
            _context.Physics.Tick(dt);

            // Logic for applying the action's specific kinetic energy.
            HandleImpulsePhysics();
        }

        /// <summary>
        /// Restores mecha systems to their default state before exiting.
        /// </summary>
        public override void Exit()
        {
            // Restores locomotion control to ensure the Mecha can move again after the action completes.
            if (_data.locksMovement)
            {
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

                // Return to Idle upon completion. The Idle state will then evaluate if it should switch to Move.
                _stateMachine.ChangeState(new SH_IdleState(_context, _stateMachine));
            }
        }

        #endregion

        #region Newtonian Impulse System

        /// <summary>
        /// Applies the action's physical impact to the Physics Motor based on the data definition.
        /// </summary>
        private void HandleImpulsePhysics()
        {
            if (_phase != ActionPhase.Active) return;
            if (_data.impulseMagnitude <= 0f) return;

            Vector3 direction = ResolveDirection();

            // Instant Impulse Application: Change in velocity (DeltaV = F/m).
            if (_data.impulseDuration <= 0f)
            {
                if (!_impulseApplied)
                {
                    _context.Physics.ApplyImpulse(direction * _data.impulseMagnitude);
                    _impulseApplied = true;
                }
            }
            // Sustained Force Application: Applied continuously over the specified duration.
            else
            {
                _context.Physics.ApplyForce(direction * _data.impulseMagnitude, _data.impulseDuration);
            }
        }

        /// <summary>
        /// Resolves the world-space direction vector based on the action's configured mode.
        /// </summary>
        private Vector3 ResolveDirection()
        {
            switch (_data.directionMode)
            {
                case DirectionMode.Forward:
                    return _context.Transform.forward;

                case DirectionMode.InputDirection:
                    // Delegates world-space resolution to the Perspective Controller for consistency.
                    Vector3 inputDir = _context.Perspective.GetWorldSpaceDirection(_context.Input.MoveInput);
                    return inputDir.sqrMagnitude > 0.01f ? inputDir : _context.Transform.forward;

                case DirectionMode.LockOnTarget:
                    // Direct forward vector from the Perspective authority (Camera or Target).
                    return _context.Perspective.GetForward();

                case DirectionMode.Custom:
                    return _context.Transform.TransformDirection(_data.customDirection).normalized;

                default:
                    return _context.Transform.forward;
            }
        }

        #endregion
    }
}