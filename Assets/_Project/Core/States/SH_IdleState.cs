using UnityEngine;

namespace Core.States
{
    /// <summary>
    /// Represents the passive state of the Mecha.
    /// In this state, no motor force is applied, allowing kinetic friction 
    /// and environmental forces to dissipate residual inertia naturally.
    /// </summary>
    public class SH_IdleState : SH_BaseState
    {
        private bool _isMoveInputDetected;

        public SH_IdleState(SH_PlayerStateMachine stateMachine, SH_PlayerContext context)
            : base(stateMachine, context) { }

        // -------------------------------------------------------
        // State Lifecycle
        // -------------------------------------------------------

        public override void Enter()
        {
#if UNITY_EDITOR
            Debug.Log("[FSM] Entering State: IDLE. Motor force neutralized, allowing inertial decay.");
#endif
            _isMoveInputDetected = false;
        }

        /// <summary>
        /// Analyzes player intent. If movement is detected, prepares for transition.
        /// </summary>
        public override void HandleInput()
        {
            _isMoveInputDetected = context.Input.MoveVector.sqrMagnitude > 0.01f;
        }

        /// <summary>
        /// Evaluates logic for exiting the idle state.
        /// Separating transition logic from input polling for FSM consistency.
        /// </summary>
        public override void Update()
        {
            if (_isMoveInputDetected)
            {
                stateMachine.ChangeState(stateMachine.MoveState);
            }
        }

        /// <summary>
        /// Continues physics simulation during the idle state.
        /// Delegates to the controller with zero direction and magnitude, 
        /// triggering Newtonian friction and gravity.
        /// </summary>
        public override void PhysicsUpdate()
        {
            // By passing Vector3.zero and 0f, the SH_CharacterController
            // understands that F_motor = 0, leaving F_net = F_friction.
            context.Controller.Move(
                Vector3.zero,
                0f,
                context.MovementSettings
            );
        }

        public override void Exit()
        {
            _isMoveInputDetected = false;
        }
    }
}