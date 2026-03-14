using Actions.Data;
using Core.StateMachine.States;
using Game.Economy;
using UnityEngine;
using UnityEngine.ProBuilder.Shapes;
using static UnityEditor.ShaderData;
using static UnityEngine.EventSystems.EventTrigger;

namespace Core.StateMachine.States
{
    /// <summary>
    /// Data-driven execution state for high-commitment actions (Attacks, Dashes, Skills).
    /// Orchestrates deterministic phase timing and integrates Newtonian impulse
    /// application via the SH_PhysicsMotor.
    ///
    /// Extended to consume Energy (EC) from SH_ResourceSystem on Enter().
    /// If the pilot lacks sufficient Energy to cover the action's staminaCost,
    /// the action is immediately aborted and control returns to SH_IdleState.
    /// This enforces the economic constraint defined in GDD §5.5.1 without
    /// modifying the phase lifecycle or the physics pipeline.
    /// </summary>
    public class SH_ActionState : SH_BaseState
    {
        #region Private Execution Fields

        /// <summary> Source data defining the physical and temporal parameters of the action. </summary>
        private readonly SH_ActionData _actionData;

        /// <summary> Accumulated time since the state was entered. </summary>
        private float _elapsedTime;

        /// <summary> Flag to ensure discrete impulses are applied only once during the active phase. </summary>
        private bool _impulseApplied;

        /// <summary>
        /// Flag set to true when Enter() aborts due to insufficient Energy.
        /// Causes Update() and PhysicsUpdate() to skip all logic until
        /// the state machine processes the transition to SH_IdleState.
        /// </summary>
        private bool _abortedDueToInsufficientEnergy;

        // --- Phase Timestamps ---
        private float _startupEnd;
        private float _activeEnd;
        private float _recoveryEnd;

        private enum ActionPhase { Startup, Active, Recovery, Completed }
        private ActionPhase _phase;

        #endregion

        #region Properties

        /// <summary>
        /// Action priority is derived directly from the ActionData asset.
        /// This allows designers to tune which actions can interrupt others.
        /// </summary>
        public override int Priority => _actionData.priority;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes the Action state with context and specific action parameters.
        /// </summary>
        public SH_ActionState(
            SH_PlayerContext context,
            SH_PlayerStateMachine stateMachine,
            SH_ActionData actionData)
            : base(context, stateMachine)
        {
            if (context == null) { Debug.LogError($"[SH_ActionState] Construction failed: SH_PlayerContext is null."); return;}
            if (stateMachine == null) { Debug.LogError($"[SH_ActionState] Construction failed: SH_PlayerStateMachine is null."); return;}
            if (actionData == null) { Debug.LogError($"[SH_ActionState] Construction failed: SH_ActionData is null."); return;}

            _actionData = actionData;
        }

        #endregion

        #region Execution Lifecycle

        /// <summary>
        /// Initializes the action timeline and triggers initial visual/logic locks.
        ///
        /// Economic gate (GDD §5.5.1):
        /// Before committing to the action, attempts to consume the staminaCost
        /// from SH_ResourceSystem. If the resource system is unavailable or the
        /// pilot lacks sufficient Energy, the action is aborted immediately and
        /// control returns to SH_IdleState on the next Update() tick.
        /// A staminaCost of zero bypasses the energy check entirely, allowing
        /// free actions (e.g., passive dashes with no cost) to execute unconditionally.
        /// </summary>
        public override void Enter()
        {
            _abortedDueToInsufficientEnergy = false;
            _elapsedTime = 0f;
            _impulseApplied = false;

            _context.Physics.SetFrictionMultiplier(1f);

            // --- Economic Gate ---
            if (_actionData.staminaCost > 0f)
            {
                SH_ResourceSystem resources = _context.Resources;

                if (resources == null)
                {
                    Debug.LogWarning($"[SH_ActionState] SH_ResourceSystem is null. " +
                                     $"Skipping energy check for '{_actionData.name}'. " +
                                     $"Assign SH_ResourceSystem to SH_PlayerContext.");
                }
                else
                {
                    bool consumed = resources.ConsumeResource(
                        Game.Economy.Data.ResourceType.EnergyCore,
                        _actionData.staminaCost);

                    if (!consumed)
                    {
                        Debug.Log($"[SH_ActionState] Action '{_actionData.name}' aborted: " +
                                  $"insufficient Energy. " +
                                  $"Required: {_actionData.staminaCost:F1} EC. " +
                                  $"Available: {resources.CurrentEnergy:F1} EC.");

                        _abortedDueToInsufficientEnergy = true;
                        return;
                    }
                }
            }

            // --- Standard Initialization (only reached if energy check passed) ---

            // Timeline assembly based on the provided data asset.
            _startupEnd = _actionData.startupTime;
            _activeEnd = _startupEnd + _actionData.activeTime;
            _recoveryEnd = _activeEnd + _actionData.recoveryTime;

            _phase = ActionPhase.Startup;

            // Suspension of locomotion if the action requires tactical commitment.
            if (_actionData.locksMovement)
            {
                _context.Locomotion.SetMovementLock(true);
            }
        }

