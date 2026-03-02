using Core;
using Data;
using PlayerMovement;

namespace Core.States
{
    /// <summary>
    /// Shared data container for the Player State Machine.
    /// Provides immutable references to core systems, ensuring all states
    /// operate on a single source of truth (SSOT).
    /// </summary>
    public class SH_PlayerContext
    {
        // --- Core System References ---
        public SH_CharacterController Controller { get; }
        public SH_InputHandler Input { get; }
        public SH_PerspectiveController PerspectiveController { get; }

        // --- Data & Settings ---
        public MovementSettings MovementSettings { get; }

        /// <summary>
        /// Initializes the context with all required dependencies for the Mecha's operation.
        /// </summary>
        public SH_PlayerContext(
            SH_CharacterController controller,
            SH_InputHandler input,
            SH_PerspectiveController perspectiveController,
            MovementSettings movementSettings)
        {
            Controller = controller;
            Input = input;
            PerspectiveController = perspectiveController;
            MovementSettings = movementSettings;
        }
    }
}