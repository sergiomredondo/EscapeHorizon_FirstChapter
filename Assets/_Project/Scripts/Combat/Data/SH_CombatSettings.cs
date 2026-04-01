using UnityEngine;

namespace Game.Combat.Data
{
    /// <summary>
    /// Central configuration asset for the combat damage system.
    /// Stores all tunable parameters for the damage formula, mitigation values,
    /// posture system, and difficulty scaling multipliers defined in GDD §5.3.4.
    ///
    /// One asset per project. Assign to SH_PlayerStateMachine in the Inspector.
    ///
    /// Terminology mapping (GDD §5.3.4 → code):
    ///   VA_base (Valor de Ataque base)    → AttackValue, derived from Strength
    ///   VD_base (Valor de Defensa base)   → DefenseValue, derived from Defense
    ///   Multiplicador de Ataque            → lightAttackMultiplier, heavyAttackMultiplier
    ///   Efectividad de Defensa             → defenseEffectiveness
    ///   Daño de Postura (DP)              → postureDamageRatio
    ///   PST (Postura del defensor)         → defined per-entity in SH_CombatStats
    ///   Golpe Crítico multiplier           → criticalMultiplier (1.5×)
    ///   Bloqueo reduction                  → blockDamageReduction (0.1–0.2)
    ///   Parry reduction                    → parryDamageReduction (0.0 = 100%)
    ///   Sobrecarga (Energy Surge)          → surgeDamageMultiplier, surgeDefenseMultiplier
    /// </summary>
    [CreateAssetMenu(
        fileName = "CombatSettings",
        menuName  = "ScapeHorizon/Settings/CombatSettings",
        order     = 303)]
    public class SH_CombatSettings : ScriptableObject
    {
        #region Attack Multipliers (GDD §5.3.4)

        [Header("Attack Multipliers")]

        [Tooltip("Damage multiplier applied to the base AttackValue for a light attack tap. " +
                 "Corresponds to Multiplicador de Ataque for Ataque Básico in GDD §5.3.4.")]
        [Min(0.1f)]
        public float lightAttackMultiplier = 1.0f;

        [Tooltip("Damage multiplier applied to the base AttackValue for a heavy/charged attack. " +
                 "Corresponds to Multiplicador de Ataque for Ataque Fuerte in GDD §5.3.4. " +
                 "Should be > lightAttackMultiplier to reward commitment.")]
        [Min(0.1f)]
        public float heavyAttackMultiplier = 1.8f;

        [Tooltip("Minimum hold duration (seconds) required to classify an attack input " +
                 "as a heavy/charged attack rather than a light/tap attack.")]
        [Min(0.05f)]
        public float heavyAttackHoldThreshold = 0.2f;

        #endregion

        #region Defense Formula (GDD §5.3.4)

        [Header("Defense Formula")]

        [Tooltip("Passive armor effectiveness fraction. Applied as: " +
                 "DE = (AV_base × AttackMult) - (DV_base × defenseEffectiveness). " +
                 "Range 0–1. Higher values make defense more effective per point.")]
        [Range(0f, 1f)]
        public float defenseEffectiveness = 0.5f;

        [Tooltip("Fraction of effective damage reduced when the target is blocking. " +
                 "GDD §5.3.4: 0.1–0.2 reduction factor (80–90% mitigation). " +
                 "Applied as: finalDamage = effectiveDamage × blockDamageReduction.")]
        [Range(0f, 0.5f)]
        public float blockDamageReduction = 0.15f;

        [Tooltip("Fraction of effective damage that passes through a perfect Parry. " +
                 "GDD §5.3.4: 0.0 = 100% mitigation. Must be 0 for a true counter-parry.")]
        [Range(0f, 0.1f)]
        public float parryDamageReduction = 0f;

        #endregion

        #region Critical Hits (GDD §5.3.4)

        [Header("Critical Hits")]

        [Tooltip("Damage multiplier applied to AV when a critical hit condition is met. " +
                 "GDD §5.3.4: 1.5× multiplier. Critical conditions: back attack, etc.")]
        [Min(1f)]
        public float criticalMultiplier = 1.5f;

        #endregion

        #region Posture System (GDD §5.3.4)

        [Header("Posture System")]

        [Tooltip("Fraction of effective attack value converted to posture damage per hit. " +
                 "GDD §5.3.4: DP is linked to VA and Strength. " +
                 "A ratio of 0.6 means 60% of the attack value hits posture in parallel.")]
        [Range(0f, 2f)]
        public float postureDamageRatio = 0.6f;

        [Tooltip("Duration (seconds) of the Stagger state after posture is broken. " +
                 "During this window the target is fully exposed and cannot act.")]
        [Min(0.1f)]
        public float staggerDuration = 2.5f;