        /// <summary>
        /// Updates the action's internal clock and manages phase transitions.
        /// Exits immediately to SH_IdleState if the action was aborted on Enter().
        /// </summary>
        public override void Update()
        {
            // If Enter() aborted due to insufficient energy, transition out immediately.
            // The return guard prevents any phase logic from executing on an uncommitted action.
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

        /// <summary>
        /// Processes physics-based forces during the active phase.
        /// Skips all physics logic if the action was aborted on Enter().
        /// </summary>
        public override void PhysicsUpdate(float dt)
        {
            if (_abortedDueToInsufficientEnergy) return;
            if (dt <= 0f) { Debug.LogError($"[SH_ActionState] PhysicsUpdate: invalid delta time ({dt})."); return;}

            _context.Physics.Tick(_context.Settings, dt);
            HandleImpulsePhysics();
        }

        /// <summary>
        /// Restores Mecha systems to their default state before exiting.
        /// Only restores locomotion locks if the action was not aborted,
        /// since an aborted action never acquired the lock in the first place.
        /// </summary>
        public override void Exit()
        {
            if (_abortedDueToInsufficientEnergy)
                return;

            if (_actionData.locksMovement)
            {
                _context.Physics.SetFrictionMultiplier(5f);
                _context.Locomotion.SetMovementLock(false);
            }
        }

        #endregion

        #region Phase Management

        /// <summary>
        /// Manages the transition between action phases and triggers state completion.
        /// </summary>
        private void UpdatePhase()
        {
            if (_elapsedTime < _startupEnd)
            {
                _phase = ActionPhase.Startup;
            }
            else if (_elapsedTime < _activeEnd)
            {
                _phase = ActionPhase.Active;
            }
            else if (_elapsedTime < _recoveryEnd)
            {
                _phase = ActionPhase.Recovery;
            }
            else
            {
                _phase = ActionPhase.Completed;

                _stateMachine.RegisterActionCooldown(_actionData);
                _context.AnimatorBridge.TriggerDash(0f);
                _stateMachine.ChangeState(new SH_MoveState(_context, _stateMachine));
                return;
            }
        }

        #endregion

        #region Newtonian Impulse System

        /// <summary>
        /// Applies the action's physical impact to the Physics Motor
        /// based on the data definition.
        /// </summary>
        private void HandleImpulsePhysics()
        {
            if (_phase != ActionPhase.Active) return;
            if (_actionData.impulseMagnitude <= 0f) return;

            Vector3 direction = ResolveDirection();

            if (_actionData.impulseDuration <= 0f)
            {
                if (!_impulseApplied)
                {
                    _context.Physics.ApplyImpulse(
                        _context.Settings,
                        direction * _actionData.impulseMagnitude);
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

        /// <summary>
        /// Resolves the world-space direction vector based on the action's configured mode.
        /// </summary>
        private Vector3 ResolveDirection()
        {
            switch (_actionData.directionMode)
            {
                case DirectionMode.Forward:
                    return _context.Transform.forward;

                case DirectionMode.InputDirection:
                    Vector3 inputDir = _context.Perspective.GetWorldSpaceDirection(
                        _context.Input.MoveInput);
                    return inputDir.sqrMagnitude > 0.01f ? inputDir : _context.Transform.forward;

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

        #region Internal Logic

        /// <summary>
        /// Maps the current physical horizontal velocity to the Animator's speed parameters.
        /// </summary>
        private void SyncAnimationWithPhysics()
        {
            if (_context.AnimatorBridge == null || _phase == ActionPhase.Completed)
                return;

            Vector3 velocity = _context.Physics.CurrentVelocity;
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;

            float normalizedSpeed = horizontalSpeed <= _context.Settings.runSpeed ? 0.5f : 1f;

            _context.AnimatorBridge.TriggerDash(normalizedSpeed);
        }

        #endregion
    }
}