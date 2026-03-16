using Actions.Data;
using Core.StateMachine.States;
using Game.Economy;
using Game.Economy.Data;
using UnityEngine;

namespace Core.StateMachine.States
{
    /// <summary>
    /// Data-driven execution state for high-commitment actions (attacks, dashes, skills).
    ///
    /// Extended for combat (GDD §5.3 Stage A):
    ///   + On Enter(), if SH_ActionData.animationTrigger is "Attack" (or any non-dash trigger),
    ///     calls AnimatorBridge.TriggerAttack() to fire the attack animation. The clip's
    ///     Animation Event then calls OnHitImpact → SH_PlayerCombatController →
    ///     SH_HitboxController, completing the hit-detection pipeline.
    ///   + On abort (insufficient energy) and on Exit(), calls
    ///     HitboxController.DeactivateHitDetection() to ensure no lingering hit
    ///     registry from a cancelled action can bleed into the next attack.
    ///
    /// Trigger routing convention (SH_ActionData.animationTrigger):
    ///   "Dash"        → TriggerDash() path (existing behavior preserved).
    ///   "Attack" / *  → TriggerAttack() path (new in Stage A).
    ///   ""            → No animation trigger fired (free/passive actions).
    /// </summary>
    public class SH_ActionState : SH_BaseState
    {
        #region Private Fields

        private readonly SH_ActionData _actionData;
        private float _elapsedTime;
        private bool _impulseApplied;
        private bool _abortedDueToInsufficientEnergy;

        private float _startupEnd;
        private float _activeEnd;
        private float _recoveryEnd;

        private enum ActionPhase { Startup, Active, Recovery, Completed }
        private ActionPhase _phase;

        #endregion

        public override int Priority => _actionData.priority;

        public SH_ActionState(
            SH_PlayerContext context,
            SH_PlayerStateMachine stateMachine,
            SH_ActionData actionData)
            : base(context, stateMachine)
        {
            if (context == null) Debug.LogError("[SH_ActionState] context is null.");
            if (stateMachine == null) Debug.LogError("[SH_ActionState] stateMachine is null.");
            if (actionData == null) Debug.LogError("[SH_ActionState] actionData is null.");
            _actionData = actionData;
        }

        #region Lifecycle

        public override void Enter()
        {
            _abortedDueToInsufficientEnergy = false;
            _elapsedTime = 0f;
            _impulseApplied = false;

            // --- Economic Gate ---
            if (_actionData.staminaCost > 0f)
            {
                SH_ResourceSystem resources = _context.Resources;
                if (resources == null)
                {
                    Debug.LogWarning(
                        $"[SH_ActionState] SH_ResourceSystem is null — skipping energy check " +
                        $"for '{_actionData.name}'.");
                }
                else
                {
                    bool consumed = resources.ConsumeResource(
                        ResourceType.EnergyCore, _actionData.staminaCost);

                    if (!consumed)
                    {
                        Debug.Log(
                            $"[SH_ActionState] '{_actionData.name}' aborted: " +
                            $"need {_actionData.staminaCost:F1} EC, " +
                            $"have {resources.CurrentEnergy:F1} EC.");

                        _abortedDueToInsufficientEnergy = true;
                        _context.HitboxController?.DeactivateHitDetection();
                        return;
                    }
                }
            }

            // --- Standard Init ---
            _startupEnd = _actionData.startupTime;
            _activeEnd = _startupEnd + _actionData.activeTime;
            _recoveryEnd = _activeEnd + _actionData.recoveryTime;
            _phase = ActionPhase.Startup;

            if (_actionData.locksMovement)
            {
                _context.Physics.SetFrictionMultiplier(0f);
                _context.Locomotion.SetMovementLock(true);
            }

            // --- Animation Trigger ---
            // Route based on the trigger name stored in the ActionData asset.
            // "Dash" keeps the existing dash animation path.
            // Any other non-empty trigger (including "Attack") fires TriggerAttack().
            string trigger = _actionData.animationTrigger;
            if (!string.IsNullOrEmpty(trigger))
            {
                if (trigger == "Dash")
                    _context.AnimatorBridge?.TriggerDash(1f);
                else
                    _context.AnimatorBridge?.TriggerAttack();
            }
        }

