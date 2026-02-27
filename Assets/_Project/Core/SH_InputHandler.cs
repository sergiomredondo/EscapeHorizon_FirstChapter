using System;
using UnityEngine;

namespace Core
{
    // Centralized input handler that exposes high-level input events.
    // This component should be a singleton placed in the scene (e.g. on the Player or InputManager object).
    [DisallowMultipleComponent]
    public class SH_InputHandler : MonoBehaviour
    {
        public event Action<Vector2> OnMove;
        public event Action OnDash;
        public event Action OnBoost;

        private IA_PlayerControls _inputActions;
        private Action<UnityEngine.InputSystem.InputAction.CallbackContext> _onMovePerformedHandler;
        private Action<UnityEngine.InputSystem.InputAction.CallbackContext> _onMoveCanceledHandler;
        private Action<UnityEngine.InputSystem.InputAction.CallbackContext> _onDashPerformedHandler;
        private Action<UnityEngine.InputSystem.InputAction.CallbackContext> _onBoostPerformedHandler;

        void Awake()
        {
            _inputActions = new IA_PlayerControls();
        }

        void OnEnable()
        {
            if (_inputActions == null)
                _inputActions = new IA_PlayerControls();

            // Bind move action if present
            try
            {
                _onMovePerformedHandler = ctx => OnMove?.Invoke(ctx.ReadValue<Vector2>());
                _onMoveCanceledHandler = ctx => OnMove?.Invoke(Vector2.zero);
                _inputActions.Player.Move.performed += new System.Action<UnityEngine.InputSystem.InputAction.CallbackContext>(_onMovePerformedHandler);
                _inputActions.Player.Move.canceled += new System.Action<UnityEngine.InputSystem.InputAction.CallbackContext>(_onMoveCanceledHandler);
            }
            catch { }

            // Optional: try to bind Dash and Boost actions if they exist in the asset.
            try
            {
                var a = _inputActions.Player.FindAction("Dash");
                if (a != null)
                {
                    _onDashPerformedHandler = ctx => OnDash?.Invoke();
                    a.performed += new System.Action<UnityEngine.InputSystem.InputAction.CallbackContext>(_onDashPerformedHandler);
                }
            }
            catch { }
            try
            {
                var b = _inputActions.Player.FindAction("Boost");
                if (b != null)
                {
                    _onBoostPerformedHandler = ctx => OnBoost?.Invoke();
                    b.performed += new System.Action<UnityEngine.InputSystem.InputAction.CallbackContext>(_onBoostPerformedHandler);
                }
            }
            catch { }

            try { _inputActions.Player.Enable(); } catch { }
        }

        void OnDisable()
        {
            try
            {
                if (_onMovePerformedHandler != null)
                    _inputActions.Player.Move.performed -= new System.Action<UnityEngine.InputSystem.InputAction.CallbackContext>(_onMovePerformedHandler);
                if (_onMoveCanceledHandler != null)
                    _inputActions.Player.Move.canceled -= new System.Action<UnityEngine.InputSystem.InputAction.CallbackContext>(_onMoveCanceledHandler);
            }
            catch { }

            try
            {
                var a = _inputActions.Player.FindAction("Dash");
                if (a != null && _onDashPerformedHandler != null)
                    a.performed -= new System.Action<UnityEngine.InputSystem.InputAction.CallbackContext>(_onDashPerformedHandler);
            }
            catch { }
            try
            {
                var b = _inputActions.Player.FindAction("Boost");
                if (b != null && _onBoostPerformedHandler != null)
                    b.performed -= new System.Action<UnityEngine.InputSystem.InputAction.CallbackContext>(_onBoostPerformedHandler);
            }
            catch { }

            try { _inputActions.Player.Disable(); } catch { }
        }

        void OnDestroy()
        {
            try { _inputActions?.Dispose(); } catch { }
            _inputActions = null;
        }

        // These public methods can be wired to UI buttons or other input systems.
        public void TriggerDash() => OnDash?.Invoke();
        public void TriggerBoost() => OnBoost?.Invoke();
    }
}
