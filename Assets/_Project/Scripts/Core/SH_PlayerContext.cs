using Animation;
using Core;
using Core.Camera;
using Core.Input;
using Core.Locomotion;
using Core.Physics;
using Data;
using Game.Economy;
using Game.Economy.Data;
using Game.Interaction;
using Game.Interaction.Data;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Immutable dependency container acting as the Single Source of Truth (SSOT).
    /// Orchestrates the initialization and communication between the State Machine
    /// and the Mecha's sub-systems while maintaining strict architectural decoupling.
    ///
    /// Extended to include the interaction sub-system:
    ///   - SH_InteractionController: Manages detection, hold timer,
    ///     and interaction resolution for all IInteractable world objects.
    ///   - SH_InteractionSettings:   Central interaction configuration asset.
    /// </summary>
    public sealed class SH_PlayerContext
    {
        #region Read-Only Authorities — Movement & Control

        public Transform Transform { get; }
        public SH_InputHandler Input { get; }
        public SH_PerspectiveController Perspective { get; }
        public SH_LocomotionController Locomotion { get; }
        public SH_PhysicsMotor Physics { get; }
        public SH_MovementSettings Settings { get; }
        public Animator Animator { get; }
        public SH_AnimatorBridge AnimatorBridge { get; }

        #endregion

        #region Read-Only Authorities — Economic Systems

        public SH_HealthComponent Health { get; }
        public SH_ResourceSystem Resources { get; }
        public SH_EconomicEventManager EconomicEvents { get; }
        public SH_EconomySettings EconomySettings { get; }
        public SH_EconomicEventSettings EconomicEventSettings { get; }

        #endregion

        #region Read-Only Authorities — Interaction System (GDD §5.2)

        /// <summary>
        /// Central authority for world interaction detection, hold timing,
        /// and interaction resolution for all IInteractable objects.
        /// Consumed by FSM states to forward input signals.
        /// </summary>
        public SH_InteractionController Interaction { get; }

        /// <summary>
        /// Central interaction configuration asset.
        /// Exposed for UI systems and debug tools that need to read
        /// detection radius, hold durations, or accessibility settings
        /// without coupling to SH_InteractionController internals.
        /// </summary>
        public SH_InteractionSettings InteractionSettings { get; }

        #endregion

        #region Constructor & Orchestration

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
            SH_EconomicEventSettings economicEventSettings,
            SH_InteractionController interaction,
            SH_InteractionSettings interactionSettings)
        {
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
            Interaction = interaction;
            InteractionSettings = interactionSettings;

            ValidateDependencies();
            OrchestrateSubsystems();
        }

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

            // Interaction System
            if (Interaction == null)
                Debug.LogError("[SH_PlayerContext] Interaction Controller reference is missing.");
            if (InteractionSettings == null)
                Debug.LogError("[SH_PlayerContext] Interaction Settings asset is missing.");
        }

        private void OrchestrateSubsystems()
        {
            // --- Movement & Control ---
            Perspective.Initialize(Settings);
            Locomotion.Initialize(Input, Settings, Physics, Perspective);
            AnimatorBridge.Initialize(Animator);

            // --- Economic Systems ---
            Health.Initialize(EconomySettings);
            Resources.Initialize(EconomySettings);
            EconomicEvents.Initialize(EconomicEventSettings, Resources);

            // --- Cross-System Observer Connections (Economy) ---
            Health.OnDefeated += Resources.ApplyDefeatPenalty;

            // --- Interaction System ---
            // Initialize after economy so the controller has a fully-ready context.
            Interaction.Initialize(InteractionSettings, this);

            // Subscribe damage interruption: being hit cancels an ongoing hold (GDD §5.2.1).
            Health.OnDamageReceived += (_, __, ___) => Interaction.NotifyDamageReceived();
        }

        #endregion
    }
}
