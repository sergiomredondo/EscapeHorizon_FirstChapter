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
    /// Terminology mapping:
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
        bool IsStaggered { get; }
        bool IsDead { get; }
        bool IsBlocking { get; }
        bool IsInParryWindow { get; }
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
