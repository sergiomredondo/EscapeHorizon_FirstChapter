using UnityEngine;

namespace Core.StateMachine.States
{
    /// <summary>
    /// Active locomotion state.
    /// Orchestrates input projection into world-space forces and drives
    /// acceleration/rotation through the locomotion and physics controllers.
    ///
    /// Extended for combat (GDD §5.3 Stage A):
    ///   + Calls CombatController.Tick() first in Update() so attack input is
    ///     processed before dash and idle-return checks. An attack committed
    ///     while moving will request the action before the movement check fires,
    ///     ensuring the FSM transitions to SH_ActionState without dropping the input.
    /// </summary>
    public class SH_MoveState : SH_BaseState
    {
        public override int Priority => 1;

        private float _initialFriction;

        public SH_MoveState(SH_PlayerContext context, SH_PlayerStateMachine stateMachine)
            : base(context, stateMachine)
        {
            if (context == null) Debug.LogError("[SH_MoveState] context is null.");
            if (stateMachine == null) Debug.LogError("[SH_MoveState] stateMachine is null.");
        }

        public override void Enter()
        {
            _context.Locomotion.SetMovementLock(false);

            // Capture current friction for the smooth ramp from post-action states.
            _initialFriction = _context.Physics.frictionMultiplier;
        }

        public override void Update()
        {
            // 1. Interaction tick — detection scan + hold timer.
            _context.Interaction?.Tick();
            if (_context.Input.InteractPressed)
            {
                _context.Input.ConsumeInteractPressed();
                _context.Interaction?.NotifyInteractPressed();
            }
            if (_context.Input.InteractReleased)
            {
                _context.Input.ConsumeInteractReleased();
                _context.Interaction?.NotifyInteractReleased();
            }

            // 2. Combat tick — attack input before locomotion transitions.
            _context.CombatController?.Tick();

            // 3. Dash check.
            if (_context.Input.DashInput)
            {
                _stateMachine.RequestAction(_context.Settings.dashAction);
                return;
            }

            // 4. Return to Idle when movement input ceases.
            if (_context.Input.MoveInput.sqrMagnitude < 0.01f)
            {
                _stateMachine.ChangeState(new SH_IdleState(_context, _stateMachine));
                return;
            }

            SyncAnimationWithPhysics();
        }

        public override void PhysicsUpdate(float dt)
        {
            if (dt <= 0)
            {
                Debug.LogError($"[SH_MoveState] PhysicsUpdate: invalid dt ({dt}).");
                return;
            }

            // Smooth friction ramp from whatever value was inherited on Enter().
            float rampT = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(Time.time / Mathf.Max(0.01f, _context.Settings.accelerationTime)));
            _context.Physics.SetFrictionMultiplier(
                Mathf.Lerp(_initialFriction, 1f, rampT));

            _context.Locomotion.Tick(dt);
            _context.Physics.Tick(_context.Settings, dt);
        }

        public override void Exit()
        {
            // Intentionally empty: friction restoration is handled by the
            // destination state to avoid racing with the ramp.
        }

        private void SyncAnimationWithPhysics()
        {
            if (_context.AnimatorBridge == null) return;

            Vector3 velocity = _context.Physics.CurrentVelocity;
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            float normalizedSpeed = 0f;

            if (horizontalSpeed > 0f)
            {
                if (horizontalSpeed <= _context.Settings.walkSpeed)
                    normalizedSpeed = (horizontalSpeed / _context.Settings.walkSpeed) * 0.5f;
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
    }
}
