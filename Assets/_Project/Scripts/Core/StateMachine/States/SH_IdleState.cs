using UnityEngine;

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
            : base(context, stateMachine) 
        {
            if (context == null) { Debug.LogError($"[SH_IdleState] Construction failed: SH_PlayerContext reference is null. Ensure that a valid context is passed when instantiating states."); return; }
            if (stateMachine == null) { Debug.LogError($"[SH_IdleState] Construction failed: SH_PlayerStateMachine reference is null. Ensure that a valid state machine is passed when instantiating states."); return; }
        }

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
            if (_context.AnimatorBridge != null)
            {
                _context.AnimatorBridge.UpdateMovement(0f);
            }
        }

        /// <summary>
        /// Evaluates transition conditions every frame. 
        /// Monitors the Input Handler for movement vectors exceeding the deadzone threshold.
        /// </summary>
        public override void Update()
        {
            // High-Priority Transition Check: If the player initiates a dash input, we immediately request the dash action.
            // This allows the Mecha to respond to burst movement commands without delay, even from an idle state.
            if (_context.Input.DashInput)
            {
                _stateMachine.RequestAction(_context.Settings.dashAction);
                return;
            }

            // Transition Logic: If the player provides movement intent via the Input Handler, switch to MoveState.
            // We use sqrMagnitude for performance optimization during threshold checks.
            if (_context.Input.MoveInput.sqrMagnitude > 0.01f)
            {
                // Transition to MoveState (to be implemented in the next architectural step).
                _stateMachine.ChangeState(new SH_MoveState(_context, _stateMachine));
                return;
            }
            // Synchronization of the visual layer: 
            // We sample the actual world velocity from the Physics Motor to drive the animation blend tree.
            // This ensures foot-sliding is minimized by matching animation to physical displacement.
            SyncAnimationWithPhysics();
        }

        /// <summary>
        /// Processes physical integration while at rest. 
        /// Ensures gravity and friction are applied to maintain grounding and dissipate any existing kinetic energy.
        /// </summary>
        /// <param name="dt">Fixed delta time injected by the StateMachine.</param>
        public override void PhysicsUpdate(float dt)
        {
            if (dt <= 0) { Debug.LogError($"[SH_IdleState] PhysicsUpdate failed: Invalid delta time value ({dt}). Ensure that a positive, non-zero value is passed when calling PhysicsUpdate."); return; }

            // Ticks the Physics Motor to integrate gravity and friction based on the MovementSettings asset.
            // This ensures the Mecha remains grounded and stops naturally if it had residual velocity.
            _context.Physics.Tick(_context.Settings, dt);
        }

        /// <summary>
        /// Cleanup logic before exiting the Idle state.
        /// </summary>
        public override void Exit()
        {
            // Restores the default friction multiplier to allow normal movement responsiveness in the next state.
            _context.Physics.SetFrictionMultiplier(1f);
        }

        #endregion

        #region Internal Logic

        /// <summary>
        /// Maps the current physical horizontal velocity to the Animator's speed parameters.
        /// </summary>
        private void SyncAnimationWithPhysics()
        {
            if (_context.AnimatorBridge == null) return;

            // Extract the horizontal components of the current velocity to calculate movement magnitude.
            Vector3 velocity = _context.Physics.CurrentVelocity;
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;

            float normalizedSpeed = 0f;
            // Normalizes the horizontal speed to a 0-1 range based on walk and run thresholds defined in the MovementSettings.
            if (horizontalSpeed > 0)
            {
                if (horizontalSpeed <= _context.Settings.walkSpeed)
                {
                    normalizedSpeed = (horizontalSpeed / _context.Settings.walkSpeed) * 0.5f;
                }
                else
                {
                    float t = Mathf.InverseLerp(_context.Settings.walkSpeed, _context.Settings.runSpeed, horizontalSpeed);
                    normalizedSpeed = 0.5f + (t * 0.5f);
                }
            }

            // Updates the Animator with the normalized horizontal speed to drive the blend tree, ensuring that the visual
            // representation matches the physical movement and reduces foot-sliding.
            _context.AnimatorBridge.UpdateMovement(normalizedSpeed);
        }

        #endregion
    }
}