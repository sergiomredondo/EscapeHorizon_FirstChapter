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
            DontDestroyOnLoad(this.gameObject);
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
                _inputActions.Player.Move.performed += _onMovePerformedHandler;
                _inputActions.Player.Move.canceled += _onMoveCanceledHandler;
                Debug.Log("SH_InputHandler: Bound Move action from generated Player map.");
            }
            catch { }

            // Optional: try to bind Dash and Boost actions if they exist in the asset.
            try
            {
                // Try to find actions via the asset. Generated wrappers may not expose FindAction on the map.
                var a = _inputActions.asset != null ? _inputActions.asset.FindAction("Player/Dash") ?? _inputActions.asset.FindAction("Dash") : null;
                if (a == null)
                {
                    Debug.Log("SH_InputHandler: Dash action not found in InputActionAsset.");
                }
                else
                {
                    _onDashPerformedHandler = ctx =>
                    {
                        Debug.Log("SH_InputHandler: Dash performed.");
                        OnDash?.Invoke();
                    };
                    a.performed += _onDashPerformedHandler;
                    Debug.Log("SH_InputHandler: Bound Dash action from InputActionAsset.");
                }
            }
            catch { }
            try
            {
                var b = _inputActions.asset != null ? _inputActions.asset.FindAction("Player/Boost") ?? _inputActions.asset.FindAction("Boost") : null;
                if (b == null)
                {
                    Debug.Log("SH_InputHandler: Boost action not found in InputActionAsset.");
                }
                else
                {
                    _onBoostPerformedHandler = ctx =>
                    {
                        Debug.Log("SH_InputHandler: Boost performed.");
                        OnBoost?.Invoke();
                    };
                    b.performed += _onBoostPerformedHandler;
                    Debug.Log("SH_InputHandler: Bound Boost action from InputActionAsset.");
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
                    _inputActions.Player.Move.performed -= _onMovePerformedHandler;
                if (_onMoveCanceledHandler != null)
                    _inputActions.Player.Move.canceled -= _onMoveCanceledHandler;
            }
            catch { }

            try
            {
                var a = _inputActions.asset != null ? _inputActions.asset.FindAction("Player/Dash") ?? _inputActions.asset.FindAction("Dash") : null;
                if (a != null && _onDashPerformedHandler != null)
                    a.performed -= _onDashPerformedHandler;
            }
            catch { }
            try
            {
                var b = _inputActions.asset != null ? _inputActions.asset.FindAction("Player/Boost") ?? _inputActions.asset.FindAction("Boost") : null;
                if (b != null && _onBoostPerformedHandler != null)
                    b.performed -= _onBoostPerformedHandler;
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
