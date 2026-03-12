using Animation;
using Core;
using Core.Camera;
using Core.Input;
using Core.Locomotion;
using Core.Physics;
using Data;
using Game.Economy;
using Game.Economy.Data;
using UnityEditor.PackageManager;
using UnityEngine;
using static UnityEditor.ShaderData;

namespace Core
{
    /// <summary>
    /// Immutable dependency container acting as the Single Source of Truth (SSOT).
    /// Orchestrates the initialization and communication between the State Machine
    /// and the Mecha's sub-systems while maintaining strict architectural decoupling.
    /// 
    /// Extended to include the economic sub-systems:
    ///   - SH_HealthComponent:       Mecha structural integrity (Durability/HP).
    ///   - SH_ResourceSystem:        IC, Scrap, and Energy resource management.
    ///   - SH_EconomicEventManager:  Dynamic economic event lifecycle management.
    ///   - SH_EconomySettings:       Central economic configuration asset.
    ///   - SH_EconomicEventSettings: Economic event configuration asset.
    /// </summary>
    public sealed class SH_PlayerContext
    {
        #region Read-Only Authorities — Movement & Control

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

        /// <summary>
        /// Abstraction layer for animator interactions, decoupling state logic
        /// from specific animation implementations.
        /// </summary>
        public SH_AnimatorBridge AnimatorBridge { get; }

        #endregion

        #region Read-Only Authorities — Economic Systems

        /// <summary>
        /// Manages the Mecha's structural integrity (Durability/HP).
        /// Fires events on damage, repair, critical state, and defeat.
        /// Does not access the resource system directly.
        /// </summary>
        public SH_HealthComponent Health { get; }

        /// <summary>
        /// Central authority for IC, Scrap, and Energy resource state.
        /// Exposes the public API consumed by combat, interaction, and UI systems.
        /// </summary>
        public SH_ResourceSystem Resources { get; }

        /// <summary>
        /// Manages the lifecycle of dynamic economic events (Scarcity, Overload, Flux).
        /// Applies and removes modifiers on SH_ResourceSystem via its setter API.
        /// </summary>
        public SH_EconomicEventManager EconomicEvents { get; }

        /// <summary>
        /// Central economic configuration asset.
        /// Exposed on the context so any state or system can read constants
        /// (e.g., maxEnergy for UI normalization) without holding a separate reference.
        /// </summary>
        public SH_EconomySettings EconomySettings { get; }

        /// <summary>
        /// Economic event configuration asset.
        /// Exposed for diagnostic and UI systems that need to read event thresholds
        /// without coupling to SH_EconomicEventManager internals.
        /// </summary>
        public SH_EconomicEventSettings EconomicEventSettings { get; }

        #endregion

        #region Constructor & Orchestration

        /// <summary>
        /// Explicitly initializes the context and orchestrates dependency injection
        /// for all sub-systems, including the new economic layer.
        /// Validates architectural integrity to prevent NullReferenceExceptions
        /// during state execution.
        /// </summary>
        public SH_PlayerContext(
            Transform transform,
            SH_InputHandler input,
            SH_PerspectiveController perspective,
            SH_LocomotionController locomotion,
            SH_PhysicsMotor physics,
            SH_MovementSettings settings,
            Animator animator,
            SH_AnimatorBridge animatorBridge,
            SH_HealthComponent health,
            SH_ResourceSystem resources,
            SH_EconomicEventManager economicEvents,
            SH_EconomySettings economySettings,
            SH_EconomicEventSettings economicEventSettings)
        {
            // Assignment of read-only properties with serialized references
            // from the State Machine.
            Transform = transform;
            Input = input;
            Perspective = perspective;
            Locomotion = locomotion;
            Physics = physics;
            Settings = settings;
            Animator = animator;
            AnimatorBridge = animatorBridge;
            Health = health;
            Resources = resources;
            EconomicEvents = economicEvents;
            EconomySettings = economySettings;
            EconomicEventSettings = economicEventSettings;

            // Validation of all critical dependencies to ensure the integrity
            // of the context before any state logic is executed.
            ValidateDependencies();

            // Orchestration of sub-systems to inject necessary references
            // and prepare them for state-driven updates.
            OrchestrateSubsystems();
        }

        /// <summary>
        /// Ensures all critical references are present before allowing
        /// state machine execution. Logs descriptive errors for each
        /// missing dependency to accelerate debugging.
        /// </summary>
        private void ValidateDependencies()
        {
            // Movement & Control
            if (Transform == null)
                Debug.LogError("[SH_PlayerContext] Transform reference is missing.");
            if (Input == null)
                Debug.LogError("[SH_PlayerContext] Input Handler reference is missing.");
            if (Perspective == null)
                Debug.LogError("[SH_PlayerContext] Perspective Controller reference is missing.");
            if (Locomotion == null)
                Debug.LogError("[SH_PlayerContext] Locomotion Controller reference is missing.");
            if (Physics == null)
                Debug.LogError("[SH_PlayerContext] Physics Motor reference is missing.");
            if (Settings == null)
                Debug.LogError("[SH_PlayerContext] Movement Settings asset is missing.");
            if (Animator == null)
                Debug.LogError("[SH_PlayerContext] Animator reference is missing.");
            if (AnimatorBridge == null)
                Debug.LogError("[SH_PlayerContext] Animator Bridge reference is missing.");

            // Economic Systems
            if (Health == null)
                Debug.LogError("[SH_PlayerContext] Health Component reference is missing.");
            if (Resources == null)
                Debug.LogError("[SH_PlayerContext] Resource System reference is missing.");
            if (EconomicEvents == null)
                Debug.LogError("[SH_PlayerContext] Economic Event Manager reference is missing.");
            if (EconomySettings == null)
                Debug.LogError("[SH_PlayerContext] Economy Settings asset is missing.");
            if (EconomicEventSettings == null)
                Debug.LogError("[SH_PlayerContext] Economic Event Settings asset is missing.");
        }

        /// <summary>
        /// Injects dependencies into sub-systems and establishes the Observer
        /// connections between economic components.
        /// Order of initialization matters: settings assets first, then components
        /// that depend on those assets, then cross-system event subscriptions last.
        /// </summary>
        private void OrchestrateSubsystems()
        {
            // --- Movement & Control ---

            // Establish camera authority for input projection.
            Perspective.Initialize(Settings);

            // Prepare locomotion for processing movement intentions.
            Locomotion.Initialize(Input, Settings, Physics, Perspective);

            // Enable state-driven visual feedback.
            AnimatorBridge.Initialize(Animator);

            // --- Economic Systems ---

            // Initialize health component with economic settings.
            // (maxDurability and defeatThreshold live in EconomySettings.)
            Health.Initialize(EconomySettings);

            // Initialize resource system with economic settings.
            // (maxEnergy, IC curves, Scrap costs live in EconomySettings.)
            Resources.Initialize(EconomySettings);

            // Initialize event manager with event settings and a reference
            // to the resource system it will apply modifiers to.
            EconomicEvents.Initialize(EconomicEventSettings, Resources);

            // --- Cross-System Observer Connections ---

            // When the Mecha reaches the defeat threshold, the resource system
            // applies the IC and EC defeat penalties automatically.
            // Neither system calls the other directly: the event is the bridge.
            Health.OnDefeated += Resources.ApplyDefeatPenalty;
        }

        #endregion
    }
}