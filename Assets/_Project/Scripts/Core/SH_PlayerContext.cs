using UnityEngine;
using Core.Locomotion;
using Core.Camera;
using Core.Physics;
using Core.Input;
using Data;

namespace Core
{
    /// <summary>
    /// Immutable dependency container acting as the Single Source of Truth (SSOT).
    /// Orchestrates the initialization and communication between the State Machine 
    /// and the Mecha's sub-systems while maintaining strict architectural decoupling.
    /// </summary>
    public sealed class SH_PlayerContext
    {
        #region Read-Only Authorities (System Access)

        /// <summary> Global positioning and orientation reference for the Mecha entity. </summary>
        public Transform Transform { get; }

        /// <summary> Interface for semantic and raw input sampling. </summary>
        public SH_InputHandler Input { get; }

        /// <summary> Provides world-space directions relative to camera or lock-on targets. </summary>
        public SH_PerspectiveController Perspective { get; }

        /// <summary> Manages acceleration deltas and high-level locomotion states. </summary>
        public SH_LocomotionController Locomotion { get; }

        /// <summary> Core motor for velocity integration, friction, and gravity. </summary>
        public SH_PhysicsMotor Physics { get; }

        /// <summary> Static data container for mass, speed limits, and acceleration times. </summary>
        public SH_MovementSettings Settings { get; }

        /// <summary> Bridge to the visual layer for feedback and skeletal state management. </summary>
        public Animator Animator { get; }

        #endregion

        #region Constructor & Orchestration

        /// <summary>
        /// Explicitly initializes the context and orchestrates the dependency injection for sub-systems.
        /// Validates architectural integrity to prevent NullReferenceExceptions during state execution.
        /// </summary>
        public SH_PlayerContext(
            Transform transform,
            SH_InputHandler input,
            SH_PerspectiveController perspective,
            SH_LocomotionController locomotion,
            SH_PhysicsMotor physics,
            SH_MovementSettings settings,
            Animator animator)
        {
            // Assinment of read-only properties with serialized references from the State Machine.
            Transform = transform;
            Input = input;
            Perspective = perspective;
            Locomotion = locomotion;
            Physics = physics;
            Settings = settings;
            Animator = animator;

            // Validation of critical dependencies to ensure the integrity of the context before any state logic is executed.
            ValidateDependencies();

            // Orchestration of sub-systems to inject necessary references and prepare them for state-driven updates.
            OrchestrateSubsystems();
        }

        /// <summary>
        /// Ensures all critical references are present before allowing state machine execution.
        /// </summary>
        private void ValidateDependencies()
        {
            if (Transform == null) Debug.LogError("[SH_PlayerContext] Transform reference is missing.");
            if (Input == null) Debug.LogError("[SH_PlayerContext] Input Handler reference is missing.");
            if (Perspective == null) Debug.LogError("[SH_PlayerContext] Perspective Controller reference is missing.");
            if (Locomotion == null) Debug.LogError("[SH_PlayerContext] Locomotion Controller reference is missing.");
            if (Physics == null) Debug.LogError("[SH_PlayerContext] Physics Motor reference is missing.");
            if (Settings == null) Debug.LogError("[SH_PlayerContext] Movement Settings asset is missing.");
        }

        /// <summary>
        /// Injects dependencies into sub-systems to ensure they have access to the SSOT data.
        /// </summary>
        private void OrchestrateSubsystems()
        {
            // Initialize the perspective controller with movement settings to establish camera authority for input projection.
            Perspective.Initialize(Settings);

            // Initialize the locomotion controller with input and movement settings to prepare it for processing movement intentions.
            Locomotion.Initialize(Input, Settings);
        }

        #endregion
    }
}