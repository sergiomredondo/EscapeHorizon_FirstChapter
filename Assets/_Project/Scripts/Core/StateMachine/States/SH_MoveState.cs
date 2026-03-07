using UnityEngine;

namespace Core.StateMachine.States
{
    /// <summary>
    /// Active locomotion state. 
    /// Orchestrates the projection of input into world-space and coordinates 
    /// the acceleration and rotation logic through the locomotion and physics controllers.
    /// </summary>
    public class SH_MoveState : SH_BaseState
    {
        #region Properties

        /// <summary> 
        /// Movement priority is set to 1. 
        /// Higher than Idle, but designed to be interrupted by combat actions or high-commitment maneuvers.
        /// </summary>
        public override int Priority => 1;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes the Move state with the architectural context.
        /// </summary>
        public SH_MoveState(SH_PlayerContext context, SH_PlayerStateMachine stateMachine)
            : base(context, stateMachine) 
        {
            if (context == null) { Debug.LogError($"[SH_MoveState] Construction failed: SH_PlayerContext reference is null. Ensure that a valid context is passed when instantiating states."); return; }
            if (stateMachine == null) { Debug.LogError($"[SH_MoveState] Construction failed: SH_PlayerStateMachine reference is null. Ensure that a valid state machine is passed when instantiating states."); return; }
        }

        #endregion

        #region Execution Lifecycle

        /// <summary>
        /// Prepares the sub-systems for active locomotion upon state entry.
        /// </summary>
        public override void Enter()
        {
            // Ensures the locomotion system is active and ready to process new force vectors.
            _context.Locomotion.SetMovementLock(false);
        }

        /// <summary>
        /// Frame-by-frame logic evaluation. 
        /// Monitors transition conditions and synchronizes visual feedback with physical reality.
        /// </summary>
        public override void Update()
        {
            // Transition Logic: If the dash input is detected, request the dash action and exit early to allow the state machine to handle the transition.
            if (_context.Input.DashInput)
            {
                _stateMachine.RequestAction(_context.Settings.dashAction);
                return;
            }

            // Transition Logic: If the movement input ceases (below the deadzone), return to Idle.
            if (_context.Input.MoveInput.sqrMagnitude < 0.01f)
            {
                _stateMachine.ChangeState(new SH_IdleState(_context, _stateMachine));
                return;
            }

            // Synchronization of the visual layer: 
            // We sample the actual world velocity from the Physics Motor to drive the animation blend tree.
            // This ensures foot-sliding is minimized by matching animation to physical displacement.
            SyncAnimationWithPhysics();
        }

        /// <summary>
        /// Physics-aligned update loop. 
        /// Sequentially processes input projection, locomotive acceleration, and Newtonian integration.
        /// </summary>
        /// <param name="dt">Fixed delta time for consistent acceleration calculations.</param>
        public override void PhysicsUpdate(float dt)
        {
            if (dt <= 0) { Debug.LogError($"[SH_MoveState] PhysicsUpdate failed: Invalid delta time (dt={dt}). Ensure that a positive fixed delta time is passed when calling PhysicsUpdate."); return; }

            // Calculates the necessary acceleration and target rotation based on the resolved direction.
            _context.Locomotion.Tick(dt);

            // Finalizes the movement by integrating gravity, friction, and the forces accumulated in the Tick.
            _context.Physics.Tick(_context.Settings, dt);
        }

        /// <summary>
        /// Cleanup before transitioning out of the movement state.
        /// </summary>
        public override void Exit()
        {
            // Reserved for potential state-specific cleanup (e.g., stopping movement particle effects).
        }

        #endregion

        #region Internal Logic

        /// <summary>
        /// Maps the current physical horizontal velocity to the Animator's speed parameters.
        /// </summary>
        private void SyncAnimationWithPhysics()
        {
            if (_context.Animator == null) return;

            // Extract the horizontal components of the current velocity to calculate movement magnitude.
            Vector3 velocity = _context.Physics.CurrentVelocity;
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;

            // Update the animator based on real physical speed rather than raw input magnitude.
            _context.Animator.SetFloat("MovementSpeed", horizontalSpeed);
        }

        #endregion
    }
}