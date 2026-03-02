using UnityEngine;
using UnityEngine.InputSystem;

namespace Core
{
    /// <summary>
    /// Deterministic input provider for the Player State Machine (FSM).
    /// - Manages continuous data streams (MoveVector, BoostActive).
    /// - Implements an automatic one-frame buffer for discrete triggers.
    /// - Maintains total side-effect neutrality (State logic handles all outcomes).
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_InputHandler : MonoBehaviour
    {
        // -------------------------------------------------------
        // Continuous Locomotion Inputs
        // -------------------------------------------------------

        public Vector2 MoveVector { get; private set; }
        public bool BoostActive { get; private set; }

        // -------------------------------------------------------
        // Buffered One-Frame Triggers (Exposed for FSM)
        // -------------------------------------------------------

        public bool DashTriggered { get; private set; }
        public bool AttackTriggered { get; private set; }

        // Internal buffers to capture asynchronous Input System events
        private bool _dashBuffered;
        private bool _attackBuffered;

        private IA_PlayerControls _inputActions;

        // -------------------------------------------------------
        // Unity Lifecycle & Binding
        // -------------------------------------------------------

        private void Awake()
        {
            _inputActions = new IA_PlayerControls();
        }

        private void OnEnable()
        {
            if (_inputActions == null) return;

            _inputActions.Player.Enable();

            // Continuous input binding
            _inputActions.Player.Move.performed += HandleMove;
            _inputActions.Player.Move.canceled += HandleMove;

            // Discrete trigger binding
            _inputActions.Player.Dash.performed += HandleDash;
            _inputActions.Player.Attack.performed += HandleAttack;

            // State-based button binding
            _inputActions.Player.Boost.performed += HandleBoost;
            _inputActions.Player.Boost.canceled += HandleBoost;
        }

        private void OnDisable()
        {
            if (_inputActions == null) return;

            _inputActions.Player.Move.performed -= HandleMove;
            _inputActions.Player.Move.canceled -= HandleMove;

            _inputActions.Player.Dash.performed -= HandleDash;
            _inputActions.Player.Attack.performed -= HandleAttack;

            _inputActions.Player.Boost.performed -= HandleBoost;
            _inputActions.Player.Boost.canceled -= HandleBoost;

            _inputActions.Player.Disable();
        }

        /// <summary>
        /// Synchronizes internal buffers with exposed triggers and performs
        /// automatic cleanup at the end of the frame logic.
        /// </summary>
        private void LateUpdate()
        {
            // Expose buffered values for the duration of exactly one frame
            DashTriggered = _dashBuffered;
            AttackTriggered = _attackBuffered;

            // Reset internal buffers to ensure input expiration
            _dashBuffered = false;
            _attackBuffered = false;
        }

        private void OnDestroy()
        {
            _inputActions?.Dispose();
        }

        // -------------------------------------------------------
        // Input Action Callbacks
        // -------------------------------------------------------

        private void HandleMove(InputAction.CallbackContext context)
        {
            MoveVector = context.ReadValue<Vector2>();
        }

        private void HandleDash(InputAction.CallbackContext context)
        {
            if (context.performed)
                _dashBuffered = true;
        }

        private void HandleAttack(InputAction.CallbackContext context)
        {
            if (context.performed)
                _attackBuffered = true;
        }

        private void HandleBoost(InputAction.CallbackContext context)
        {
            // Captures the current button state (True while held)
            BoostActive = context.ReadValueAsButton();
        }
    }
}