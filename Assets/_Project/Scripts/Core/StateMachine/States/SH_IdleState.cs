using UnityEngine;

namespace Core.StateMachine.States
{
    /// <summary>
    /// Default resting state of the Mecha.
    /// Manages physical stability, processes residual momentum, and evaluates
    /// transitions to active movement.
    ///
    /// Extended to integrate the interaction system (GDD §5.2.1):
    ///   - Ticks SH_InteractionController every frame for detection and hold timer.
    ///   - Forwards InteractPressed / InteractReleased flags from SH_InputHandler.
    ///   - Consumes flags after forwarding to prevent double-firing.
    ///
    /// Interaction is valid in Idle because a stationary Bear is the natural
    /// posture for extracting a Captive Core (high-commitment hold action).
    /// The hold timer is NOT interrupted by the Idle→Move transition;
    /// SH_MoveState also ticks the controller so the hold continues seamlessly.
    /// Interruption is handled by SH_InteractionController itself (range break
    /// and damage events) rather than by state transitions.
    /// </summary>
    public class SH_IdleState : SH_BaseState
    {
        #region Properties

        /// <summary>
        /// Idle priority is set to the minimum value (0).
        /// Any locomotion or combat state can take control immediately.
        /// </summary>
        public override int Priority => 0;

        #endregion

        #region Constructor

        public SH_IdleState(SH_PlayerContext context, SH_PlayerStateMachine stateMachine)
            : base(context, stateMachine)
        {
            if (context == null)
                Debug.LogError("[SH_IdleState] Construction failed: SH_PlayerContext is null.");
            if (stateMachine == null)
                Debug.LogError("[SH_IdleState] Construction failed: SH_PlayerStateMachine is null.");
        }

        #endregion

        #region Execution Lifecycle

        /// <summary>
        /// Ensures movement locks are released and visual state is reset to resting values.
        /// </summary>
        public override void Enter()
        {
            _context.Locomotion.SetMovementLock(false);

            if (_context.AnimatorBridge != null)
                _context.AnimatorBridge.UpdateMovement(0f);
        }

        /// <summary>
        /// Evaluates transition conditions and ticks the interaction system every frame.
        ///
        /// Execution order:
        ///   1. Tick interaction controller (detection scan + hold timer).
        ///   2. Forward and consume interact input flags.
        ///   3. Evaluate high-priority transitions (Dash, Move).
        ///   4. Sync animator with physics.
        ///
        /// Interaction tick is placed first so the controller always has an updated
        /// focus candidate before input forwarding is processed in the same frame.
        /// </summary>
        public override void Update()
        {
            // --- 1. Interaction System Tick ---
            if (_context.Interaction != null)
            {
                _context.Interaction.Tick();

                // Forward press/release flags and consume them to prevent double-firing
                // in subsequent frames or in SH_MoveState if a transition occurs.
                if (_context.Input.InteractPressed)
                {
                    _context.Interaction.NotifyInteractPressed();
                    _context.Input.ConsumeInteractPressed();
                }

                if (_context.Input.InteractReleased)
                {
                    _context.Interaction.NotifyInteractReleased();
                    _context.Input.ConsumeInteractReleased();
                }
            }

            // --- 2. High-Priority Transition: Dash ---
            if (_context.Input.DashInput)
            {
                _stateMachine.RequestAction(_context.Settings.dashAction);
                return;
            }

            // --- 3. Transition: Move ---
            if (_context.Input.MoveInput.sqrMagnitude > 0.01f)
            {
                _stateMachine.ChangeState(new SH_MoveState(_context, _stateMachine));
                return;
            }

            // --- 4. Animator Sync ---
            SyncAnimationWithPhysics();
        }

        /// <summary>
        /// Applies gravity and friction to maintain grounding and dissipate residual velocity.
        /// </summary>
        public override void PhysicsUpdate(float dt)
        {
            if (dt <= 0)
            {
                Debug.LogError($"[SH_IdleState] PhysicsUpdate: invalid delta time ({dt}).");
                return;
            }
            _context.Physics.Tick(_context.Settings, dt);
        }

        /// <summary>
        /// Restores default friction multiplier before transitioning out.
        /// Does NOT interrupt an active hold — the hold continues in SH_MoveState
        /// if the player begins moving during extraction (by design).
        /// </summary>
        public override void Exit()
        {
            _context.Physics.SetFrictionMultiplier(1f);
        }

        #endregion

        #region Internal Logic

        private void SyncAnimationWithPhysics()
        {
            if (_context.AnimatorBridge == null) return;

            Vector3 velocity = _context.Physics.CurrentVelocity;
            float horizontalSpeed = new UnityEngine.Vector2(velocity.x, velocity.z).magnitude;

            float normalizedSpeed = 0f;
            if (horizontalSpeed > 0)
            {
                if (horizontalSpeed <= _context.Settings.walkSpeed)
                {
                    normalizedSpeed = (horizontalSpeed / _context.Settings.walkSpeed) * 0.5f;
                }
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
