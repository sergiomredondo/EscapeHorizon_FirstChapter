using Actions.Data;
using Animation;
using Core.Camera;
using Core.Input;
using Core.Locomotion;
using Core.Physics;
using Core.StateMachine.States;
using Data;
using DebugTools;
using Game.Economy;
using Game.Economy.Data;
using Game.Interaction;
using Game.Interaction.Data;
using System.Collections.Generic;
using UnityEngine;

namespace Core.StateMachine
{
    /// <summary>
    /// Acts as the owner of the SH_PlayerContext (SSOT).
    /// Orchestrates all lifecycle transitions and sub-system initialization.
    /// Extended to wire the Interaction sub-system into the context
    /// and expose InteractionSettings for the Inspector (GDD §5.2.1).
    /// </summary>
    public class SH_PlayerStateMachine : MonoBehaviour
    {
        #region Dependencies — Movement & Control

        [Header("Movement & Control References")]

        [Tooltip("Input handler is responsible for processing raw input and providing " +
                 "normalized movement vectors and action states.")]
        [SerializeField] private SH_InputHandler _input;

        [Tooltip("Perspective controller converts input vectors into world-space directions " +
                 "based on camera orientation.")]
        [SerializeField] private SH_PerspectiveController _perspective;

        [Tooltip("Locomotion controller translates input intentions into locomotive forces.")]
        [SerializeField] private SH_LocomotionController _locomotion;

        [Tooltip("Physics motor is responsible for velocity integration, gravity, and friction.")]
        [SerializeField] private SH_PhysicsMotor _physics;

        [Tooltip("Movement settings defines mass, speed limits, acceleration times, " +
                 "and friction coefficients. Assign the MovementSettings asset.")]
        [SerializeField] private SH_MovementSettings _settings;

        [Tooltip("Animator is responsible for visual feedback and skeletal state management.")]
        [SerializeField] private Animator _animator;

        [Tooltip("Animator bridge decouples state logic from animation implementations.")]
        [SerializeField] private SH_AnimatorBridge _animatorBridge;

        #endregion

        #region Dependencies — Economic Systems

        [Header("Economic System References")]

        [Tooltip("Health component manages the Mecha's structural integrity (Durability/HP).")]
        [SerializeField] private SH_HealthComponent _health;

        [Tooltip("Resource system manages IC, Scrap, and Energy resource state.")]
        [SerializeField] private SH_ResourceSystem _resources;

        [Tooltip("Economic event manager handles dynamic event lifecycle.")]
        [SerializeField] private SH_EconomicEventManager _economicEvents;

        [Tooltip("Economy settings is the central configuration asset for all economic constants. " +
                 "Assign the EconomySettings asset from Settings/Economy/.")]
        [SerializeField] private SH_EconomySettings _economySettings;

        [Tooltip("Economic event settings defines all tunable parameters for dynamic events. " +
                 "Assign the EconomicEventSettings asset from Settings/Economy/.")]
        [SerializeField] private SH_EconomicEventSettings _economicEventSettings;

        #endregion

        #region Dependencies — Interaction System (GDD §5.2)

        [Header("Interaction System References")]

        [Tooltip("Interaction controller manages detection, hold timers, and interaction " +
                 "resolution for all IInteractable world objects (GDD §5.2.1). " +
                 "Add SH_InteractionController component to the Bear GameObject.")]
        [SerializeField] private SH_InteractionController _interaction;

        [Tooltip("Interaction settings defines detection radius, hold durations, and " +
                 "accessibility options (GDD §5.2.1, §5.1.4). " +
                 "Assign the InteractionSettings asset from Settings/Interaction/.")]
        [SerializeField] private SH_InteractionSettings _interactionSettings;

        #endregion

        #region Dependencies — Visualization

        [Header("Visualization Settings")]

        [Tooltip("Toggle on-screen debugging information for the current state and " +
                 "physics telemetry.")]
        [SerializeField] private bool showOnScreenDebugging = true;

        #endregion

        #region Private State

        /// <summary> The Single Source of Truth for all state-shared data. </summary>
        private SH_PlayerContext _context;

        /// <summary> The currently active behavior state. </summary>
        private SH_BaseState _currentState;

        /// <summary> Optional reference to the physics debugger. </summary>
        private SH_Debugger _physicsDebugger;

        /// <summary> Tracks cooldown timers for actions. </summary>
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
                _health,
                _resources,
                _economicEvents,
                _economySettings,
                _economicEventSettings,
                _interaction,
                _interactionSettings
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
                Debug.LogError($"[SH_PlayerStateMachine] Attempted to change to a null state. " +
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
                Debug.LogError($"[SH_PlayerStateMachine] RequestAction called with null data.");
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

            // Economic Systems
            if (_health == null) Debug.LogError($"[SH_PlayerStateMachine] SH_HealthComponent not assigned on {gameObject.name}.");
            if (_resources == null) Debug.LogError($"[SH_PlayerStateMachine] SH_ResourceSystem not assigned on {gameObject.name}.");
            if (_economicEvents == null) Debug.LogError($"[SH_PlayerStateMachine] SH_EconomicEventManager not assigned on {gameObject.name}.");
            if (_economySettings == null) Debug.LogError($"[SH_PlayerStateMachine] SH_EconomySettings not assigned on {gameObject.name}.");
            if (_economicEventSettings == null) Debug.LogError($"[SH_PlayerStateMachine] SH_EconomicEventSettings not assigned on {gameObject.name}.");

            // Interaction System
            if (_interaction == null) Debug.LogError($"[SH_PlayerStateMachine] SH_InteractionController not assigned on {gameObject.name}. " +
                                                                  $"Add SH_InteractionController component to Bear.");
            if (_interactionSettings == null) Debug.LogError($"[SH_PlayerStateMachine] SH_InteractionSettings not assigned on {gameObject.name}. " +
                                                                  $"Assign InteractionSettings asset from Settings/Interaction/.");
        }

        #endregion
    }
}
