using UnityEngine;

namespace Core.StateMachine.States
{
    /// <summary>
    /// Default resting state of the Mecha.
    /// Manages physical stability, processes residual momentum, and evaluates
    /// transitions to active movement and combat.
    ///
    /// Extended for combat (GDD §5.3 Stage A):
    ///   + Calls CombatController.Tick() first in Update() to process attack input
    ///     and hold-duration classification before any transition checks.
    ///     This ensures a tap-attack input committed from Idle is captured before
    ///     the movement check could redirect to SH_MoveState.
    /// </summary>
    public class SH_IdleState : SH_BaseState
    {
        public override int Priority => 0;

        public SH_IdleState(SH_PlayerContext context, SH_PlayerStateMachine stateMachine)
            : base(context, stateMachine)
        {
            if (context == null) Debug.LogError("[SH_IdleState] context is null.");
            if (stateMachine == null) Debug.LogError("[SH_IdleState] stateMachine is null.");
        }

        public override void Enter()
        {
            _context.Locomotion.SetMovementLock(false);

            if (_context.AnimatorBridge != null)
                _context.AnimatorBridge.UpdateMovement(0f);
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

            // 2. Combat tick — attack input reading and hold-duration classification.
            //    Must run before dash/move checks so a committed attack action
            //    takes priority over locomotion transitions.
            _context.CombatController?.Tick();

            // 3. Dash check — high-priority burst movement.
            if (_context.Input.DashInput)
            {
                _stateMachine.RequestAction(_context.Settings.dashAction);
                return;
            }

            // 4. Move transition — if movement input exceeds deadzone.
            if (_context.Input.MoveInput.sqrMagnitude > 0.01f)
            {
                _stateMachine.ChangeState(new SH_MoveState(_context, _stateMachine));
                return;
            }

            // 5. Animation sync with actual physics velocity.
            SyncAnimationWithPhysics();
        }

        public override void PhysicsUpdate(float dt)
        {
            if (dt <= 0)
            {
                Debug.LogError($"[SH_IdleState] PhysicsUpdate: invalid dt ({dt}).");
                return;
            }
            _context.Physics.Tick(_context.Settings, dt);
        }

        public override void Exit()
        {
            _context.Physics.SetFrictionMultiplier(1f);
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
