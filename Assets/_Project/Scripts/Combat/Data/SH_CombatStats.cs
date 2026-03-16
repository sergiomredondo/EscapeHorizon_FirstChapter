using UnityEngine;

namespace Game.Combat.Data
{
    /// <summary>
    /// Per-entity-archetype base combat attribute sheet.
    /// One asset per archetype: player, Assailant, Tank, Flanker, Boss variants.
    ///
    /// Consumed by:
    ///   - SH_DamageCalculator to compute AttackValue and DefenseValue.
    ///   - SH_PlayerCombatController (player sheet).
    ///   - SH_EnemyController (Stage B, enemy sheets).
    ///
    /// Terminology mapping (GDD §5.3.4 → code):
    ///   Fuerza (F)    → Strength   — drives AttackValue (AV_base)
    ///   Defensa (D)   → Defense    — drives DefenseValue (DV_base)
    ///   Agilidad (A)  → Agility    — drives evasion and parry success probability
    ///   Postura (PST) → PostureMax — maximum posture before stagger
    ///
    /// Design note:
    ///   These are base values before any build tree multipliers or Energy Surge
    ///   amplification. The skill tree will apply additive/multiplicative modifiers
    ///   at runtime. For Stage A (no skill tree yet), these are the effective values.
    /// </summary>
    [CreateAssetMenu(
        fileName = "CombatStats",
        menuName  = "ScapeHorizon/Combat/CombatStats",
        order     = 310)]
    public class SH_CombatStats : ScriptableObject
    {
        #region Core Attributes (GDD §5.3.4)

        [Header("Core Attributes")]

        [Tooltip("Strength (Fuerza). Primary driver of AttackValue. " +
                 "AV_base = Strength × SH_CombatSettings.attackMultiplier. " +
                 "Player base: 10. Scales with Development Points via skill tree.")]
        [Min(1f)]
        public float Strength = 10f;

        [Tooltip("Defense (Defensa). Primary driver of DefenseValue. " +
                 "DV_base = Defense. Applied via SH_CombatSettings.defenseEffectiveness. " +
                 "Player base: 8. Tank archetype: higher. Flanker archetype: low.")]
        [Min(0f)]
        public float Defense = 8f;

        [Tooltip("Agility (Agilidad). Drives evasion success probability and parry timing. " +
                 "Higher values widen the effective parry window and increase dodge distance. " +
                 "Player base: 6. Flanker archetype: high. Tank archetype: low.")]
        [Min(0f)]
        public float Agility = 6f;

        #endregion

        #region Posture (GDD §5.3.4 — Postura)

        [Header("Posture")]

        [Tooltip("Maximum posture points before a stagger event is triggered. " +
                 "Corresponds to PST (Postura del defensor) in GDD §5.3.4. " +
                 "When PostureDamage accumulated exceeds this value, IsStaggered = true. " +
                 "Higher values make the entity harder to stagger.")]
        [Min(1f)]
        public float PostureMax = 100f;

        #endregion

        #region Health (Durability)

        [Header("Health")]

        [Tooltip("Maximum Durability for this archetype. " +
                 "For the player, this is overridden by SH_EconomySettings.maxDurability. " +
                 "For enemies this is the authoritative HP value, scaled per zone by " +
                 "SH_CombatSettings.enemyHpScalingPerZone.")]
        [Min(1f)]
        public float MaxDurability = 100f;

        #endregion

        #region Archetype Classification (GDD §5.3.3)

        [Header("Archetype Classification")]

        /// <summary>
        /// Classification enum matching the three enemy archetypes defined in GDD §5.3.3.
        /// Used by SH_EnemyController (Stage B) to select the correct behavior tree.
        /// </summary>
        public EnemyArchetype Archetype = EnemyArchetype.Assailant;

        [Tooltip("Whether this archetype is immune to standard Stagger. " +
                 "GDD §5.3.5: Bosses are immune to standard Stagger; only Energy Surge " +
                 "can break their posture to create a vulnerability window.")]
        public bool IsStaggerImmune = false;

        #endregion

        #region Editor Validation

        private void OnValidate()
        {
            Strength      = Mathf.Max(1f, Strength);
            Defense       = Mathf.Max(0f, Defense);
            Agility       = Mathf.Max(0f, Agility);
            PostureMax    = Mathf.Max(1f, PostureMax);
            MaxDurability = Mathf.Max(1f, MaxDurability);
        }

        #endregion
    }

    /// <summary>
    /// Enemy role classification matching GDD §5.3.3 terminology.
    /// Used by SH_EnemyController (Stage B) to drive behavior module selection.
    ///
    /// GDD terminology → enum value:
    ///   Asaltante (Agresores) → Assailant
    ///   Tanques (Defensivos)  → Tank
    ///   Flanqueadores (Ágiles)→ Flanker
    /// </summary>
    public enum EnemyArchetype
    {
        /// <summary>
        /// Medium speed, high damage, low resilience.
        /// Closes distance rapidly and chains attack combos.
        /// </summary>
        Assailant,

        /// <summary>
        /// Low speed, medium damage, high resilience.
        /// Holds position, prioritizes blocking/parrying, punishes misses.
        /// </summary>
        Tank,

        /// <summary>
        /// High speed, low damage, low resilience.
        /// Flanks, targets back, attacks after failed dodge by the mecha.
        /// </summary>
        Flanker,

        /// <summary>
        /// Special classification for boss-tier entities.
        /// Immune to standard stagger. Multi-phase. See GDD §5.3.5.
        /// </summary>
        Boss
    }
}
