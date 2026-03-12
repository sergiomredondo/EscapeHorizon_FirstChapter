using Actions.Data;
using Animation;
using Core.Camera;
using Core.Input;
using Core.Locomotion;
using Core.Physics;
using Core.StateMachine;
using Core.StateMachine.States;
using Data;
using DebugTools;
using Game.Economy;
using Game.Economy.Data;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ProBuilder.Shapes;
using static UnityEditor.ShaderData;
using static UnityEngine.EventSystems.EventTrigger;

namespace Core.StateMachine
{
    /// <summary>
    /// Central orchestrator of the Mecha's behavior.
    /// Manages state transitions, ensures dependency injection into states,
    /// and arbitrates action requests based on a priority-driven system.
    /// Acts as the owner of the SH_PlayerContext (SSOT).
    ///
    /// Extended to serialize and inject the five economic sub-system references
    /// required by SH_PlayerContext: SH_HealthComponent, SH_ResourceSystem,
    /// SH_EconomicEventManager, SH_EconomySettings, and SH_EconomicEventSettings.
    /// </summary>
    [SelectionBase]
    [DisallowMultipleComponent]
    public class SH_PlayerStateMachine : MonoBehaviour
    {
        #region Dependencies — Movement & Control

        [Header("Movement & Control References")]

        [Tooltip("Input handler is responsible for processing raw input and providing normalized movement vectors and action states. Use SH_InputHandler component.")]
        [SerializeField] private SH_InputHandler _input;

        [Tooltip("Perspective controller is responsible for converting input vectors into world space directions based on camera orientation. Use SH_PerspectiveController component.")]
        [SerializeField] private SH_PerspectiveController _perspective;

        [Tooltip("Locomotion controller is responsible for translating input intentions into locomotive forces. Use SH_LocomotionController component.")]
        [SerializeField] private SH_LocomotionController _locomotion;

        [Tooltip("Physics motor is responsible for velocity integration, gravity, and friction. Use SH_PhysicsMotor component.")]
        [SerializeField] private SH_PhysicsMotor _physics;

        [Tooltip("Movement settings defines mass, speed limits, acceleration times, and friction coefficients. Use SH_MovementSettings asset.")]
        [SerializeField] private SH_MovementSettings _settings;

        [Tooltip("Animator is responsible for visual feedback and skeletal state management. Use the Animator component on the Mecha model.")]
        [SerializeField] private Animator _animator;

        [Tooltip("Animator bridge is an abstraction layer for animator interactions, decoupling state logic from specific animation implementations. Use SH_AnimatorBridge component.")]
        [SerializeField] private SH_AnimatorBridge _animatorBridge;

        #endregion

        #region Dependencies — Economic Systems

        [Header("Economic System References")]

        [Tooltip("Health component manages the Mecha's structural integrity (Durability/HP). Fires events on damage, repair, critical state, and defeat. Use SH_HealthComponent on the Bear GameObject.")]
        [SerializeField] private SH_HealthComponent _health;

        [Tooltip("Resource system manages IC, Scrap, and Energy resource state. Central authority for all economic operations. Use SH_ResourceSystem on the Bear GameObject.")]
        [SerializeField] private SH_ResourceSystem _resources;

        [Tooltip("Economic event manager handles dynamic event lifecycle: IC Scarcity, Reconfiguration Overload, and Energy Flux. Use SH_EconomicEventManager on the Bear GameObject.")]
        [SerializeField] private SH_EconomicEventManager _economicEvents;

        [Tooltip("Economy settings is the central configuration asset for all economic constants: resource caps, progression curves, defeat penalties, and Durability thresholds. Assign the EconomySettings asset from Settings/Economy/.")]
        [SerializeField] private SH_EconomySettings _economySettings;

        [Tooltip("Economic event settings defines all tunable parameters for dynamic events: scarcity coefficients, overload thresholds, and flux probabilities. Assign the EconomicEventSettings asset from Settings/Economy/.")]
        [SerializeField] private SH_EconomicEventSettings _economicEventSettings;

        #endregion

        #region Dependencies — Visualization

        [Header("Visualization Settings")]

        [Tooltip("Toggle on-screen debugging information for the current state and physics telemetry. Requires SH_Debugger component on the same GameObject.")]
        [SerializeField] private bool showOnScreenDebugging = true;

        #endregion

        #region Private State

        /// <summary> The Single Source of Truth for all state-shared data. </summary>
        private SH_PlayerContext _context;

        /// <summary> The currently active behavior state. </summary>
        private SH_BaseState _currentState;

        /// <summary> Optional reference to the physics debugger for on-screen telemetry. </summary>
        private SH_Debugger _physicsDebugger;

        /// <summary>
        /// Tracks cooldown timers for actions to prevent spamming
        /// and manage resource-based abilities.
        /// </summary>
        private readonly Dictionary<SH_ActionData, float> _actionCooldowns =
            new Dictionary<SH_ActionData, float>();

        #endregion

        #region Initialization

        /// <summary>
        /// Initial setup of the architectural core.
        /// Validates all dependencies, instantiates the Context (SSOT)
        /// to link all sub-systems, and optionally initializes the debugger.
        /// Validation is performed before context construction so that
        /// missing references are reported before a NullReferenceException
        /// could occur inside SH_PlayerContext.
        /// </summary>
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
                _economicEventSettings
            );

