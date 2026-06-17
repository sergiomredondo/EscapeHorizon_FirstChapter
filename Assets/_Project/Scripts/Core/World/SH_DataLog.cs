using Game.Interaction;
using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Narrative interactable. Supports two activation modes:
    ///   Interactable — player presses the interact button when in range.
    ///   AutoTrigger  — activates automatically when the player enters the collider.
    ///
    /// Supports two display types:
    ///   TextPanel       — shows a text log panel via UI Toolkit.
    ///   ImageSequence   — plays a Canvas Animator sequence.
    /// </summary>
    public class SH_DataLog : SH_InteractableObject
    {
        #region Enums

        public enum DataLogMode { Interactable, AutoTrigger }
        public enum DataLogDisplay { TextPanel, ImageSequence }

        #endregion

        #region Inspector

        [Header("Data Log — Mode")]

        [Tooltip("Display name for the button action. Used in UI prompts. " +
         "If empty, the GameObject's name will be used.")]
        [SerializeField] private string _displayName = "Panel de datos";

        [Tooltip("Interactable: player presses E to activate.\n" +
                 "AutoTrigger: activates when player enters the collider.")]
        [SerializeField] private DataLogMode _mode = DataLogMode.Interactable;

        [Tooltip("TextPanel: shows the UI Toolkit text log overlay.\n" +
                 "ImageSequence: plays a Canvas Animator sequence.")]
        [SerializeField] private DataLogDisplay _displayType = DataLogDisplay.TextPanel;

        [Header("Text Panel Content")]
        [SerializeField] private string _logTitle = "SYSTEM LOG";
        [SerializeField] private string _logSource = "SECTOR / MODULE";

        [TextArea(4, 12)]
        [SerializeField] private string _logBody = "";

        [Header("Image Sequence Content")]
        [Tooltip("Name of the Animator state to play for this sequence.")]
        [SerializeField] private string _animStateName = "";

        [Header("Behaviour")]
        [Tooltip("If false, the log is consumed after the first activation.")]
        [SerializeField] private bool _canBeReRead = false;

        [Header("AutoTrigger — Detection")]
        [Tooltip("Tag used to identify the player for AutoTrigger mode.")]
        [SerializeField] private string _playerTag = "Player";

        #endregion

        #region SH_InteractableObject Overrides

        protected override void Awake()
        {
            base.Awake();
            interactionType = InteractionType.Press;
            _isAvailable = true;
        }

        public override void Interact(Core.SH_PlayerContext context)
        {
            Activate();
        }

        protected override void OnFocusEnterInternal() { }
        protected override void OnFocusExitInternal() { }
        protected override void OnDestroyVisualOnLoad() { }

        #endregion

        #region AutoTrigger

        private void OnTriggerEnter(UnityEngine.Collider other)
        {
            if (_mode != DataLogMode.AutoTrigger) return;
            if (!other.CompareTag(_playerTag)) return;
            if (!_isAvailable) return;

            Activate();
        }

        #endregion

        #region Activation

        private void Activate()
        {
            if (SH_NarrativeSequencer.Instance == null)
            {
                Debug.LogWarning($"[SH_DataLog] '{gameObject.name}': " +
                                 $"SH_NarrativeSequencer not found in scene.");
                return;
            }

            switch (_displayType)
            {
                case DataLogDisplay.TextPanel:
                    SH_NarrativeSequencer.Instance.ShowTextLog(
                        _logTitle, _logSource, _logBody);
                    break;

                case DataLogDisplay.ImageSequence:
                    SH_NarrativeSequencer.Instance.ShowImageSequence(_animStateName);
                    break;
            }

            if (!_canBeReRead)
                MarkConsumed();
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(_displayName) ? gameObject.name : _displayName;
        }
        #endregion
    }
}