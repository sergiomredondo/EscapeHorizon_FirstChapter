using Actions.Data;
using Animation;
using Core.Camera;
using Core.Input;
using Core.Locomotion;
using Core.Physics;
using Core.StateMachine.States;
using Data;
using DebugTools;
using Game.Combat.Core;
using Game.Combat.Data;
using Game.Economy;
using Game.Economy.Data;
using Game.Interaction;
using Game.Interaction.Data;
using System.Collections.Generic;
using UnityEngine;

namespace Core.StateMachine
{
    /// <summary>
    /// Central orchestrator of the Mecha's behavior.
    /// Acts as the owner of the SH_PlayerContext (SSOT) and serializes all
    /// sub-system references for Inspector assignment.
    ///
    /// Extended for Stage B (GDD §5.3 Stage B):
    ///   + Two new serialized fields under 'Combat System Stage B':
    ///       _surgeSystem      — SH_EnergySurgeSystem (bar accumulation + auto-activation).
    ///       _difficultyManager — SH_DifficultyManager (zone scaling + dynamic AI loop).
    ///   + SH_PlayerContext constructor extended with both new arguments.
    /// </summary>
    [SelectionBase]
    [DisallowMultipleComponent]
    public class SH_PlayerStateMachine : MonoBehaviour
    {
        #region Dependencies — Movement & Control

        [Header("Movement & Control References")]
        [SerializeField] private SH_InputHandler _input;
        [SerializeField] private SH_PerspectiveController _perspective;
        [SerializeField] private SH_LocomotionController _locomotion;
        [SerializeField] private SH_PhysicsMotor _physics;
        [SerializeField] private SH_MovementSettings _settings;
        [SerializeField] private Animator _animator;
        [SerializeField] private SH_AnimatorBridge _animatorBridge;

        #endregion

        #region Dependencies — Economic Systems

        [Header("Economic System References")]
        [SerializeField] private SH_HealthComponent _health;
        [SerializeField] private SH_ResourceSystem _resources;
        [SerializeField] private SH_EconomicEventManager _economicEvents;
        [SerializeField] private SH_EconomySettings _economySettings;
        [SerializeField] private SH_EconomicEventSettings _economicEventSettings;

        #endregion

        #region Dependencies — Interaction System

        [Header("Interaction System References")]
        [SerializeField] private SH_InteractionController _interaction;
        [SerializeField] private SH_InteractionSettings _interactionSettings;

        #endregion

        #region Dependencies — Combat System Stage A

        [Header("Combat System References — Stage A")]

        [Tooltip("Player combat controller manages attack input, light/heavy classification, " +
                 "Energy Surge state, and routes OnHitImpact to the hitbox controller. " +
                 "Add SH_PlayerCombatController component to Bear.")]
        [SerializeField] private SH_PlayerCombatController _combatController;

        [Tooltip("Hitbox controller runs the per-hit OverlapSphere scan and delivers " +
                 "SH_DamagePayload to ICombatTarget entities. " +
                 "Add SH_HitboxController component to Bear.")]
        [SerializeField] private SH_HitboxController _hitboxController;

        [Tooltip("Central combat formula configuration asset. " +
                 "Create via ScapeHorizon/Settings/CombatSettings.")]
        [SerializeField] private SH_CombatSettings _combatSettings;

        [Tooltip("Player archetype base attribute sheet (Strength, Defense, Agility, PostureMax). " +
                 "Create via ScapeHorizon/Combat/CombatStats and name it PlayerStats.")]
        [SerializeField] private SH_CombatStats _playerCombatStats;

        #endregion

        #region Dependencies — Combat System Stage B

        [Header("Combat System References — Stage B")]

        [Tooltip("Energy Surge bar accumulation system. " +
                 "Fills from damage dealt/received. Auto-activates Surge at 100%. " +
                 "Add SH_EnergySurgeSystem component to Bear.")]
        [SerializeField] private SH_EnergySurgeSystem _surgeSystem;

        [Tooltip("Difficulty manager. Applies zone scaling to registered enemies and " +
                 "runs the 60-second dynamic AI aggressiveness loop (GDD §5.3.6). " +
                 "Add SH_DifficultyManager component to Bear (or a persistent manager object).")]
        [SerializeField] private SH_DifficultyManager _difficultyManager;

        #endregion

        #region Dependencies — Visualization

        [Header("Visualization Settings")]
        [SerializeField] private bool showOnScreenDebugging = true;

        #endregion

        #region Private State

        private SH_PlayerContext _context;
        private SH_BaseState _currentState;
        private SH_Debugger _physicsDebugger;

        private readonly Dictionary<SH_ActionData, float> _actionCooldowns =
            new Dictionary<SH_ActionData, float>();

        #endregion

        #region Initialization

        private void Awake()
        {
            ValidateDependencies();

            _context = new SH_PlayerContext(
                transform,
                _input,
                _perspective,
                _locomotion,
                _physics,
                _settings,
                _animator,
                _animatorBridge,
                this,
                _health,
                _resources,
                _economicEvents,
                _economySettings,
                _economicEventSettings,
                _interaction,
                _interactionSettings,
                _combatController,
                _hitboxController,
                _combatSettings,
                _playerCombatStats,
                _surgeSystem,          // Stage B
                _difficultyManager     // Stage B
            );

            if (showOnScreenDebugging)
            {
                _physicsDebugger = GetComponent<SH_Debugger>();
                if (_physicsDebugger != null)
                    _physicsDebugger.Initialize(_context);
            }
        }

        private void Start()
        {
            ChangeState(new SH_IdleState(_context, this));
            InjectContextToAllEnemies();
        }

