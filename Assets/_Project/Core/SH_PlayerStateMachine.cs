using Core.States;
using Data;
using PlayerMovement;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Deterministic Finite State Machine (FSM) for the Player.
    /// Orchestrates the lifecycle of behaviors, ensuring total synchronization between:
    /// - Newtonian CharacterController integration.
    /// - Buffered InputHandler (single-frame triggers).
    /// - Strict execution flow: Input Analysis -> Global Evaluation -> Logic -> Physics.
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_PlayerStateMachine : MonoBehaviour
    {
        [Header("FSM Diagnostics")]
        [SerializeField, ReadOnly] private string currentStateName;

        public SH_BaseState CurrentState { get; private set; }

        // --- Core States ---
        public SH_IdleState IdleState { get; private set; }
        public SH_MoveState MoveState { get; private set; }
        public SH_DashState DashState { get; private set; }

        private SH_PlayerContext _context;
        private bool _isInitialized;

        [Header("Configuration Assets")]
        [SerializeField] private MovementSettings movementSettings;

        #region Unity Lifecycle

        private void Awake()
        {
            InitializeFSM();
        }

        private void Start()
        {
            if (_isInitialized)
                Initialize(IdleState);
        }

        /// <summary>
        /// Logic update cycle. Executes the analytical decision-making process
        /// before committing to physical changes.
        /// </summary>
        private void Update()
        {
            if (!_isInitialized || CurrentState == null) return;

            // 1. Capture player intention within the current context
            CurrentState.HandleInput();

            // 2. Evaluate high-priority global transitions (Overrides)
            EvaluateGlobalTransitions();

            // 3. Process active state logic and local transitions
            CurrentState.Update();

            // Update diagnostics
            currentStateName = CurrentState.GetType().Name;
        }

        /// <summary>
        /// Physics update cycle. Synchronized with the CharacterController 
        /// for Newtonian force integration.
        /// </summary>
        private void FixedUpdate()
        {
            if (!_isInitialized || CurrentState == null) return;

            // 4. Physical execution phase
            CurrentState.PhysicsUpdate();
        }

        #endregion

        #region Initialization Logic

        /// <summary>
        /// Validates dependencies and constructs the Single Source of Truth (Context).
        /// </summary>
        private void InitializeFSM()
        {
            if (movementSettings == null)
            {
                Debug.LogError($"[FSM] MovementSettings missing on {name}. Execution halted.");
                return;
            }

            // Dependency Resolution
            var controller = GetComponent<SH_CharacterController>();
            var input = GetComponent<SH_InputHandler>();
            var perspective = GetComponent<SH_PerspectiveController>();

            if (controller == null || input == null || perspective == null)
            {
                Debug.LogError($"[FSM] Critical components (Controller/Input/Perspective) missing on {name}.");
                return;
            }

            // Context construction: Immutable SSOT for states
            _context = new SH_PlayerContext(
                controller,
                input,
                perspective,
                movementSettings
            );

            // State instantiation
            IdleState = new SH_IdleState(this, _context);
            MoveState = new SH_MoveState(this, _context);
            DashState = new SH_DashState(this, _context);

            _isInitialized = true;
        }

        /// <summary>
        /// Defines the initial entry point of the machine.
        /// </summary>
        public void Initialize(SH_BaseState startingState)
        {
            CurrentState = startingState;
            CurrentState.Enter();
        }

        #endregion

        #region FSM Core Logic

        /// <summary>
        /// Executes a formal transition between states, respecting validation rules.
        /// </summary>
        public void ChangeState(SH_BaseState newState)
        {
            if (!_isInitialized || newState == null || CurrentState == newState)
                return;

            // Integrity Check
            if (!CurrentState.CanExit() || !newState.CanEnter())
                return;

            CurrentState.Exit();
            CurrentState = newState;
            CurrentState.Enter();
        }

        /// <summary>
        /// Analyzes global input triggers that take precedence over the current state.
        /// Integrated between HandleInput and Update to ensure deterministic evaluation.
        /// </summary>
        private void EvaluateGlobalTransitions()
        {
            var input = _context.Input;

            // Dash Priority: Overrides standard locomotion if triggers are active
            if (input.DashTriggered && CurrentState != DashState)
            {
                ChangeState(DashState);
            }
        }

        #endregion
    }

    /// <summary>
    /// Attribute used for Inspector-level diagnostic readouts only.
    /// </summary>
    public class ReadOnlyAttribute : PropertyAttribute { }
}