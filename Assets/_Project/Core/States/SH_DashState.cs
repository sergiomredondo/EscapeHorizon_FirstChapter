using UnityEngine;
using Data;

namespace Core.States
{
    /// <summary>
    /// Implements high-intensity dash behavior using a Newtonian model.
    /// Applies an instantaneous kinetic energy injection (Δv) and 
    /// synchronizes state duration with the physics loop.
    /// </summary>
    public class SH_DashState : SH_BaseState
    {
        private float _dashTimer;
        private float _lastDashTime;
        private Vector3 _dashDirection;

        private MovementSettings Settings => context.MovementSettings;

        public SH_DashState(SH_PlayerStateMachine stateMachine, SH_PlayerContext context)
            : base(stateMachine, context) { }

        // -------------------------------------------------------
        // Validation Logic
        // -------------------------------------------------------

        public override bool CanEnter()
        {
            return Time.time >= _lastDashTime + Settings.dashCooldown;
        }

        public override bool CanExit()
        {
            return _dashTimer <= 0f;
        }

        // -------------------------------------------------------
        // State Lifecycle
        // -------------------------------------------------------

        public override void Enter()
        {
#if UNITY_EDITOR
            Debug.Log("[FSM] Starting DASH: Inertial impulse synchronized with physics.");
#endif

            // Explicit protection against invalid mass settings
            if (Settings.mass <= 0f)
            {
                Debug.LogError("[DashState] Invalid Mass: Value must be greater than zero.");
                _dashTimer = 0f;
                return;
            }

            _dashTimer = Settings.dashDuration;
            _lastDashTime = Time.time;

            DetermineDashDirection();

            // Calculation of velocity change (Δv): Δv = (F * t) / m
            float impulseMagnitude = Settings.dashForce * Settings.dashDuration;
            float deltaV = impulseMagnitude / Settings.mass;

            Vector3 deltaVelocityVector = _dashDirection * deltaV;

            // Direct injection of kinetic energy into the Newtonian controller
            context.Controller.AddForce(deltaVelocityVector);
        }

        public override void HandleInput()
        {
            // Physical Commitment: Active locomotion input is ignored during dash to simulate mass.
        }

        public override void Update()
        {
            // Intentionally empty. Timer logic is handled in PhysicsUpdate to maintain temporal coherence.
        }

        public override void PhysicsUpdate()
        {
            // Timer synchronized with the fixed physics engine step
            _dashTimer -= Time.fixedDeltaTime;

            // Allow the controller to process environmental physics during the impulse:
            // - Gravity integration
            // - Kinetic friction
            // - Residual inertia
            context.Controller.Move(Vector3.zero, 0f, Settings);

            if (_dashTimer <= 0f)
            {
                EvaluateTransition();
            }
        }

        public override void Exit()
        {
            // Reset internal state to ensure deterministic re-entry
            _dashDirection = Vector3.zero;
        }

        // -------------------------------------------------------
        // Internal Logic
        // -------------------------------------------------------

        private void EvaluateTransition()
        {
            if (context.Input.MoveVector.sqrMagnitude > 0.01f)
                stateMachine.ChangeState(stateMachine.MoveState);
            else
                stateMachine.ChangeState(stateMachine.IdleState);
        }

        /// <summary>
        /// Calculates the world-space dash direction based on player input and camera perspective.
        /// Falls back to character forward if no input is present.
        /// </summary>
        private void DetermineDashDirection()
        {
            Vector2 input = context.Input.MoveVector;

            if (input.sqrMagnitude > 0.01f)
            {
                Transform camTransform = context.PerspectiveController?.ActiveCameraTransform;

                if (camTransform != null)
                {
                    Vector3 forward = camTransform.forward;
                    Vector3 right = camTransform.right;

                    forward.y = 0f;
                    right.y = 0f;

                    _dashDirection = (forward.normalized * input.y +
                                     right.normalized * input.x).normalized;
                }
                else
                {
                    _dashDirection = new Vector3(input.x, 0f, input.y).normalized;
                }
            }
            else
            {
                _dashDirection = context.Controller.transform.forward;
            }
        }
    }
}