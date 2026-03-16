using UnityEngine;

namespace Game.Combat.Core
{
    /// <summary>
    /// Value object carrying all parameters of a single combat hit.
    /// Passed from SH_HitboxController to ICombatTarget.TakeDamage()
    /// so no system needs to pass individual floats or reference the attacker.
    ///
    /// Terminology mapping (GDD §5.3.4 → code):
    ///   Daño Efectivo (DE)       → EffectiveDamage
    ///   Daño de Postura (DP)     → PostureDamage
    ///   Daño Cinético            → KineticDamage (DamageType enum)
    ///   Stagger / knockback      → KnockbackImpulse (world-space vector)
    ///   Hit-stop (frame freeze)  → HitstopDuration
    ///
    /// Design decision:
    ///   This is a struct (value type) so it can be passed without heap
    ///   allocation in the per-hit hot path. Fields are public for direct
    ///   read access; mutation after creation is intentional only via the
    ///   builder pattern in SH_DamageCalculator.BuildPayload().
    /// </summary>
    public struct SH_DamagePayload
    {
        // ─── Damage values ────────────────────────────────────────────────

        /// <summary>
        /// Effective kinetic damage to apply to the target's Durability.
        /// Result of DE = (AV_base × AttackMultiplier) - (DV_base × DefenseEffectiveness).
        /// Always >= 0. A value of 0 means the hit connected but dealt no
        /// structural damage (e.g. fully blocked).
        /// </summary>
        public float EffectiveDamage;

        /// <summary>
        /// Secondary posture damage that erodes the target's PostureValue.
        /// Calculated in parallel with EffectiveDamage. Reaching PostureValue = 0
        /// triggers a Stagger window on the target.
        /// </summary>
        public float PostureDamage;

        // ─── Hit classification ───────────────────────────────────────────

        /// <summary>
        /// Whether this hit was computed as a critical hit (1.5× multiplier).
        /// Determined by positional conditions (e.g. back attack) in
        /// SH_DamageCalculator, not by randomness or Agility.
        /// </summary>
        public bool IsCritical;

        /// <summary>
        /// Whether the hit was fully absorbed by a successful Parry
        /// (EffectiveDamage == 0, but posture damage is applied to attacker).
        /// </summary>
        public bool WasParried;

        /// <summary>
        /// Whether the hit was partially mitigated by a Block
        /// (80–90% damage reduction applied).
        /// </summary>
        public bool WasBlocked;

        // ─── Physics response ─────────────────────────────────────────────

        /// <summary>
        /// World-space knockback impulse to apply to the target's physics motor.
        /// Derived from SH_ActionData.staggerImpulse and the attacker's facing direction.
        /// Zero vector means no knockback (e.g. blocked hits).
        /// </summary>
        public Vector3 KnockbackImpulse;

        /// <summary>
        /// Duration in seconds of the hit-stop (frame-freeze) effect triggered
        /// on both attacker and target upon a clean hit. Zero disables hit-stop.
        /// Sourced from SH_ActionData.hitstopDuration.
        /// </summary>
        public float HitstopDuration;

        // ─── Metadata ─────────────────────────────────────────────────────

        /// <summary>
        /// World-space position where the hit was detected.
        /// Used by the feedback system to spawn floating damage numbers
        /// and visual hit effects at the correct location.
        /// </summary>
        public Vector3 HitPoint;

        /// <summary>
        /// Whether this hit originated from a player-controlled entity.
        /// Used by SH_CaptiveCore to distinguish player attacks (eligible for
        /// ForceDestroy) from environmental damage.
        /// </summary>
        public bool IsPlayerAttack;

        // ─── Factory ──────────────────────────────────────────────────────

        /// <summary>
        /// Creates a zero-damage informational payload with default values.
        /// Use SH_DamageCalculator.BuildPayload() for real combat hits.
        /// </summary>
        public static SH_DamagePayload Empty => new SH_DamagePayload
        {
            EffectiveDamage  = 0f,
            PostureDamage    = 0f,
            IsCritical       = false,
            WasParried       = false,
            WasBlocked       = false,
            KnockbackImpulse = Vector3.zero,
            HitstopDuration  = 0f,
            HitPoint         = Vector3.zero,
            IsPlayerAttack   = false
        };
    }
}
