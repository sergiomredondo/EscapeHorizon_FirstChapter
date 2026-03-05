using UnityEngine;
using Core.StateMachine;

namespace Core.StateMachine.States
{
    /// <summary>
    /// Default resting state of the Mecha. 
    /// Manages physical stability, processes residual momentum, and evaluates transitions to active movement.
    /// Represents the lowest hierarchy level, interruptible by any intentional player action.
    /// </summary>
    public class SH_IdleState : SH_BaseState
    {
        #region Properties

        /// <summary> 
        /// Idle priority is set to the minimum value (0). 
        /// This ensures any locomotion or combat state can take control of the Mecha immediately.
        /// </summary>
        public override int Priority => 0;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes the Idle state with the global player context.
        /// </summary>
        public SH_IdleState(SH_PlayerContext context, SH_PlayerStateMachine stateMachine)
            : base(context, stateMachine) { }

        #endregion

        #region Execution Lifecycle

        /// <summary>
        /// Entry point for the Idle state. 
        /// Ensures movement locks are released and visual states are reset to resting values.
        /// </summary>
        public override void Enter()
        {
            // Unlocks locomotion processing to allow the Mecha to respond to future input intent.
            _context.Locomotion.SetMovementLock(false);

            // Resets the animator's speed parameter to synchronize the visual layer with the logical resting state.
            if (_context.Animator != null)
            {
                _context.Animator.SetFloat("MovementSpeed", 0f);
            }
        }

        /// <summary>
        /// Evaluates transition conditions every frame. 
        /// Monitors the Input Handler for movement vectors exceeding the deadzone threshold.
        /// </summary>
        public override void Update()
        {
            // Transition Logic: If the player provides movement intent via the Input Handler, switch to MoveState.
            // We use sqrMagnitude for performance optimization during threshold checks.
            if (_context.Input.MoveInput.sqrMagnitude > 0.01f)
            {
                // Transition to MoveState (to be implemented in the next architectural step).
                _stateMachine.ChangeState(new SH_MoveState(_context, _stateMachine));
            }
        }

        /// <summary>
        /// Processes physical integration while at rest. 
        /// Ensures gravity and friction are applied to maintain grounding and dissipate any existing kinetic energy.
        /// </summary>
        /// <param name="dt">Fixed delta time injected by the StateMachine.</param>
        public override void PhysicsUpdate(float dt)
        {
            // Ticks the Physics Motor to integrate gravity and friction based on the MovementSettings asset.
            // This ensures the Mecha remains grounded and stops naturally if it had residual velocity.
            _context.Physics.Tick(dt);
        }

        /// <summary>
        /// Cleanup logic before exiting the Idle state.
        /// </summary>
        public override void Exit()
        {
            // No specific cleanup required for Idle, but kept for architectural consistency.
        }

        #endregion
    }
}