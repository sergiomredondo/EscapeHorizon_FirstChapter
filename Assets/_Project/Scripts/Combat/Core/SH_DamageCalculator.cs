using UnityEngine;
using Game.Combat.Data;

namespace Game.Combat.Core
{
    /// <summary>
    /// Pure static utility class implementing all damage and posture formulas
    /// defined in GDD §5.3.4. No MonoBehaviour, no state, no Unity lifecycle.
    ///
    /// All methods are deterministic given the same inputs, making this class
    /// safe to call from any context and trivially testable in isolation.
    ///
    /// Primary formula (GDD §5.3.4):
    ///   DE = (AV_base × AttackMultiplier) - (DV_base × DefenseEffectiveness)
    ///
    /// Where:
    ///   AV_base  = attacker.Strength (from SH_CombatStats)
    ///   DV_base  = defender.Defense  (from SH_CombatStats)
    ///   AttackMultiplier  = light/heavy/surge multiplier (from SH_CombatSettings)
    ///   DefenseEffectiveness = settings.defenseEffectiveness
    ///
    /// The public entry point for combat hits is BuildPayload(), which runs the
    /// full formula pipeline and returns a ready-to-deliver SH_DamagePayload.
    /// </summary>
    public static class SH_DamageCalculator
    {
        // ─── Attack Value ─────────────────────────────────────────────────

        /// <summary>
        /// Computes the base AttackValue (AV_base) for an attacker.
        /// AV_base = attacker.Strength × attackMultiplier.
        /// The multiplier is sourced from SH_CombatSettings based on attack type.
        /// An optional Energy Surge multiplier is applied on top when active.
        /// </summary>
        public static float ComputeAttackValue(
            SH_CombatStats attackerStats,
            SH_CombatSettings settings,
            AttackType attackType,
            bool isSurgeActive)
        {
            if (attackerStats == null || settings == null) return 0f;

            float attackMult = attackType == AttackType.Heavy
                ? settings.heavyAttackMultiplier
                : settings.lightAttackMultiplier;

            float surgeMult = isSurgeActive ? settings.surgeDamageMultiplier : 1f;

            return attackerStats.Strength * attackMult * surgeMult;
        }

        // ─── Defense Value ────────────────────────────────────────────────

        /// <summary>
        /// Computes the effective DefenseValue (DV_base) for a defender.
        /// DV_base = defender.Defense.
        /// An optional Energy Surge multiplier is applied when the defender has Surge active.
        /// </summary>
        public static float ComputeDefenseValue(
            SH_CombatStats defenderStats,
            SH_CombatSettings settings,
            bool defenderSurgeActive)
        {
            if (defenderStats == null || settings == null) return 0f;

            float surgeMult = defenderSurgeActive ? settings.surgeDefenseMultiplier : 1f;
            return defenderStats.Defense * surgeMult;
        }

        // ─── Effective Damage ─────────────────────────────────────────────

        /// <summary>
        /// Applies the primary damage formula:
        ///   DE = (AV_base × AttackMultiplier) - (DV_base × DefenseEffectiveness)
        /// Clamps the result to 0 (a hit always deals at least 0 damage, never heals).
        /// Critical multiplier is applied to AV before subtraction.
        /// </summary>
        public static float ComputeEffectiveDamage(
            float attackValue,
            float defenseValue,
            SH_CombatSettings settings,
            bool isCritical)
        {
            if (settings == null) return 0f;

            float av = isCritical ? attackValue * settings.criticalMultiplier : attackValue;
            float de = av - (defenseValue * settings.defenseEffectiveness);
            return Mathf.Max(0f, de);
        }

        // ─── Posture Damage ───────────────────────────────────────────────

        /// <summary>
        /// Computes PostureDamage in parallel with EffectiveDamage.
        /// GDD §5.3.4: DP is linked to AV and Strength.
        /// PD = AV_base × postureDamageRatio.
        /// Surge amplifies posture damage for faster boss stagger windows.
        /// </summary>
        public static float ComputePostureDamage(
            float attackValue,
            SH_CombatSettings settings,
            bool isSurgeActive)
        {
            if (settings == null) return 0f;

            float surgeMult = isSurgeActive ? settings.surgePostureMultiplier : 1f;
            return attackValue * settings.postureDamageRatio * surgeMult;
        }

        // ─── Mitigation (Block / Parry) ───────────────────────────────────

        /// <summary>
        /// Applies block or parry mitigation to an already-computed effective damage value.
        ///
        /// Parry: 100% mitigation (or settings.parryDamageReduction if non-zero).
        /// Block: 80–90% mitigation (settings.blockDamageReduction factor).
        ///
        /// GDD §5.3.4 mitigation rules:
        ///   Block factor 0.1–0.2 → multiplied against damage (not subtracted).
        ///   Parry factor 0.0     → zero damage passes through.
        /// </summary>
        public static float ApplyMitigation(
            float effectiveDamage,
            SH_CombatSettings settings,
            bool isParry,
            bool isBlock)
        {
            if (settings == null) return effectiveDamage;

            if (isParry) return effectiveDamage * settings.parryDamageReduction;
            if (isBlock) return effectiveDamage * settings.blockDamageReduction;
            return effectiveDamage;
        }

        // ─── Critical Hit Detection ───────────────────────────────────────

