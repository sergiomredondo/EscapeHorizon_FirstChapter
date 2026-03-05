using UnityEngine;
using UnityEngine.InputSystem;
using StarterAssets;

namespace Core.Input
{
    /// <summary>
    /// Centralized input handler that translates hardware signals into gameplay data.
    /// It implements the IPlayerActions interface nested within the generated IA_PlayerControls class.
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public class SH_InputHandler : MonoBehaviour, IA_PlayerControls.IPlayerActions
    {
        #region Data Properties

        /// <summary> Current movement vector. </summary>
        public Vector2 MoveInput { get; private set; }

        /// <summary> Current camera rotation vector. </summary>
        public Vector2 LookInput { get; private set; }

        /// <summary> Current state of the Dash action. </summary>
        public bool DashInput { get; private set; }

        /// <summary> Current state of the Boost action. </summary>
        public bool BoostInput { get; private set; }

        #endregion

        #region Interface Implementation: IA_PlayerControls.IPlayerActions

        /// <summary> Updates movement data from input context. </summary>
        public void OnMove(InputAction.CallbackContext context)
        {
            MoveInput = context.ReadValue<Vector2>();
        }

        /// <summary> Updates camera rotation data from input context. </summary>
        public void OnLook(InputAction.CallbackContext context)
        {
            LookInput = context.ReadValue<Vector2>();
        }

        /// <summary> Updates Dash state from input context. </summary>
        public void OnDash(InputAction.CallbackContext context)
        {
            DashInput = context.ReadValue<float>() > 0.5f;
        }

        /// <summary> Updates Boost state from input context. </summary>
        public void OnBoost(InputAction.CallbackContext context)
        {
            BoostInput = context.ReadValue<float>() > 0.5f;
        }

        /// <summary> Placeholder for attack logic implementation. </summary>
        public void OnAttack(InputAction.CallbackContext context) { }

        /// <summary> Placeholder for interaction logic implementation. </summary>
        public void OnInteract(InputAction.CallbackContext context) { }

        /// <summary> Placeholder for scanning logic implementation. </summary>
        public void OnScan(InputAction.CallbackContext context) { }

        /// <summary> Placeholder for menu toggle implementation. </summary>
        public void OnMenu(InputAction.CallbackContext context) { }

        #endregion
    }
}