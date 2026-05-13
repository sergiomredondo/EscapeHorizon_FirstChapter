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
using Game.Progression;
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
        [Tooltip("Central input handler. Routes player input to the current state. " +
                 "Add SH_InputHandler component to Bear and set up bindings.")]
        [SerializeField] private SH_InputHandler _input;
        [Tooltip("Perspective controller manages camera modes and view rotation. " +
                 "Add SH_PerspectiveController component to Bear and assign the main camera.")]
        [SerializeField] private SH_PerspectiveController _perspective;
        [Tooltip("Locomotion controller handles movement logic and state transitions. " +
                 "Add SH_LocomotionController component to Bear.")]
        [SerializeField] private SH_LocomotionController _locomotion;
        [Tooltip("Physics motor applies movement forces and handles collision. " +
                 "Add SH_PhysicsMotor component to Bear and configure colliders/rigidbody.")]
        [SerializeField] private SH_PhysicsMotor _physics;
        [Tooltip("Movement settings asset. Configure walk/run speeds, acceleration, etc. " +
                 "Create via ScapeHorizon/Settings/MovementSettings and assign it here.")]
        [SerializeField] private SH_MovementSettings _settings;
        [Tooltip("Animator component for controlling animations. " +
                 "Add Animator component to Bear and set up the controller with movement/combat animations.")]
        [SerializeField] private Animator _animator;
        [Tooltip("Animator bridge for syncing animation events and parameters. " +
                 "Add SH_AnimatorBridge component to Bear.")]
        [SerializeField] private SH_AnimatorBridge _animatorBridge;
        [Tooltip("Maps each SH_ActionData to its AnimationClip for the player entity. " +
         "Create via ScapeHorizon/Animation/ActionAnimationMap.")]
        [SerializeField] private SH_ActionAnimationMap _actionAnimationMap;
        [Tooltip("World-space position and rotation the camera moves to during the defeat cinematic. " +
         "Place an empty GameObject in the scene at the desired cinematic angle and assign it here.")]
        [SerializeField] private Transform _defeatCameraTarget;
        [Tooltip("Duration in seconds of the defeat cinematic before the player resets.")]
        [Min(1f)]
        [SerializeField] private float _defeatSequenceDuration = 3f;
        [Tooltip("Animation trigger string sent to Bear's Animator when defeated.")]
        [SerializeField] private string _defeatAnimationTrigger = "Defeat";
        [Tooltip("World-space spawn point where the player resets after defeat. " +
                 "Assign an empty GameObject placed at the level start position.")]
        [SerializeField] private Transform _spawnPoint;

        #endregion

        #region Dependencies — Economic Systems

        [Header("Economic System References")]
        [Tooltip("Health component manages Durability/HP and defeat state. " +
                 "Add SH_HealthComponent to Bear.")]
        [SerializeField] private SH_HealthComponent _health;
        [Tooltip("Resource system manages Scrap and other player resources. " +
                 "Add SH_ResourceSystem component to Bear.")]
        [SerializeField] private SH_ResourceSystem _resources;
        [Tooltip("Economic event manager handles global economic events and their effects. " +
                 "Add SH_EconomicEventManager component to Bear (or a persistent manager object).")]
        [SerializeField] private SH_EconomicEventManager _economicEvents;
        [Tooltip("Economy settings asset. Configure global economic parameters and scaling. " +
                 "Create via ScapeHorizon/Settings/EconomySettings and assign it here.")]
        [SerializeField] private SH_EconomySettings _economySettings;
        [Tooltip("Economic event settings asset. Configure specific economic events, triggers, and effects. " +
                 "Create via ScapeHorizon/Settings/EconomicEventSettings and assign it here.")]
        [SerializeField] private SH_EconomicEventSettings _economicEventSettings;

        #endregion

        #region Dependencies — Interaction System

        [Header("Interaction System References")]
        [Tooltip("Interaction controller manages interactable detection, hold timers, and event routing. " +
                 "Add SH_InteractionController component to Bear.")]
        [SerializeField] private SH_InteractionController _interaction;
        [Tooltip("Interaction settings asset. Configure interaction ranges, hold durations, and other parameters. " +
                 "Create via ScapeHorizon/Settings/InteractionSettings and assign it here.")]
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

        [Tooltip("Visual and audio feedback data for player hit and defeat reactions.")]
        [SerializeField] private SH_PlayerFeedbackData _playerFeedbackData;

        #endregion

        #region Dependencies — Combat System Stage B

        [Header("Combat System References — Stage B")]

        [Tooltip("Energy Surge bar accumulation system. " +
                 "Fills from damage dealt/received. Auto-activates Surge at 100%. " +
                 "Add SH_EnergySurgeSystem component to Bear.")]
        [SerializeField] private SH_EnergySurgeSystem _surgeSystem;

        [Tooltip("Build system for the Analysis Tree " +
         "Add SH_BuildSystem component to Bear.")]
        [SerializeField] private SH_BuildSystem _buildSystem;

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

        public SH_PlayerContext GetContext() => _context;

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
                _actionAnimationMap,
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
                _playerFeedbackData,
                _surgeSystem,
                _buildSystem,
                _difficultyManager
            );

            _context.Health.OnDefeated += HandlePlayerDefeated;

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

        private void HandlePlayerDefeated()
        {
            ChangeState(new SH_DeathSequenceState(
                _context,
                this,
                _defeatAnimationTrigger,
                _defeatSequenceDuration,
                _defeatCameraTarget,
                _spawnPoint));
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
                ChangeState(new SH_ActionState(_context, this, actionData, _actionAnimationMap));
                return true;
            }

            return false;
        }

        public bool RequestSurge()
        {
            if (_settings.surgeAction == null)
            {
                Debug.LogWarning("[SH_PlayerStateMachine] RequestSurge: surgeAction is not assigned in SH_MovementSettings.");
                return false;
            }

            if (_context.SurgeSystem == null || !_context.SurgeSystem.CanActivateSurge)
                return false;

            if (_currentState != null && _settings.surgeAction.priority < _currentState.Priority)
                return false;

            ChangeState(new SH_SurgeState(_context, this, _settings.surgeAction, _actionAnimationMap));
            return true;
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
            if (_buildSystem == null) Debug.LogWarning($"[SH_PlayerStateMachine] SH_BuildSystem not assigned on {gameObject.name}. ");
            if (_difficultyManager == null) Debug.LogError($"[SH_PlayerStateMachine] SH_DifficultyManager not assigned on {gameObject.name}.");

            if (_spawnPoint == null)
                Debug.LogWarning($"[SH_PlayerStateMachine] No spawn point assigned on {gameObject.name}. " +
                                 $"Player will reset to origin on defeat.");
        }

        #endregion

        #region Cleanup

        /// <summary> Disposes the player context to clean up event subscriptions and other resources. </summary>
        private void OnDestroy()
        {
            if (_context?.Health != null)
                _context.Health.OnDefeated -= HandlePlayerDefeated;
            _context?.Dispose();
        }

        #endregion
    }
}
