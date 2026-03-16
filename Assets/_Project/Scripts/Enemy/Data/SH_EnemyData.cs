using UnityEngine;
using Game.Combat.Data;
using Game.Economy.Data;

namespace Game.Enemy.Data
{
    /// <summary>
    /// Per-archetype configuration asset for an enemy unit.
    /// Combines combat stats (Strength, Defense, Agility) with AI behavioral
    /// parameters and economic reward data into a single designer-facing asset.
    ///
    /// One asset per archetype: Assailant.asset, Tank.asset, Flanker.asset,
    /// plus optional variants (EliteAssailant.asset, etc. for zone scaling).
    ///
    /// Terminology mapping (GDD §5.3.3 → code):
    ///   Asaltante (Agresores) → EnemyArchetype.Assailant
    ///   Tanque   (Defensivos) → EnemyArchetype.Tank
    ///   Flanqueador (Ágiles)  → EnemyArchetype.Flanker
    ///
    /// Responsibility boundaries:
    ///   OWNS: All tunable parameters that define this archetype's personality.
    ///   DOES NOT OWN: Runtime state (current HP, posture) — that lives in
    ///                 SH_EnemyController per-instance.
    ///   DOES NOT OWN: Damage formula (SH_DamageCalculator).
    ///   DOES NOT OWN: Drop delivery (SH_ResourceDropData).
    /// </summary>
    [CreateAssetMenu(
        fileName = "EnemyData",
        menuName  = "ScapeHorizon/Enemy/EnemyData",
        order     = 320)]
    public class SH_EnemyData : ScriptableObject
    {
        #region Identity

        [Header("Identity")]

        [Tooltip("Display name used in UI, debug panels, and telemetry logs.")]
        public string DisplayName = "Enemy";

        [Tooltip("Archetype classification. Drives AI behavior module selection and " +
                 "SH_DifficultyManager zone scaling logic.")]
        public EnemyArchetype Archetype = EnemyArchetype.Assailant;

        [Tooltip("If true, this unit is an Elite variant. " +
                 "SH_PlayerCombatController calls RollEnergyEventOnEliteEncounter() " +
                 "when the hitbox connects against an Elite.")]
        public bool IsElite = false;

        #endregion

        #region Combat Stats (GDD §5.3.4)

        [Header("Combat Stats")]

        [Tooltip("Base combat attribute sheet. Drives AttackValue and DefenseValue " +
                 "in SH_DamageCalculator. Assign the archetype's SH_CombatStats asset.")]
        public SH_CombatStats CombatStats;

        #endregion

        #region Health & Posture

        [Header("Health & Posture")]

        [Tooltip("Maximum Durability (HP). Overrides SH_CombatStats.MaxDurability when " +
                 "not zero, allowing per-variant health tuning without creating a new " +
                 "SH_CombatStats asset. Set to 0 to use CombatStats.MaxDurability.")]
        [Min(0f)]
        public float MaxDurabilityOverride = 0f;

        [Tooltip("Maximum posture points before stagger. Overrides SH_CombatStats.PostureMax " +
                 "when not zero. Set to 0 to use CombatStats.PostureMax.")]
        [Min(0f)]
        public float PostureMaxOverride = 0f;

        /// <summary>
        /// Resolved max durability, respecting the override field.
        /// </summary>
        public float ResolvedMaxDurability =>
            MaxDurabilityOverride > 0f
                ? MaxDurabilityOverride
                : (CombatStats != null ? CombatStats.MaxDurability : 100f);

        /// <summary>
        /// Resolved max posture, respecting the override field.
        /// </summary>
        public float ResolvedPostureMax =>
            PostureMaxOverride > 0f
                ? PostureMaxOverride
                : (CombatStats != null ? CombatStats.PostureMax : 100f);

        #endregion

        #region AI Behavioral Parameters (GDD §5.3.3)

        [Header("AI — Detection & Navigation")]

        [Tooltip("Distance at which the enemy enters Search state after detecting " +
                 "the player (sight or sound). GDD §5.3.3.")]
        [Min(0.5f)]
        public float DetectionRange = 10f;

        [Tooltip("Distance at which the enemy transitions from Search to Attack state. " +
                 "Must be <= DetectionRange.")]
        [Min(0.5f)]
        public float AttackEngageRange = 6f;

        [Tooltip("Melee attack range. When the player is within this distance and " +
                 "the attack cooldown is ready, the enemy executes a combo.")]
        [Min(0.5f)]
        public float MeleeAttackRange = 2.5f;

        [Tooltip("Movement speed during Patrol and Search states (m/s).")]
        [Min(0f)]
        public float PatrolSpeed = 2f;

