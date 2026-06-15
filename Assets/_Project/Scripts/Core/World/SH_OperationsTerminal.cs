using Game.Interaction;
using UI;
using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// The unique interactable terminal in the safe zone.
    /// Opens the build menu in full interaction mode (GDD §C1).
    /// Inherits from SH_InteractableObject so SH_InteractionController
    /// detects it by overlap using the Interactable layer, exactly like
    /// SH_CaptiveCore and SH_ScrapPile.
    /// </summary>
    public class SH_OperationsTerminal : SH_InteractableObject
    {
        [Header("Terminal")]
        [Tooltip("Renderer for focus color feedback. " +
                 "Leave empty to skip the color swap.")]
        [SerializeField] private Renderer _terminalRenderer;

        [Tooltip("Color applied when the player is in range and the terminal is focused.")]
        [SerializeField] private Color _focusColor = new Color(0f, 1f, 0.7f, 1f);

        [Tooltip("Display name for the terminal. Used in UI prompts. " +
                 "If empty, the GameObject's name will be used.")]
        [SerializeField] private string _displayName = "Faro";

        private Color _baseColor;
        private SH_UIBridge _bridge;

        protected override void Awake()
        {
            base.Awake();

            // Terminal is always available — it never gets consumed.
            interactionType = InteractionType.Press;
            persistentID = "TERMINAL_SAFE_ZONE_01";
            _isAvailable = true;

            if (_terminalRenderer != null)
                _baseColor = _terminalRenderer.material.color;

            _bridge = UnityEngine.Object.FindFirstObjectByType<SH_UIBridge>();

            if (_bridge == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[SH_OperationsTerminal] SH_UIBridge not found in scene.");
#endif
            }
        }

        

        public override string ToString()
        {
            return string.IsNullOrEmpty(_displayName) ? gameObject.name : _displayName;
        }
        public override void Interact(Core.SH_PlayerContext context)
        {
            _bridge?.OpenBuildMenu(interactionEnabled: true);
        }

        protected override void OnFocusEnterInternal()
        {
            if (_terminalRenderer != null)
                _terminalRenderer.material.color = _focusColor;
        }

        protected override void OnFocusExitInternal()
        {
            if (_terminalRenderer != null)
                _terminalRenderer.material.color = _baseColor;
        }

        // Terminal never gets consumed — override to prevent MarkConsumed().
        protected override void OnDestroyVisualOnLoad() { }
    }
}