        public override void Update()
        {
            if (_abortedDueToInsufficientEnergy)
            {
                _stateMachine.ChangeState(new SH_MoveState(_context, _stateMachine));
                return;
            }

            _elapsedTime += Time.deltaTime;
            UpdatePhase();

            if (_context.AnimatorBridge == null) return;
            SyncAnimationWithPhysics();
        }

        public override void PhysicsUpdate(float dt)
        {
            if (_abortedDueToInsufficientEnergy) return;
            if (dt <= 0f)
            {
                Debug.LogError($"[SH_ActionState] PhysicsUpdate: invalid dt ({dt}).");
                return;
            }
            _context.Physics.Tick(_context.Settings, dt);
            HandleImpulsePhysics();
        }

        public override void Exit()
        {
            if (_abortedDueToInsufficientEnergy) return;

            // Deactivate hitbox so no stale registry survives into the next action.
            _context.HitboxController?.DeactivateHitDetection();

            if (_actionData.locksMovement)
            {
                _context.Physics.SetFrictionMultiplier(5f);
                _context.Locomotion.SetMovementLock(false);
            }
        }

        #endregion

        #region Phase Management

        private void UpdatePhase()
        {
            if (_elapsedTime < _startupEnd)
                _phase = ActionPhase.Startup;
            else if (_elapsedTime < _activeEnd)
                _phase = ActionPhase.Active;
            else if (_elapsedTime < _recoveryEnd)
                _phase = ActionPhase.Recovery;
            else
            {
                _phase = ActionPhase.Completed;
                _stateMachine.RegisterActionCooldown(_actionData);
                _context.AnimatorBridge?.TriggerDash(0f);
                _stateMachine.ChangeState(new SH_IdleState(_context, _stateMachine));
            }
        }

        #endregion

        #region Newtonian Impulse

        private void HandleImpulsePhysics()
        {
            _context.Physics.SetFrictionMultiplier(1f);
            if (_phase != ActionPhase.Active) return;
            if (_actionData.impulseMagnitude <= 0f) return;

            Vector3 direction = ResolveDirection();

            if (_actionData.impulseDuration <= 0f)
            {
                if (!_impulseApplied)
                {
                    _context.Physics.ApplyImpulse(
                        _context.Settings, direction * _actionData.impulseMagnitude);
                    _impulseApplied = true;
                }
            }
            else
            {
                _context.Physics.ApplyForce(
                    _context.Settings,
                    direction * _actionData.impulseMagnitude,
                    _actionData.impulseDuration);
            }
        }

        private Vector3 ResolveDirection()
        {
            switch (_actionData.directionMode)
            {
                case DirectionMode.Forward:
                    return _context.Transform.forward;
                case DirectionMode.InputDirection:
                    Vector3 inputDir = _context.Perspective.GetWorldSpaceDirection(
                        _context.Input.MoveInput);
                    return inputDir.sqrMagnitude > 0.01f
                        ? inputDir
                        : _context.Transform.forward;
                case DirectionMode.LockOnTarget:
                    return _context.Perspective.GetForward();
                case DirectionMode.Custom:
                    return _context.Transform
                        .TransformDirection(_actionData.customDirection)
                        .normalized;
                default:
                    return _context.Transform.forward;
            }
        }

        #endregion

        #region Animation Sync

        private void SyncAnimationWithPhysics()
        {
            if (_phase == ActionPhase.Completed) return;

            Vector3 velocity = _context.Physics.CurrentVelocity;
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            float normalizedSpeed = horizontalSpeed <= _context.Settings.runSpeed
                ? 0.5f : 1f;

            _context.AnimatorBridge.TriggerDash(normalizedSpeed);
        }

        #endregion
    }
}
