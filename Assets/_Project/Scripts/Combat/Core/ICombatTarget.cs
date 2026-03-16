using UnityEngine;

namespace Game.Combat.Core
{
    /// <summary>
    /// Contract that any entity capable of receiving combat hits must fulfill.
    /// Consumed exclusively by SH_HitboxController — that system knows nothing
    /// about enemy types, captive cores, or player health; it only knows this
    /// interface.
    ///
    /// Implementing types (Stage A): SH_PlayerCombatReceiver (player as target).
    /// Implementing types (Stage B): SH_EnemyController (enemy archetypes),
    ///                                SH_CaptiveCore extended (destroy-on-hit path).
    ///
    /// Terminology mapping (GDD §5.3.4 → code):
    ///   Daño Cinético        → payload.EffectiveDamage applied to Durability
    ///   Daño de Postura (DP) → payload.PostureDamage applied to PostureValue
    ///   Stagger              → IsStaggered property
    ///   Parry window         → IsInParryWindow property (set externally by AI/player state)
    ///   Block                → IsBlocking property
    ///
    /// Responsibility boundaries:
    ///   DEFINES: The minimum surface SH_HitboxController needs to deliver a hit.
    ///   DOES NOT DEFINE: AI behavior, animation, resource delivery, or
    ///                    entity lifecycle. Those belong to concrete types.
    /// </summary>
    public interface ICombatTarget
    {
        // ─── State queries ────────────────────────────────────────────────

        /// <summary>
        /// True while this target is in an active Stagger state (posture broken).
        /// During stagger the target cannot act and is fully exposed to damage.
        /// SH_HitboxController uses this to determine hit eligibility on bosses
        /// (GDD §5.3.5: bosses are only vulnerable during stagger windows).
        /// </summary>
        bool IsStaggered { get; }

        /// <summary>
        /// True if the target is no longer alive (Durability at or below defeat
        /// threshold). SH_HitboxController skips dead targets during overlap scans.
        /// </summary>
        bool IsDead { get; }

        /// <summary>
        /// True while the target is actively blocking.
        /// SH_DamageCalculator applies the 80–90% damage reduction factor when
        /// this flag is true at the moment of impact.
        /// </summary>
        bool IsBlocking { get; }

        /// <summary>
        /// True during the precise parry window defined by the target's state machine.
        /// SH_DamageCalculator applies 100% damage mitigation (and generates posture
        /// damage on the attacker) when this flag is true at impact.
        /// </summary>
        bool IsInParryWindow { get; }

        // ─── World position ───────────────────────────────────────────────

        /// <summary>
        /// World-space center position of this target.
        /// Used by SH_HitboxController for the OverlapSphere scan origin fallback
        /// and by the feedback system for hit effect placement.
        /// </summary>
        Vector3 WorldPosition { get; }

        // ─── Hit reception ────────────────────────────────────────────────

        /// <summary>
        /// Processes a single combat hit delivered by SH_HitboxController.
        /// The implementing type is responsible for applying EffectiveDamage to
        /// its health system, PostureDamage to its posture system, triggering
        /// knockback via its physics motor, and initiating hit-stop if applicable.
        ///
        /// Called once per valid overlap per active phase frame.
        /// The same target cannot be hit twice by the same active-phase window
        /// (de-duplication is enforced by SH_HitboxController's hit registry).
        /// </summary>
        /// <param name="payload">
        /// All parameters of this hit: effective damage, posture damage, critical
        /// flag, parry/block flags, knockback vector, hit-stop duration, and hit point.
        /// </param>
        void ReceiveHit(SH_DamagePayload payload);
    }
}