        /// <summary>
        /// Determines whether a hit qualifies as a critical hit.
        /// GDD §5.3.4: Criticals occur by positional condition (back attack),
        /// not by randomness or Agility.
        ///
        /// A hit is critical when the angle between the attacker's facing direction
        /// and the vector from the defender to the attacker is within the back-attack
        /// cone (> 135° difference from defender's forward).
        /// </summary>
        public static bool IsCriticalHit(
            Vector3 attackerPosition,
            Vector3 defenderPosition,
            Vector3 defenderForward)
        {
            Vector3 toAttacker = (attackerPosition - defenderPosition).normalized;
            float dot = Vector3.Dot(defenderForward.normalized, toAttacker);
            // dot < -0.707 means the attacker is behind the defender (> 135° arc)
            return dot < -0.707f;
        }

        // ─── Knockback ────────────────────────────────────────────────────

        /// <summary>
        /// Computes the world-space knockback impulse applied to the target.
        /// Direction: away from the attacker on the horizontal plane.
        /// Magnitude: SH_ActionData.staggerImpulse, zero if hit was parried or blocked.
        /// </summary>
        public static Vector3 ComputeKnockback(
            Vector3 attackerPosition,
            Vector3 targetPosition,
            float staggerImpulse,
            bool wasParried,
            bool wasBlocked)
        {
            if (wasParried) return Vector3.zero;

            // Blocked hits still push, but at reduced magnitude
            float magnitude = wasBlocked ? staggerImpulse * 0.3f : staggerImpulse;

            Vector3 direction = (targetPosition - attackerPosition);
            direction.y = 0f;
            direction.Normalize();

            if (direction.sqrMagnitude < 0.001f)
                direction = Vector3.forward;

            return direction * magnitude;
        }

        // ─── Full Payload Builder ─────────────────────────────────────────

        /// <summary>
        /// Builds a complete SH_DamagePayload by running the full formula pipeline.
        /// This is the primary entry point called by SH_HitboxController for each
        /// valid hit detected during the active phase.
        ///
        /// Pipeline:
        ///   1. Compute AV and DV.
        ///   2. Detect critical hit (positional).
        ///   3. Compute effective damage (DE formula).
        ///   4. Detect parry / block from target state.
        ///   5. Apply mitigation.
        ///   6. Compute posture damage.
        ///   7. Compute knockback.
        ///   8. Pack into SH_DamagePayload.
        /// </summary>
        public static SH_DamagePayload BuildPayload(
            SH_CombatStats attackerStats,
            SH_CombatStats defenderStats,
            SH_CombatSettings settings,
            AttackType attackType,
            bool attackerSurgeActive,
            bool defenderSurgeActive,
            ICombatTarget target,
            Vector3 attackerPosition,
            float staggerImpulse,
            float hitstopDuration,
            Vector3 hitPoint,
            Vector3 defenderForward)
        {
            if (attackerStats == null || defenderStats == null || settings == null)
            {
                Debug.LogError("[SH_DamageCalculator] BuildPayload: null reference in parameters.");
                return SH_DamagePayload.Empty;
            }

            // 1. Base values
            float av = ComputeAttackValue(attackerStats, settings, attackType, attackerSurgeActive);
            float dv = ComputeDefenseValue(defenderStats, settings, defenderSurgeActive);

            // 2. Critical detection
            Vector3 defenderPos = target?.WorldPosition ?? hitPoint;
            bool isCritical = IsCriticalHit(attackerPosition, defenderPos, defenderForward);

            // 3. Effective damage (pre-mitigation)
            float de = ComputeEffectiveDamage(av, dv, settings, isCritical);

            // 4. Parry / block state from target
            bool isParry = target?.IsInParryWindow ?? false;
            bool isBlock = !isParry && (target?.IsBlocking ?? false);

            // 5. Apply mitigation
            float finalDamage = ApplyMitigation(de, settings, isParry, isBlock);

            // 6. Posture damage
            float postureDmg = ComputePostureDamage(av, settings, attackerSurgeActive);
            // Parried hits deal posture damage to the ATTACKER instead of the defender.
            // That logic is handled by SH_PlayerCombatController on parry detection.
            if (isParry) postureDmg = 0f;

            // 7. Knockback
            Vector3 knockback = ComputeKnockback(
                attackerPosition, defenderPos, staggerImpulse, isParry, isBlock);

            return new SH_DamagePayload
            {
                EffectiveDamage  = finalDamage,
                PostureDamage    = postureDmg,
                IsCritical       = isCritical,
                WasParried       = isParry,
                WasBlocked       = isBlock,
                KnockbackImpulse = knockback,
                HitstopDuration  = isParry ? 0f : hitstopDuration,
                HitPoint         = hitPoint,
                IsPlayerAttack   = true
            };
        }
    }

    /// <summary>
    /// Classifies the attack type for multiplier selection in SH_DamageCalculator.
    /// Determined by SH_PlayerCombatController based on hold duration of the attack input.
    /// GDD §5.1.1: tap = light attack, hold = heavy/charged attack.
    /// </summary>
    public enum AttackType
    {
        /// <summary>
        /// Quick tap attack. Lower multiplier, faster recovery.
        /// </summary>
        Light,

        /// <summary>
        /// Held/charged attack. Higher multiplier, locked commitment phase.
        /// </summary>
        Heavy
    }
}
