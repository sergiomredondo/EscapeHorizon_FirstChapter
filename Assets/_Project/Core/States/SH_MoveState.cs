using UnityEngine;

namespace Core.States
{
    /// <summary>
    /// Governs the standard locomotion state of the Mecha unit.
    /// Translates analytical player intent into world-space vectors relative 
    /// to the active perspective, delegating physical resolution to the SH_CharacterController.
    /// </summary>
    public class SH_MoveState : SH_BaseState
    {
        // Internal state variables for deterministic data flow between phases
        private Vector3 _worldDirection;
        private float _inputMagnitude;

        public SH_MoveState(SH_PlayerStateMachine stateMachine, SH_PlayerContext context)
            : base(stateMachine, context) { }

        public override void Enter()
        {
#if UNITY_EDITOR
            Debug.Log("[FSM] Entering MOVE State: Initializing locomotion integration.");
#endif
        }

        /// <summary>
        /// Analysis Phase: Decodes raw input into a normalized directional intent.
        /// Strict separation ensures that state transitions do not pollute the input capture.
        /// </summary>
        public override void HandleInput()
        {
            Vector2 moveInput = context.Input.MoveVector;
            _inputMagnitude = Mathf.Clamp01(moveInput.magnitude);

            // Intent threshold to prevent micro-input noise interference
            if (_inputMagnitude > 0.01f)
            {
                _worldDirection = CalculatePerspectiveDirection(moveInput);
            }
            else
            {
                _worldDirection = Vector3.zero;
            }
        }

        /// <summary>
        /// Decision Phase: Evaluates transition criteria to return to Idle.
        /// Decoupled from physics to maintain logic consistency across varying frame rates.
        /// </summary>
        public override void Update()
        {
            if (_inputMagnitude <= 0.01f)
            {
                stateMachine.ChangeState(stateMachine.IdleState);
            }
        }

        /// <summary>
        /// Execution Phase: Injects motor force into the Newtonian controller.
        /// Velocity persistence and inertial decay are handled by the physical integration layer.
        /// </summary>
        public override void PhysicsUpdate()
        {
            context.Controller.Move(
                _worldDirection,
                _inputMagnitude,
                context.MovementSettings
            );
        }

        /// <summary>
        /// Projects 2D input space onto the horizontal XZ plane of the active camera.
        /// Ensures spatial consistency between player perception and Mecha orientation.
        /// </summary>
        private Vector3 CalculatePerspectiveDirection(Vector2 input)
        {
            // Null-safety check for asynchronous camera initialization
            Transform camTransform = context.PerspectiveController?.ActiveCameraTransform;

            if (camTransform == null)
            {
                // Fallback to global axes to maintain system availability
                return new Vector3(input.x, 0f, input.y).normalized;
            }

            // Extraction of planar direction vectors (discarding Y-axis to prevent vertical tilt)
            Vector3 forward = camTransform.forward;
            Vector3 right = camTransform.right;

            forward.y = 0f;
            right.y = 0f;

            forward.Normalize();
            right.Normalize();

            // Resulting world-space unit of directional will
            return (forward * input.y + right * input.x).normalized;
        }

        /// <summary>
        /// Cleanup Phase: Resets internal state to ensure a deterministic re-entry.
        /// </summary>
        public override void Exit()
        {
            _worldDirection = Vector3.zero;
            _inputMagnitude = 0f;
        }
    }
}