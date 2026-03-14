using UnityEngine;
using Core;

namespace Game.Interaction
{
    /// <summary>
    /// Contract that any interactable world entity must fulfill.
    /// Defines the minimum surface required by SH_InteractionController
    /// to detect, prioritize, and execute interactions without coupling
    /// to specific object types.
    ///
    /// Implementing types: SH_CaptiveCore, SH_ScrapPile, and any future
    /// interactable defined in GDD §5.2.1 (Baúles, Puertas, Consolas).
    ///
    /// Responsibility boundaries:
    ///   - DEFINES: Interaction type (hold vs. press), range, and availability.
    ///   - DEFINES: The execution entry point for interaction resolution.
    ///   - DOES NOT OWN: Hold timer logic (belongs to SH_InteractionController).
    ///   - DOES NOT OWN: Input handling (belongs to SH_InputHandler).
    /// </summary>
    public interface IInteractable
    {
        /// <summary>
        /// Classifies how this object's interaction is triggered.
        /// Determines whether SH_InteractionController uses a hold timer
        /// or a single-frame press to resolve the interaction.
        /// </summary>
        InteractionType InteractionType { get; }

        /// <summary>
        /// World-space position of the interaction point.
        /// Used by SH_InteractionController to calculate distance
        /// and determine the closest valid target.
        /// </summary>
        Vector3 WorldPosition { get; }

        /// <summary>
        /// Returns true if this object can currently be interacted with.
        /// Prevents interaction with already-resolved or disabled objects
        /// (e.g., a Captive Core that has already been rescued or destroyed).
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Executes the interaction resolution for this object.
        /// Called by SH_InteractionController when the interaction
        /// condition is met (hold complete or press detected).
        /// The context provides access to the resource system and
        /// any other sub-system the interaction needs to affect.
        /// </summary>
        /// <param name="context">
        /// The player context. Provides access to SH_ResourceSystem
        /// for delivering rewards and SH_HealthComponent for any
        /// state checks required by the interaction.
        /// </param>
        void Interact(SH_PlayerContext context);

        /// <summary>
        /// Called by SH_InteractionController when the player enters
        /// interaction range. Used to activate proximity visual feedback
        /// (e.g., highlight, UI prompt).
        /// </summary>
        void OnFocusEnter();

        /// <summary>
        /// Called by SH_InteractionController when the player leaves
        /// interaction range or the object is deselected. Used to
        /// deactivate proximity visual feedback.
        /// </summary>
        void OnFocusExit();

        /// <summary>
        /// Called by SH_InteractionController if an ongoing hold
        /// interaction is interrupted before completion (e.g., player
        /// takes damage or moves out of range during the hold).
        /// Used to reset hold progress indicators on the object side.
        /// </summary>
        void OnInteractionInterrupted();
    }

    /// <summary>
    /// Defines how an interactable object's action is triggered.
    /// Determines the interaction resolution path in SH_InteractionController.
    /// </summary>
    public enum InteractionType
    {
        /// <summary>
        /// Single-frame press. Resolves immediately on button down.
        /// Used by: Baúles de Suministro, Mecanismos de Progresión,
        /// Puntos de Acoplamiento (GDD §5.2.1).
        /// </summary>
        Press,

        /// <summary>
        /// Sustained button hold. Resolves after holdDuration seconds.
        /// Interruptible by damage or range break.
        /// Used by: Núcleos Cautivos, Pilas de Chatarra (GDD §5.2.1).
        /// The hold duration is defined per-object in SH_InteractionSettings.
        /// </summary>
        Hold
    }
}
