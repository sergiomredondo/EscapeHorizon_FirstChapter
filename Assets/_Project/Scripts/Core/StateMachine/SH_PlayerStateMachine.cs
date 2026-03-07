using Actions.Data;
using Core.Camera;
using Core.Locomotion;
using Core.Physics;
using Core.Input;
using Core.StateMachine.States;
using Data;
using UnityEngine;

namespace Core.StateMachine
{
    /// <summary>
    /// Central orchestrator of the Mecha's behavior. 
    /// Manages state transitions, ensures dependency injection into states, 
    /// and arbitrates action requests based on a priority-driven system.
    /// Acts as the owner of the SH_PlayerContext (SSOT).
    /// </summary>
    [SelectionBase]
    [DisallowMultipleComponent]
    public class SH_PlayerStateMachine : MonoBehaviour
    {
        #region Dependencies

        [Header("System References")]
        [Tooltip("Input handler is responsible for processing raw input and providing normalized movement vectors and action states. Use SH_InputHandler component.")]
        [SerializeField] private SH_InputHandler _input;

        [Tooltip("Perspective controller is responsible for converting input vectors into world space directions based on camera orientation. Use SH_PerspectiveController component.")]
        [SerializeField] private SH_PerspectiveController _perspective;

        [Tooltip("Locomotion controller is responsible for translating input intentions into locomotive forces. Use SH_LocomotionController component.")]
        [SerializeField] private SH_LocomotionController _locomotion;

        [Tooltip("Physics motor is responsible for velocity integration, gravity, and friction. Use SH_PhysicsMotor component.")]
        [SerializeField] private SH_PhysicsMotor _physics;

        [Tooltip("Movement settings is responsible for defining mass, speed limits, acceleration times, and friction coefficients. Use SH_MovementSettings asset.")]
        [SerializeField] private SH_MovementSettings _settings;

        [Tooltip("Animator is responsible for visual feedback and skeletal state management. Use the Animator component on the Mecha model.")]
        [SerializeField] private Animator _animator;

        /// <summary> The Single Source of Truth for all state-shared data. </summary>
        private SH_PlayerContext _context;

        /// <summary> The currently active behavior state. </summary>
        private SH_BaseState _currentState;

        #endregion

        #region Initialization

        /// <summary>
        /// Initial setup of the architectural core. 
        /// Instantiates the Context (SSOT) to link all sub-systems.
        /// </summary>
        private void Awake()
        {
            if (_input == null) Debug.LogError($"[SH_PlayerStateMachine] SH_InputHandler is not assigned in {gameObject.name}. Please add a SH_InputHandler component.");
            if (_perspective == null) Debug.LogError($"[SH_PlayerStateMachine] SH_PerspectiveController is not assigned in {gameObject.name}. Please add a SH_PerspectiveController component.");
            if (_locomotion == null) Debug.LogError($"[SH_PlayerStateMachine] SH_LocomotionController is not assigned in {gameObject.name}. Please add a SH_LocomotionController component.");
            if (_physics == null) Debug.LogError($"[SH_PlayerStateMachine] SH_PhysicsMotor is not assigned in {gameObject.name}. Please add a SH_PhysicsMotor component.");
            if (_settings == null) Debug.LogError($"[SH_PlayerStateMachine] SH_MovementSettings is not assigned in {gameObject.name}. Please assign a SH_MovementSettings asset.");
            if (_animator == null) Debug.LogError($"[SH_PlayerStateMachine] Animator is not assigned in {gameObject.name}. Please add an Animator component to the Mecha model.");

            // Initialization of the Player Context with all necessary dependencies.
            _context = new SH_PlayerContext(
                transform,
                _input,
                _perspective,
                _locomotion,
                _physics,
                _settings,
                _animator
            );
        }

        /// <summary>
        /// Sets the initial operational state of the Mecha.
        /// </summary>
        private void Start()
        {
            // Set the initial state to Idle, which is the default resting state of the Mecha.
            ChangeState(new SH_IdleState(_context, this));
        }

        /// <summary>
        /// Frame-by-frame update loop for input handling and logic transitions.
        /// </summary>
        private void Update()
        {
            if (_currentState == null) return;

            // 1. Handling of player input is delegated to the current state, allowing for context-sensitive processing.
            _currentState.HandleInput();

            // 2. State logic updates are processed after input handling to allow for immediate reactions to player intentions and state changes.
            _currentState.Update();
        }

        /// <summary>
        /// Physics-aligned update loop for locomotive and kinematic calculations.
        /// </summary>
        private void FixedUpdate()
        {
            if (_currentState == null) return;

            // 3. Physics updates are processed in FixedUpdate to ensure consistent timing for velocity integration and collision handling, regardless of frame rate fluctuations.
            _currentState.PhysicsUpdate(Time.fixedDeltaTime);
        }

        #endregion

        #region State Management API

        /// <summary>
        /// Executes a transition to a new state, managing the lifecycle of both previous and next states.
        /// </summary>
        /// <param name="newState">The instance of the target state.</param>
        public void ChangeState(SH_BaseState newState)
        {
            if (newState == null) { Debug.LogError($"[SH_PlayerStateMachine] Attempted to change to a null state. Transition aborted. Current state: {_currentState?.GetType().Name ?? "None"}"); return; }
            
            // Checks if the new state can be entered based on its internal conditions (e.g., cooldowns, resource availability).
            _currentState?.Exit();
            _currentState = newState;
            _currentState.Enter();
        }

        /// <summary>
        /// Evaluates and initiates high-priority actions (Skills, Combat, Special Moves).
        /// </summary>
        /// <param name="actionData">The declarative data asset defining the action's properties.</param>
        /// <returns>True if the action priority allowed the transition.</returns>
        public bool RequestAction(SH_ActionData actionData)
        {
            if (actionData == null) { Debug.LogError($"[SH_PlayerStateMachine] Attempted to request an action with null data. Request aborted. Current state: {_currentState?.GetType().Name ?? "None"}"); return false; }
            
            // Priority check ensures that only actions of equal or higher priority can interrupt the current state, allowing for a dynamic and responsive combat system while preventing lower-priority actions from disrupting critical maneuvers.
            if (_currentState == null || actionData.priority >= _currentState.Priority)
            {
                ChangeState(new SH_ActionState(_context, this, actionData));
                return true;
            }

            return false;
        }

        #endregion

        #region Debug API

        /// <summary> Returns the readable name of the current state for telemetry and UI. </summary>
        public string GetCurrentStateName() => _currentState != null ? _currentState.GetType().Name : "None";

        #endregion
    }
}