        [Tooltip("Movement speed during Attack pursuit (m/s). " +
                 "GDD §5.3.3: Assailant = fast, Tank = slow, Flanker = fast.")]
        [Min(0f)]
        public float PursuitSpeed = 4f;

        [Tooltip("Angular speed for orientation toward the player (deg/s).")]
        [Min(0f)]
        public float RotationSpeed = 180f;

        [Header("AI — Combat Behavior")]

        [Tooltip("Minimum time (seconds) between melee combo executions. " +
                 "Higher = more deliberate. GDD §5.3.3: Tank has higher cooldowns.")]
        [Min(0.1f)]
        public float AttackCooldown = 2.0f;

        [Tooltip("Number of attack iterations in a single combo window. " +
                 "Assailant: 2–3, Tank: 1–2, Flanker: 1.")]
        [Range(1, 5)]
        public int ComboLength = 2;

        [Tooltip("Probability (0–1) that this unit attempts to block an incoming attack. " +
                 "GDD §5.3.3: Tank prioritizes blocking/parrying.")]
        [Range(0f, 1f)]
        public float BlockProbability = 0.2f;

        [Tooltip("Duration (seconds) of the active block window per attempt.")]
        [Min(0.1f)]
        public float BlockDuration = 0.4f;

        [Tooltip("Probability (0–1) that a block attempt upgrades to a Parry (100% mitigation). " +
                 "GDD §5.3.4: Parry factor = 0.")]
        [Range(0f, 1f)]
        public float ParryUpgradeProbability = 0.1f;

        [Tooltip("Probability (0–1) that this unit attempts to evade (step back / strafe) " +
                 "when the player activates Energy Surge. GDD §5.3.3.")]
        [Range(0f, 1f)]
        public float SurgeEvadeProbability = 0.7f;

        [Tooltip("Distance retreated during an evasion step (m). " +
                 "GDD §5.3.3: Flanker evades farther than Assailant.")]
        [Min(0f)]
        public float EvasionDistance = 2.5f;

        [Tooltip("Health fraction (0–1) at which the enemy transitions to " +
                 "a critical-health behavior pattern (desperate last attack or retreat). " +
                 "GDD §5.3.3: critical health triggers more aggressive or defensive shift.")]
        [Range(0f, 0.5f)]
        public float CriticalHealthThreshold = 0.2f;

        [Tooltip("If true, this unit retreats when health drops below CriticalHealthThreshold. " +
                 "If false, it enters a last-stand aggressive pattern instead.")]
        public bool RetreatsAtCriticalHealth = false;

        [Header("AI — Group Behavior (GDD §5.3.3.6)")]

        [Tooltip("If true, this unit acts as the Group Leader when spawned in a squad. " +
                 "Only one leader per group. Leader fixes combat range and absorbs aggro. " +
                 "GDD §5.3.3.6: Leader is typically a Tank.")]
        public bool CanBeGroupLeader = false;

        [Tooltip("Minimum distance this unit keeps from other enemies in the same squad. " +
                 "Prevents stacking and allows tactical positioning.")]
        [Min(0f)]
        public float MinGroupSpacing = 1.5f;

        #endregion

        #region Reward Data (GDD §5.5 — Economic Integration)

        [Header("Reward Data")]

        [Tooltip("Drop data asset defining Scrap and Energy rewards on defeat. " +
                 "Used by SH_EnemyController.OnDefeated() to call DeliverDestroyRewards(). " +
                 "For Captive Automata, use SH_CaptiveCore instead — this field handles " +
                 "non-captive enemy types that are simply destroyed.")]
        public SH_ResourceDropData DropData;

        #endregion

        #region Editor Validation

        private void OnValidate()
        {
            AttackEngageRange = Mathf.Min(AttackEngageRange, DetectionRange);
            MeleeAttackRange  = Mathf.Min(MeleeAttackRange, AttackEngageRange);

            PatrolSpeed     = Mathf.Max(0f, PatrolSpeed);
            PursuitSpeed    = Mathf.Max(0f, PursuitSpeed);
            RotationSpeed   = Mathf.Max(0f, RotationSpeed);
            AttackCooldown  = Mathf.Max(0.1f, AttackCooldown);
            ComboLength     = Mathf.Clamp(ComboLength, 1, 5);

            BlockProbability        = Mathf.Clamp01(BlockProbability);
            ParryUpgradeProbability = Mathf.Clamp01(ParryUpgradeProbability);
            SurgeEvadeProbability   = Mathf.Clamp01(SurgeEvadeProbability);
            EvasionDistance         = Mathf.Max(0f, EvasionDistance);
            CriticalHealthThreshold = Mathf.Clamp(CriticalHealthThreshold, 0f, 0.5f);
            MinGroupSpacing         = Mathf.Max(0f, MinGroupSpacing);
        }

        #endregion
    }
}
