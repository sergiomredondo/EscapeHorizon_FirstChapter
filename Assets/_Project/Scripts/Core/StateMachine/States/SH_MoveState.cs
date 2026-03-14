using Actions.Data;
using Unity.Mathematics;
using UnityEngine;

namespace Core.StateMachine.States
{
    /// <summary>
    /// Active locomotion state.
    /// Orchestrates the projection of input into world-space and coordinates
    /// acceleration and rotation through the locomotion and physics controllers.
    ///
    /// Extended to integrate the interaction system (GDD §5.2.1):
    ///   - Ticks SH_InteractionController every frame for detection and hold timer.
    ///   - Forwards InteractPressed / InteractReleased flags from SH_InputHandler.
    ///
    /// The interaction tick continues in MoveState so that:
    ///   a) A hold started in IdleState persists through slow movement
    ///      (the controller's range-break logic handles interruption by distance).
    ///   b) The player can initiate an interaction while approaching a target.
    ///
    /// Note: SH_InteractionController.breakOnRangeExit controls whether movement
    /// that takes the player out of detection range cancels an active hold.
    /// If true (default), fast movement away from target interrupts extraction.
    /// </summary>
    public class SH_MoveState : SH_BaseState
    {
        #region Dependencies

        private float _accelerationTimer;
        private float _initialFriction;

        #endregion

        #region Properties

        /// <summary>
        /// Movement priority is 1.
        /// Higher than Idle, but interruptible by combat actions or Dash.
        /// </summary>
        public override int Priority => 1;

        #endregion

        #region Constructor

        public SH_MoveState(SH_PlayerContext context, SH_PlayerStateMachine stateMachine)
            : base(context, stateMachine)
        {
            if (context == null)
                Debug.LogError("[SH_MoveState] Construction failed: SH_PlayerContext is null.");
            if (stateMachine == null)
                Debug.LogError("[SH_MoveState] Construction failed: SH_PlayerStateMachine is null.");
        }

        #endregion

        #region Execution Lifecycle

        /// <summary>
        /// Ensures the locomotion system is active on state entry.
        /// </summary>
        public override void Enter()
        {
            _context.Locomotion.SetMovementLock(false);
            _accelerationTimer = 0f;
            _initialFriction = _context.Physics.frictionMultiplier;
        }

        /// <summary>
        /// Frame-by-frame logic evaluation.
        ///
        /// Execution order:
        ///   1. Tick interaction controller (detection scan + hold timer continuation).
        ///   2. Forward and consume interact input flags.
        ///   3. Evaluate high-priority transitions (Dash, stop → Idle).
        ///   4. Sync animator with physics.
        /// </summary>
        public override void Update()
        {
            // --- Friction Reduction for Smooth Acceleration ---
            if (_initialFriction > 1f)
            {
                if (_accelerationTimer < _context.Settings.accelerationTime)
                {
                    _accelerationTimer += Time.deltaTime;
                    float t = Mathf.Clamp01(_accelerationTimer / _context.Settings.accelerationTime);
                    float smoohedT = Mathf.SmoothStep(0f, 1f, t);
                    float currentFriction = Mathf.Lerp(_initialFriction, 1f, smoohedT);
                    _context.Physics.SetFrictionMultiplier(currentFriction);
                }
            }

            // --- 1. Interaction System Tick ---
            if (_context.Interaction != null)
            {
                _context.Interaction.Tick();

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

            // --- 3. Transition: Stop → Idle ---
            if (_context.Input.MoveInput.sqrMagnitude < 0.01f)
            {
                _stateMachine.ChangeState(new SH_IdleState(_context, _stateMachine));
                return;
            }

            // --- 4. Animator Sync ---
            SyncAnimationWithPhysics();

        }

        /// <summary>
        /// Processes input projection, locomotive acceleration, and Newtonian integration.
        /// </summary>
        public override void PhysicsUpdate(float dt)
        {
            if (dt <= 0)
            {
                Debug.LogError($"[SH_MoveState] PhysicsUpdate: invalid delta time ({dt}).");
                return;
            }
            _context.Locomotion.Tick(dt);
            _context.Physics.Tick(_context.Settings, dt);
        }

        /// <summary>
        /// Exits the state, resetting any modified parameters to ensure a clean slate for the next state.
        /// </summary>
        public override void Exit()
        {
            
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