        [Tooltip("Posture regeneration rate (points/second) when not being hit. " +
                 "Allows enemies to recover from partial posture damage over time.")]
        [Min(0f)]
        public float postureRegenRate = 8f;

        #endregion

        #region Energy Surge Multipliers

        [Header("Energy Surge")]

        [Tooltip("Damage multiplier applied to AttackValue while the Energy Surge state is active. " +
                 "Applied on top of the build's Strength modifier. " +
                 "GDD §5.3.2: Strength build can amplify from +20% to +50% during Surge.")]
        [Min(1f)]
        public float surgeDamageMultiplier = 1.5f;

        [Tooltip("Defense multiplier applied to DefenseValue while Energy Surge is active. " +
                 "GDD §5.3.2: Reason build amplifies Defense during Surge for survival.")]
        [Min(1f)]
        public float surgeDefenseMultiplier = 1.3f;

        [Tooltip("Posture damage multiplier during Energy Surge. " +
                 "Allows faster boss stagger during Surge windows (GDD §5.3.5).")]
        [Min(1f)]
        public float surgePostureMultiplier = 1.8f;

        [Tooltip("Duration (seconds) of the post-Surge cooldown period where " +
                 "Strength, Defense, and Agility are reduced below base values. " +
                 "GDD §5.3.2: brief Enfriamiento after Surge ends.")]
        [Min(0f)]
        public float surgeCooldownDuration = 3f;

        [Tooltip("Attribute reduction factor applied during the post-Surge cooldown. " +
                 "Values < 1.0 reduce effective stats below base. " +
                 "GDD §5.3.2: attributes drop slightly below base after Surge.")]
        [Range(0.5f, 1f)]
        public float surgeCooldownPenalty = 0.85f;

        #endregion

        #region Difficulty Scaling (GDD §5.3.6)

        [Header("Difficulty Scaling")]

        [Tooltip("HP multiplier applied to all enemies per zone level above 1. " +
                 "GDD §5.3.6: linear scaling, ~10% increase per zone.")]
        [Min(0f)]
        public float enemyHpScalingPerZone = 0.1f;

        [Tooltip("Attack value multiplier for enemies on Normal difficulty. " +
                 "GDD §5.3.6 table: Normal = 1.0×.")]
        [Min(0.1f)]
        public float difficultyNormalAttackMult = 1.0f;

        [Tooltip("Attack value multiplier for enemies on Hard (Mastery) difficulty. " +
                 "GDD §5.3.6 table: Hard = 1.3×.")]
        [Min(0.1f)]
        public float difficultyHardAttackMult = 1.3f;

        [Tooltip("HP multiplier for enemies on Hard difficulty. " +
                 "GDD §5.3.6 table: Hard = 1.2×.")]
        [Min(0.1f)]
        public float difficultyHardHpMult = 1.2f;

        [Tooltip("AI aggressiveness multiplier on Hard difficulty. " +
                 "GDD §5.3.6 table: Hard = 1.5×. Reduces time between attacks " +
                 "and shrinks parry windows.")]
        [Min(0.1f)]
        public float difficultyHardAIMult = 1.5f;

        #endregion

        #region Editor Validation

        private void OnValidate()
        {
            lightAttackMultiplier    = Mathf.Max(0.1f, lightAttackMultiplier);
            heavyAttackMultiplier    = Mathf.Max(lightAttackMultiplier, heavyAttackMultiplier);
            heavyAttackHoldThreshold = Mathf.Max(0.05f, heavyAttackHoldThreshold);
            defenseEffectiveness     = Mathf.Clamp01(defenseEffectiveness);
            blockDamageReduction     = Mathf.Clamp(blockDamageReduction, 0f, 0.5f);
            parryDamageReduction     = Mathf.Clamp(parryDamageReduction, 0f, 0.1f);
            criticalMultiplier       = Mathf.Max(1f, criticalMultiplier);
            postureDamageRatio       = Mathf.Max(0f, postureDamageRatio);
            staggerDuration          = Mathf.Max(0.1f, staggerDuration);
            postureRegenRate         = Mathf.Max(0f, postureRegenRate);
            surgeDamageMultiplier    = Mathf.Max(1f, surgeDamageMultiplier);
            surgeDefenseMultiplier   = Mathf.Max(1f, surgeDefenseMultiplier);
            surgePostureMultiplier   = Mathf.Max(1f, surgePostureMultiplier);
            surgeCooldownDuration    = Mathf.Max(0f, surgeCooldownDuration);
            surgeCooldownPenalty     = Mathf.Clamp(surgeCooldownPenalty, 0.5f, 1f);
        }

        #endregion
    }
}
