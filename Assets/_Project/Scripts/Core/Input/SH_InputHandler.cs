using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Core.Input
{
    /// <summary>
    /// Centralized input handler. Translates hardware signals from the
    /// IA_PlayerControls action map into per-frame data properties consumed
    /// by the FSM states and the interaction / combat controllers.
    ///
    /// Binding mechanism:
    ///   This class owns and manages an IA_PlayerControls instance.
    ///   It calls SetCallbacks(this) so the generated code routes every
    ///   action callback to the correct OnXxx method here, regardless of
    ///   the PlayerInput component's Behavior setting (SendMessages,
    ///   InvokeUnityEvents, or none).
    ///
    ///   This is the robust, architecture-safe path. It does not depend on
    ///   method-name matching or Unity message broadcasting.
    ///
    /// Action map coverage:
    ///   Move, Look, Dash, Boost       — locomotion
    ///   Interact (press + hold)        — GDD §5.2.1
    ///   Attack (press + hold)          — GDD §5.3 (consumed by SH_PlayerCombatController)
    ///   Scan, Menu                     — future systems
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public class SH_InputHandler : MonoBehaviour, IA_PlayerControls.IPlayerActions
    {
        #region Input Action Asset

        /// <summary>
        /// The generated wrapper around the IA_PlayerControls.inputactions asset.
        /// Instantiated in Awake, enabled in OnEnable, disabled in OnDisable.
        /// </summary>
        private IA_PlayerControls _controls;

        #endregion

        #region Movement Properties

        /// <summary> Current movement vector from the Move action. </summary>
        public Vector2 MoveInput { get; private set; }

        /// <summary> Current camera look vector from the Look action. </summary>
        public Vector2 LookInput { get; private set; }

        /// <summary> True while the Dash button is held. </summary>
        public bool DashInput { get; private set; }

        /// <summary> True while the EnergySurge button is held. </summary>
        public bool EnergySurgetInput { get; private set; }

        #endregion

        #region Interaction Properties

        /// <summary>
        /// True for the single frame the Interact button transitions to pressed.
        /// Consumed and cleared by SH_IdleState / SH_MoveState via
        /// ConsumeInteractPressed() to prevent double-firing across frames.
        /// </summary>
        public bool InteractPressed { get; private set; }

        /// <summary>
        /// True for the single frame the Interact button transitions to released.
        /// Consumed by SH_IdleState / SH_MoveState via ConsumeInteractReleased().
        /// </summary>
        public bool InteractReleased { get; private set; }

        /// <summary>
        /// True while the Interact button is continuously held.
        /// Read by SH_InteractionController to sustain the hold timer.
        /// </summary>
        public bool InteractHeld { get; private set; }

        #endregion

        #region Combat Properties

        /// <summary>
        /// True for the single frame the Attack button transitions to pressed.
        /// Consumed by SH_PlayerCombatController via ConsumeAttackPressed().
        /// </summary>
        public bool AttackPressed { get; private set; }

        /// <summary>
        /// True while the Attack button is continuously held.
        /// Used by SH_PlayerCombatController to distinguish light (tap)
        /// from heavy (hold) attacks.
        /// </summary>
        public bool AttackHeld { get; private set; }

        /// <summary>
        /// True while the EnergySurge button is held. Used by SH_PlayerCombatController
        /// to trigger the surge attack when the button is released after holding for the required time.
        /// </summary>
        public bool SurgePressed { get; private set; }

        /// <summary>
        /// True for the single frame the Scan button transitions to pressed. Used by SH_ScanController
        /// to initiate a scan action when the player presses the scan button. This flag should be consumed
        /// by calling ConsumeScanPressed() after processing the scan action to reset the state for future scan inputs.
        /// </summary>
        public bool ScanPressed { get; private set; }

        #endregion

        #region Consume API

        /// <summary>
        /// Clears InteractPressed. Call once per frame after reading the flag
        /// to prevent it from being processed again in the same or next frame.
        /// </summary>
        public void ConsumeInteractPressed() => InteractPressed = false;

        /// <summary>
        /// Clears InteractReleased. Call once per frame after reading.
        /// </summary>
        public void ConsumeInteractReleased() => InteractReleased = false;

        /// <summary>
        /// Clears AttackPressed. Call once per frame after reading.
        /// </summary>
        public void ConsumeAttackPressed() => AttackPressed = false;

        /// <summary>
        /// Resets the surge pressed state to indicate that the surge action is no longer active.
        /// </summary>
        public void ConsumeSurgePressed() => SurgePressed = false;

        /// <summary>
        /// Resets the scan pressed state to indicate that a scan action has been handled.
        /// </summary>
        /// <remarks>Call this method after processing a scan event to clear the scan pressed flag. This
        /// allows subsequent scan actions to be detected correctly.</remarks>
        public void ConsumeScanPressed() => ScanPressed = false;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // Create the action map wrapper and register this class as the
            // callback target. This routes all action events to OnXxx methods
            // here regardless of the PlayerInput component's Behavior setting.
            _controls = new IA_PlayerControls();
            _controls.Player.SetCallbacks(this);
        }

        private void OnEnable()
        {
            _controls.Player.Enable();
        }

        private void OnDisable()
        {
            _controls.Player.Disable();
        }

        #endregion

        #region IA_PlayerControls.IPlayerActions Implementation

        /// <summary> Receives Move action events and updates MoveInput. </summary>
        public void OnMove(InputAction.CallbackContext context)
        {
            MoveInput = context.ReadValue<Vector2>();
        }

        /// <summary> Receives Look action events and updates LookInput. </summary>
        public void OnLook(InputAction.CallbackContext context)
        {
            LookInput = context.ReadValue<Vector2>();
        }

        /// <summary>
        /// Receives Dash action events.
        /// Started phase sets DashInput true; Canceled clears it.
        /// </summary>
        public void OnDash(InputAction.CallbackContext context)
        {
            DashInput = context.phase == InputActionPhase.Started
                     || context.phase == InputActionPhase.Performed;
        }

        /// <summary>
        /// Handles the input event for the energy surge action when triggered by the user.
        /// </summary>
        /// <remarks>Call this method in response to the energy surge input action to update the state
        /// accordingly. Typically used within an input system event handler.</remarks>
        /// <param name="context">The callback context containing information about the input action event.</param>
        public void OnEnergySurge(InputAction.CallbackContext context)
        {
            if (context.phase == InputActionPhase.Started)
                SurgePressed = true;
        }

        /// <summary>
        /// Receives Attack action events.
        /// Started → AttackPressed (single frame) + AttackHeld (sustained).
        /// Canceled → clears AttackHeld.
        /// AttackPressed is single-frame: it must be consumed via ConsumeAttackPressed().
        /// </summary>
        public void OnAttack(InputAction.CallbackContext context)
        {
            if (context.phase == InputActionPhase.Started)
            {
                AttackPressed = true;
                AttackHeld = true;
            }
            else if (context.phase == InputActionPhase.Canceled)
            {
                AttackHeld = false;
            }
        }

        /// <summary>
        /// Receives Interact action events.
        ///
        /// Started  → InteractPressed (single-frame pulse) + InteractHeld (sustained).
        /// Canceled → InteractReleased (single-frame pulse) + clears InteractHeld.
        ///
        /// Both single-frame flags must be consumed (ConsumeInteractPressed /
        /// ConsumeInteractReleased) by the reading state in the same frame to
        /// prevent them from being forwarded more than once.
        /// </summary>
        public void OnInteract(InputAction.CallbackContext context)
        {
            if (context.phase == InputActionPhase.Started)
            {
                InteractPressed = true;
                InteractReleased = false;
                InteractHeld = true;
            }
            else if (context.phase == InputActionPhase.Canceled)
            {
                InteractReleased = true;
                InteractHeld = false;
            }
        }

        /// <summary>
        /// Handles the scan input action event and updates the scan state when the action is started.
        /// </summary>
        /// <remarks>Call this method from an input system event handler to process scan input. Typically
        /// used in response to user input in gameplay or UI scenarios.</remarks>
        /// <param name="context">The callback context containing information about the input action event.</param>
        public void OnScan(InputAction.CallbackContext context)
        {
            if (context.phase == InputActionPhase.Started)
                ScanPressed = true;
        }

        /// <summary> Menu action — reserved for UI system. </summary>
        public void OnMenu(InputAction.CallbackContext context) { }

        #endregion
    }
}