        /// <summary>
        /// Finds all SH_EnemyController instances in the scene and injects
        /// the player context so their FSM ticks can detect and react to the player.
        /// For the prototype this runs once on Start. A proper spawn system will
        /// handle injection per-enemy at instantiation time in a later stage.
        /// </summary>
        private void InjectContextToAllEnemies()
        {
            var enemies = FindObjectsByType<Game.Enemy.SH_EnemyController>(FindObjectsSortMode.None);

            foreach (var enemy in enemies)
                enemy.SetPlayerContext(_context);

            if (enemies.Length > 0)
                Debug.Log($"[SH_PlayerStateMachine] Player context injected into {enemies.Length} enemy/enemies.");
            else
                Debug.LogWarning("[SH_PlayerStateMachine] No SH_EnemyController found in scene. " +
                                 "Place at least one enemy before pressing Play.");
        }

        private void Update()
        {
            if (_currentState == null) return;
            _currentState.HandleInput();
            _currentState.Update();
        }

        private void FixedUpdate()
        {
            if (_currentState == null) return;
            _currentState.PhysicsUpdate(Time.fixedDeltaTime);
        }

        #endregion

        #region State Management API

        public void ChangeState(SH_BaseState newState)
        {
            if (newState == null)
            {
                Debug.LogError($"[SH_PlayerStateMachine] ChangeState: null state. " +
                               $"Current: {_currentState?.GetType().Name ?? "None"}");
                return;
            }
            _currentState?.Exit();
            _currentState = newState;
            _currentState.Enter();
        }

        public bool RequestAction(SH_ActionData actionData)
        {
            if (actionData == null)
            {
                Debug.LogError("[SH_PlayerStateMachine] RequestAction: null actionData.");
                return false;
            }

            if (_actionCooldowns.TryGetValue(actionData, out float lastFinishTime))
            {
                if (Time.time < lastFinishTime + actionData.coolDownTime)
                    return false;
            }

            if (_currentState == null || actionData.priority >= _currentState.Priority)
            {
                _actionCooldowns[actionData] = Time.time;
                ChangeState(new SH_ActionState(_context, this, actionData));
                return true;
            }

            return false;
        }

        public void RegisterActionCooldown(SH_ActionData actionData)
        {
            if (actionData == null) return;
            _actionCooldowns[actionData] = Time.time;
        }

        #endregion

        #region Debug API

        public string GetCurrentStateName() =>
            _currentState != null ? _currentState.GetType().Name : "None";

        #endregion

        #region Internal Validation

        private void ValidateDependencies()
        {
            // Movement & Control
            if (_input == null) Debug.LogError($"[SH_PlayerStateMachine] SH_InputHandler not assigned on {gameObject.name}.");
            if (_perspective == null) Debug.LogError($"[SH_PlayerStateMachine] SH_PerspectiveController not assigned on {gameObject.name}.");
            if (_locomotion == null) Debug.LogError($"[SH_PlayerStateMachine] SH_LocomotionController not assigned on {gameObject.name}.");
            if (_physics == null) Debug.LogError($"[SH_PlayerStateMachine] SH_PhysicsMotor not assigned on {gameObject.name}.");
            if (_settings == null) Debug.LogError($"[SH_PlayerStateMachine] SH_MovementSettings not assigned on {gameObject.name}.");
            if (_animator == null) Debug.LogError($"[SH_PlayerStateMachine] Animator not assigned on {gameObject.name}.");
            if (_animatorBridge == null) Debug.LogError($"[SH_PlayerStateMachine] SH_AnimatorBridge not assigned on {gameObject.name}.");

            // Economic
            if (_health == null) Debug.LogError($"[SH_PlayerStateMachine] SH_HealthComponent not assigned on {gameObject.name}.");
            if (_resources == null) Debug.LogError($"[SH_PlayerStateMachine] SH_ResourceSystem not assigned on {gameObject.name}.");
            if (_economicEvents == null) Debug.LogError($"[SH_PlayerStateMachine] SH_EconomicEventManager not assigned on {gameObject.name}.");
            if (_economySettings == null) Debug.LogError($"[SH_PlayerStateMachine] SH_EconomySettings not assigned on {gameObject.name}.");
            if (_economicEventSettings == null) Debug.LogError($"[SH_PlayerStateMachine] SH_EconomicEventSettings not assigned on {gameObject.name}.");

            // Interaction
            if (_interaction == null) Debug.LogError($"[SH_PlayerStateMachine] SH_InteractionController not assigned on {gameObject.name}.");
            if (_interactionSettings == null) Debug.LogError($"[SH_PlayerStateMachine] SH_InteractionSettings not assigned on {gameObject.name}.");

            // Combat Stage A
            if (_combatController == null) Debug.LogError($"[SH_PlayerStateMachine] SH_PlayerCombatController not assigned on {gameObject.name}.");
            if (_hitboxController == null) Debug.LogError($"[SH_PlayerStateMachine] SH_HitboxController not assigned on {gameObject.name}.");
            if (_combatSettings == null) Debug.LogError($"[SH_PlayerStateMachine] SH_CombatSettings not assigned on {gameObject.name}.");
            if (_playerCombatStats == null) Debug.LogError($"[SH_PlayerStateMachine] SH_CombatStats (player) not assigned on {gameObject.name}.");

            // Combat Stage B
            if (_surgeSystem == null) Debug.LogError($"[SH_PlayerStateMachine] SH_EnergySurgeSystem not assigned on {gameObject.name}. Add component to Bear.");
            if (_difficultyManager == null) Debug.LogError($"[SH_PlayerStateMachine] SH_DifficultyManager not assigned on {gameObject.name}.");
        }

        #endregion

        #region Cleanup

        /// <summary> Disposes the player context to clean up event subscriptions and other resources. </summary>
        private void OnDestroy()
        {
            _context?.Dispose();
        }

        #endregion
    }
}