            if (showOnScreenDebugging)
            {
                _physicsDebugger = GetComponent<SH_Debugger>();
                if (_physicsDebugger != null)
                {
                    _physicsDebugger.Initialize(_context);
                }
            }
        }

        /// <summary>
        /// Sets the initial operational state of the Mecha.
        /// </summary>
        private void Start()
        {
            ChangeState(new SH_IdleState(_context, this));
        }

        /// <summary>
        /// Frame-by-frame update loop for input handling and logic transitions.
        /// </summary>
        private void Update()
        {
            if (_currentState == null) return;

            _currentState.HandleInput();
            _currentState.Update();
        }

        /// <summary>
        /// Physics-aligned update loop for locomotive and kinematic calculations.
        /// </summary>
        private void FixedUpdate()
        {
            if (_currentState == null) return;

            _currentState.PhysicsUpdate(Time.fixedDeltaTime);
        }

        #endregion

        #region State Management API

        /// <summary>
        /// Executes a transition to a new state, managing the lifecycle
        /// of both the previous and the incoming state.
        /// </summary>
        /// <param name="newState"> The instance of the target state. </param>
        public void ChangeState(SH_BaseState newState)
        {
            if (newState == null) { Debug.LogError($"[SH_PlayerStateMachine] Attempted to change to a null state. Transition aborted. Current state: {_currentState?.GetType().Name ?? "None"}"); return;}

            _currentState?.Exit();
            _currentState = newState;
            _currentState.Enter();
        }

        /// <summary>
        /// Evaluates and initiates high-priority actions (combat, dash, special moves).
        /// Enforces cooldown and priority checks before authorizing the transition.
        /// </summary>
        /// <param name="actionData"> The declarative data asset defining the action. </param>
        /// <returns> True if the action priority allowed the transition. </returns>
        public bool RequestAction(SH_ActionData actionData)
        {
            if (actionData == null) { Debug.LogError($"[SH_PlayerStateMachine] Attempted to request an action with null data. Request aborted. Current state: {_currentState?.GetType().Name ?? "None"}"); return false;}

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

        /// <summary>
        /// Registers the completion of an action to enforce its cooldown timer,
        /// preventing spamming and encouraging strategic decision-making.
        /// </summary>
        /// <param name="actionData"> The declarative data asset of the completed action. </param>
        public void RegisterActionCooldown(SH_ActionData actionData)
        {
            if (actionData == null) { Debug.LogError($"[SH_PlayerStateMachine] Attempted to register cooldown for an action with null data. Registration aborted. Current state: {_currentState?.GetType().Name ?? "None"}"); return; }
            _actionCooldowns[actionData] = Time.time;
        }

        #endregion

        #region Debug API

        /// <summary>
        /// Returns the readable name of the current state for telemetry and UI display.
        /// </summary>
        public string GetCurrentStateName() =>
            _currentState != null ? _currentState.GetType().Name : "None";

        #endregion

        #region Internal Validation

        /// <summary>
        /// Validates all serialized dependencies before context construction.
        /// Reports each missing reference individually to accelerate debugging
        /// in the Unity Inspector.
        /// </summary>
        private void ValidateDependencies()
        {
            // Movement & Control
            if (_input == null) Debug.LogError($"[SH_PlayerStateMachine] SH_InputHandler is not assigned in {gameObject.name}. Add SH_InputHandler component.");
            if (_perspective == null) Debug.LogError($"[SH_PlayerStateMachine] SH_PerspectiveController is not assigned in {gameObject.name}. Add SH_PerspectiveController component.");
            if (_locomotion == null) Debug.LogError($"[SH_PlayerStateMachine] SH_LocomotionController is not assigned in {gameObject.name}. Add SH_LocomotionController component.");
            if (_physics == null) Debug.LogError($"[SH_PlayerStateMachine] SH_PhysicsMotor is not assigned in {gameObject.name}. Add SH_PhysicsMotor component.");
            if (_settings == null) Debug.LogError($"[SH_PlayerStateMachine] SH_MovementSettings is not assigned in {gameObject.name}. Assign MovementSettings asset.");
            if (_animator == null) Debug.LogError($"[SH_PlayerStateMachine] Animator is not assigned in {gameObject.name}. Add Animator component to Mecha model.");
            if (_animatorBridge == null) Debug.LogError($"[SH_PlayerStateMachine] SH_AnimatorBridge is not assigned in {gameObject.name}. Add SH_AnimatorBridge component.");

            // Economic Systems
            if (_health == null) Debug.LogError($"[SH_PlayerStateMachine] SH_HealthComponent is not assigned in {gameObject.name}. Add SH_HealthComponent component.");
            if (_resources == null) Debug.LogError($"[SH_PlayerStateMachine] SH_ResourceSystem is not assigned in {gameObject.name}. Add SH_ResourceSystem component.");
            if (_economicEvents == null) Debug.LogError($"[SH_PlayerStateMachine] SH_EconomicEventManager is not assigned in {gameObject.name}. Add SH_EconomicEventManager component.");
            if (_economySettings == null) Debug.LogError($"[SH_PlayerStateMachine] SH_EconomySettings is not assigned in {gameObject.name}. Assign EconomySettings asset.");
            if (_economicEventSettings == null) Debug.LogError($"[SH_PlayerStateMachine] SH_EconomicEventSettings is not assigned in {gameObject.name}. Assign EconomicEventSettings asset.");
        }

        #endregion
    }
}