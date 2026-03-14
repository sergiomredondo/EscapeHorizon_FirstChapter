using UnityEngine;

namespace Game.Interaction.Data
{
    /// <summary>
    /// Central configuration asset for the interaction system.
    /// Stores all tunable parameters for hold durations, detection ranges,
    /// and accessibility options defined in GDD §5.2.1 and §5.1.4.
    ///
    /// One asset per project. Assign to SH_PlayerStateMachine in the Inspector.
    /// Modify values here to tune interaction feel without touching code.
    /// </summary>
    [CreateAssetMenu(
        fileName = "InteractionSettings",
        menuName = "ScapeHorizon/Settings/InteractionSettings",
        order = 302)]
    public class SH_InteractionSettings : ScriptableObject
    {
        #region Detection

        [Header("Detection")]

        [Tooltip("Radius (meters) within which the SH_InteractionController scans " +
                 "for IInteractable objects each frame. " +
                 "Defines how close Bear must be to receive the interaction prompt.")]
        [Min(0.1f)]
        public float detectionRadius = 2.5f;

        [Tooltip("LayerMask used by the sphere overlap scan. " +
                 "Set this to the layer(s) assigned to interactable world objects. " +
                 "Restricts the scan to avoid performance overhead from irrelevant colliders.")]
        public LayerMask interactableLayer;

        #endregion

        #region Hold Durations

        [Header("Hold Durations — per Interaction Category")]

        [Tooltip("Time (seconds) the player must hold the interact button to " +
                 "complete the extraction long hold = high commitment = ethical weight. " +
                 "Feedback: Response time < 50ms from press to radial bar start.")]
        [Min(0.1f)] 
        public float captiveCoreHoldDuration = 2.5f;

        [Tooltip("Time (seconds) the player must hold to destroy a Pila de Chatarra " +
                 "and collect its Scrap. Shorter than Núcleo hold to reflect lower risk.")]
        [Min(0.1f)]
        public float scrapPileHoldDuration = 1.2f;

        [Tooltip("Time (seconds) to hold for generic Hold interactions not covered " +
                 "by a specific category above.")]
        [Min(0.1f)]
        public float defaultHoldDuration = 1.5f;

        #endregion

        #region Press Confirmation

        [Header("Press Confirmation — Accessibility")]

        [Tooltip("Time window (seconds) within which a button press is classified " +
                 "as a 'Press' interaction rather than the start of a 'Hold'. " +
                 "Higher values help players with reduced motor control.")]
        [Range(0f, 0.5f)]
        public float pressConfirmationWindow = 0.1f;

        #endregion

        #region Toggle/Hold Mode

        [Header("Toggle Mode — Accessibility")]

        [Tooltip("If true, the Núcleo Cautivo hold action becomes a toggle: " +
                 "first press starts the hold, second press cancels it. " +
                 "GDD §5.1.4: addresses fatigue for players with limited grip endurance.")]
        public bool captiveCoreToggleMode = false;

        [Tooltip("If true, the Pila de Chatarra hold action becomes a toggle.")]
        public bool scrapPileToggleMode = false;

        #endregion

        #region Range Break

        [Header("Range Break")]

        [Tooltip("If true, a hold interaction is interrupted if the player moves " +
                 "beyond detectionRadius + rangeBreakBuffer during the hold. " +
                 "Set to false to allow slow movement during interaction.")]
        public bool breakOnRangeExit = true;

        [Tooltip("Additional buffer distance (meters) beyond detectionRadius before " +
                 "a hold is forcibly interrupted by movement. " +
                 "Provides a small grace window to avoid hair-trigger cancellations.")]
        [Min(0f)]
        public float rangeBreakBuffer = 0.3f;

        #endregion

        #region Editor Validation

        private void OnValidate()
        {
            detectionRadius     = Mathf.Max(0.1f, detectionRadius);
            captiveCoreHoldDuration = Mathf.Max(0.1f, captiveCoreHoldDuration);
            scrapPileHoldDuration   = Mathf.Max(0.1f, scrapPileHoldDuration);
            defaultHoldDuration     = Mathf.Max(0.1f, defaultHoldDuration);
            pressConfirmationWindow = Mathf.Clamp(pressConfirmationWindow, 0f, 0.5f);
            rangeBreakBuffer        = Mathf.Max(0f, rangeBreakBuffer);
        }

        #endregion
    }
}
