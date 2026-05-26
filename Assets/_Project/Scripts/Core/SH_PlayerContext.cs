using Animation;
using Core;
using Core.Camera;
using Core.Input;
using Core.Locomotion;
using Core.Physics;
using Core.StateMachine;
using Data;
using Game.Combat.Core;
using Game.Combat.Data;
using Game.Economy;
using Game.Economy.Data;
using Game.Interaction;
using Game.Interaction.Data;
using Game.Progression;
using System;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Immutable dependency container — Single Source of Truth (SSOT).
    ///
    /// Extended for Stage B (GDD §5.3 Stage B):
    ///   + SurgeSystem       — SH_EnergySurgeSystem (bar accumulation and auto-activation).
    ///   + DifficultyManager — SH_DifficultyManager (zone scaling + dynamic AI loop).
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
        public Animator[] Animators { get; }
        public SH_AnimatorBridge AnimatorBridge { get; }
        public SH_ActionAnimationMap ActionAnimationMap {  get; }

        #endregion

        #region Read-Only Authorities — FSM

        public SH_PlayerStateMachine StateMachine { get; }

        #endregion

        #region Read-Only Authorities — Economic Systems

        public SH_HealthComponent Health { get; }
        public SH_ResourceSystem Resources { get; }
        public SH_EconomicEventManager EconomicEvents { get; }
        public SH_EconomySettings EconomySettings { get; }
        public SH_EconomicEventSettings EconomicEventSettings { get; }

        #endregion

        #region Read-Only Authorities — Interaction System

        public SH_InteractionController Interaction { get; }
        public SH_InteractionSettings InteractionSettings { get; }

        #endregion

        #region Read-Only Authorities — Combat System (Stage A)

        public SH_PlayerCombatController CombatController { get; }
        public SH_HitboxController HitboxController { get; }
        public SH_CombatSettings CombatSettings { get; }
        public SH_CombatStats PlayerCombatStats { get; }

        public SH_PlayerFeedbackData FeedbackData { get; }

        #endregion

        #region Read-Only Authorities — Combat System (Stage B)

        /// <summary>
        /// Manages the Energy Surge bar accumulation, auto-activation, and drain.
        /// GDD §5.3.2: bar fills from damage dealt/received; at 100% activates Surge.
        /// </summary>
        public SH_EnergySurgeSystem SurgeSystem { get; }

        /// <summary>
        /// Manages build-based stat modifiers and special effects. 
        /// GDD §5.3.4: 3 branches with 5 nodes each, providing stat bonuses and unique effects.
        /// </summary>
        public SH_BuildSystem BuildSystem { get; }

        /// <summary>
        /// Manages zone-based difficulty scaling and the dynamic AI aggressiveness
        /// mini-loop (GDD §5.3.6). Tracks all active enemies.
        /// </summary>
        public SH_DifficultyManager DifficultyManager { get; }

        #endregion

        #region Delegate Storage — Subscriptions

        // Stored delegate reference to allow explicit unsubscription in Dispose().
        // Without this reference, the anonymous lambda cannot be removed with -=,
        // leaving the Interaction component retained by the Health event even after
        // both components have been logically decommissioned.
        private Action<float, float, float> _onDamageReceivedHandler;
        private Action<float, float, float> _onHitFeedbackHandler;

        #endregion

        #region Constructor & Orchestration

        public SH_PlayerContext(
            Transform transform,
            SH_InputHandler input,
            SH_PerspectiveController perspective,
            SH_LocomotionController locomotion,
            SH_PhysicsMotor physics,
            SH_MovementSettings settings,
            Animator[] animators,
            SH_AnimatorBridge animatorBridge,
            SH_ActionAnimationMap animationMap,
            SH_PlayerStateMachine stateMachine,
            SH_HealthComponent health,
            SH_ResourceSystem resources,
            SH_EconomicEventManager economicEvents,
            SH_EconomySettings economySettings,
            SH_EconomicEventSettings economicEventSettings,
            SH_InteractionController interaction,
            SH_InteractionSettings interactionSettings,
            SH_PlayerCombatController combatController,
            SH_HitboxController hitboxController,
            SH_CombatSettings combatSettings,
            SH_CombatStats playerCombatStats,
            SH_PlayerFeedbackData playerFeedbackData,
            SH_EnergySurgeSystem surgeSystem,
            SH_BuildSystem buildSystem,
            SH_DifficultyManager difficultyManager)
        {
            Transform = transform;
            Input = input;
            Perspective = perspective;
            Locomotion = locomotion;
            Physics = physics;
            Settings = settings;
            Animators = animators;
            AnimatorBridge = animatorBridge;
            ActionAnimationMap = animationMap;
            StateMachine = stateMachine;
            Health = health;
            Resources = resources;
            EconomicEvents = economicEvents;
            EconomySettings = economySettings;
            EconomicEventSettings = economicEventSettings;
            Interaction = interaction;
            InteractionSettings = interactionSettings;
            CombatController = combatController;
            HitboxController = hitboxController;
            CombatSettings = combatSettings;
            PlayerCombatStats = playerCombatStats;
            FeedbackData = playerFeedbackData;
            SurgeSystem = surgeSystem;
            BuildSystem = buildSystem;
            DifficultyManager = difficultyManager;

            ValidateDependencies();
            OrchestrateSubsystems();
        }

        private void ValidateDependencies()
        {
#if UNITY_EDITOR
            if (Transform == null) Debug.LogError("[SH_PlayerContext] Transform is missing.");
            if (Input == null) Debug.LogError("[SH_PlayerContext] InputHandler is missing.");
            if (Perspective == null) Debug.LogError("[SH_PlayerContext] PerspectiveController is missing.");
            if (Locomotion == null) Debug.LogError("[SH_PlayerContext] LocomotionController is missing.");
            if (Physics == null) Debug.LogError("[SH_PlayerContext] PhysicsMotor is missing.");
            if (Settings == null) Debug.LogError("[SH_PlayerContext] MovementSettings is missing.");
            if (Animators == null || Animators.Length == 0) Debug.LogError("[SH_PlayerContext] No Animators assigned.");
            if (AnimatorBridge == null) Debug.LogError("[SH_PlayerContext] AnimatorBridge is missing.");
            if (StateMachine == null) Debug.LogError("[SH_PlayerContext] StateMachine is missing.");
            if (Health == null) Debug.LogError("[SH_PlayerContext] HealthComponent is missing.");
            if (Resources == null) Debug.LogError("[SH_PlayerContext] ResourceSystem is missing.");
            if (EconomicEvents == null) Debug.LogError("[SH_PlayerContext] EconomicEventManager is missing.");
            if (EconomySettings == null) Debug.LogError("[SH_PlayerContext] EconomySettings is missing.");
            if (EconomicEventSettings == null) Debug.LogError("[SH_PlayerContext] EconomicEventSettings is missing.");
            if (Interaction == null) Debug.LogError("[SH_PlayerContext] InteractionController is missing.");
            if (InteractionSettings == null) Debug.LogError("[SH_PlayerContext] InteractionSettings is missing.");
            if (CombatController == null) Debug.LogError("[SH_PlayerContext] CombatController is missing.");
            if (HitboxController == null) Debug.LogError("[SH_PlayerContext] HitboxController is missing.");
            if (CombatSettings == null) Debug.LogError("[SH_PlayerContext] CombatSettings is missing.");
            if (PlayerCombatStats == null) Debug.LogError("[SH_PlayerContext] PlayerCombatStats is missing.");
            if (SurgeSystem == null) Debug.LogError("[SH_PlayerContext] SurgeSystem is missing.");
            if (BuildSystem == null) Debug.LogWarning("[SH_PlayerContext] BuildSystem is missing. Progression tree will not function.");
            if (DifficultyManager == null) Debug.LogError("[SH_PlayerContext] DifficultyManager is missing.");
#endif
        }

        private void OrchestrateSubsystems()
        {
            // Movement & Control
            Perspective.Initialize(Settings);
            Locomotion.Initialize(Input, Settings, Physics, Perspective);
            AnimatorBridge.Initialize(Animators);

            // Economic
            Health.Initialize(EconomySettings);
            Resources.Initialize(EconomySettings);
            EconomicEvents.Initialize(EconomicEventSettings, Resources);
            Health.OnDefeated += Resources.ApplyDefeatPenalty;

            // Set retreat threshold from economy settings.
            if (EconomySettings != null && Health != null)
            {
                float thresholdAbsolute = Health.MaxDurability * EconomySettings.retreatHealthThreshold;
                Health.SetRetreatThreshold(thresholdAbsolute);
            }

            // Interaction
            Interaction.Initialize(InteractionSettings, this);
            _onDamageReceivedHandler = (_, __, ___) => Interaction.NotifyDamageReceived();
            Health.OnDamageReceived += _onDamageReceivedHandler;

            // Player hit feedback — spawn effect and play audio on damage received.
            if (FeedbackData != null)
            {
                Health.OnDamageReceived += OnPlayerHitFeedback;
            }

            // Combat Stage A
            HitboxController.Initialize(this, CombatSettings, PlayerCombatStats);
            CombatController.Initialize(this, HitboxController, CombatSettings);

            // Combat Stage B
            SurgeSystem.Initialize(this, CombatController);
            DifficultyManager.Initialize(this);
            BuildSystem?.Initialize(this);
        }

        /// <summary>
        /// Plays hit feedback effects when the player takes damage.
        /// Subscribed to Health.OnDamageReceived. Parameters: (float currentHealth, float maxHealth, float damageTaken).
        /// </summary>
        /// <param name="current"></param>
        /// <param name="max"></param>
        /// <param name="damageTaken"></param>
        private void OnPlayerHitFeedback(float current, float max, float damageTaken)
        {
            if (FeedbackData == null) return;

            if (FeedbackData.hitEffectPrefab != null)
            {
                GameObject fx = UnityEngine.Object.Instantiate(
                    FeedbackData.hitEffectPrefab,
                    Transform.position,
                    Transform.rotation);
                UnityEngine.Object.Destroy(fx, FeedbackData.effectAutoDestroyTime);
            }

            if (FeedbackData.hitAudioClip != null)
                AudioSource.PlayClipAtPoint(FeedbackData.hitAudioClip, Transform.position);
        }

        /// <summary>
        /// Releases all event subscriptions held by this context.
        /// Must be called from SH_PlayerStateMachine.OnDestroy() to prevent
        /// delegate-retained references from blocking garbage collection.
        /// </summary>
        public void Dispose()
        {
            if (Health != null && _onDamageReceivedHandler != null)
            {
                Health.OnDamageReceived -= _onDamageReceivedHandler;
                _onDamageReceivedHandler = null;
            }
            if (Health != null && _onHitFeedbackHandler != null)
            {
                Health.OnDamageReceived -= OnPlayerHitFeedback;
            }
        }

        #endregion
    }
}
