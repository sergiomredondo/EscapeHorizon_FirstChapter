using Actions.Data;
using Core;
using Game.Combat.Data;
using Game.Interaction;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Combat.Core
{
    /// <summary>
    /// Activates a sphere overlap scan during the Active phase of SH_ActionState,
    /// detects ICombatTarget colliders, computes damage via SH_DamageCalculator,
    /// and delivers SH_DamagePayload to each valid target.
    ///
    /// Stage B additions:
    ///   + After delivering a hit, notifies SH_EnergySurgeSystem.NotifyDamageDealt()
    ///     so the surge bar fills from combat engagement (GDD §5.3.2).
    ///   + After delivering a hit, notifies SH_DifficultyManager.NotifyDamageDealt()
    ///     for RDIR measurement in the dynamic AI loop (GDD §5.3.6.4).
    ///   + Detects SH_EnemyData.IsElite on the hit target and calls
    ///     RollEnergyEventOnEliteEncounter() only for Elite archetype enemies,
    ///     replacing the unconditional call from Stage A.
    ///
    /// Lives on the Bear GameObject. Initialized by SH_PlayerContext.
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_HitboxController : MonoBehaviour
    {
        #region Dependencies

        private SH_PlayerContext _context;
        private SH_CombatSettings _combatSettings;
        private SH_CombatStats _playerStats;
        private bool _isInitialized;

        #endregion

        #region Runtime State

        private readonly HashSet<ICombatTarget> _hitRegistry = new HashSet<ICombatTarget>();
        private readonly Collider[] _overlapBuffer = new Collider[16];
        private SH_ActionData _currentAction;
        private AttackType _currentAttackType;
        private bool _surgeActiveAtActivation;

        #endregion

        #region Initialization

        public void Initialize(
            SH_PlayerContext context,
            SH_CombatSettings combatSettings,
            SH_CombatStats playerStats)
        {
            if (context == null)
            {
                Debug.LogError($"[SH_HitboxController] Initialize: context is null on {gameObject.name}.");
                return;
            }
            if (combatSettings == null)
            {
                Debug.LogError($"[SH_HitboxController] Initialize: combatSettings is null on {gameObject.name}.");
                return;
            }
            if (playerStats == null)
            {
                Debug.LogError($"[SH_HitboxController] Initialize: playerStats is null on {gameObject.name}.");
                return;
            }

            _context = context;
            _combatSettings = combatSettings;
            _playerStats = playerStats;
            _isInitialized = true;
        }

        #endregion

        #region Public API

        public void ActivateHitDetection(
            SH_ActionData action,
            AttackType attackType,
            bool surgeActive)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("[SH_HitboxController] ActivateHitDetection called before initialization.");
                return;
            }
            if (action == null)
            {
                Debug.LogWarning("[SH_HitboxController] ActivateHitDetection: action is null.");
                return;
            }

            _hitRegistry.Clear();
            _currentAction = action;
            _currentAttackType = attackType;
            _surgeActiveAtActivation = surgeActive;

            RunOverlapScan();
        }

        public void DeactivateHitDetection()
        {
            _hitRegistry.Clear();
            _currentAction = null;
        }

        #endregion

        #region Internal Scan Logic

        private void RunOverlapScan()
        {
            if (_currentAction == null) return;
            if (_currentAction.hitboxRadius <= 0f) return;

            Vector3 hitboxCenter = transform.TransformPoint(_currentAction.hitboxOffset);

            int count = Physics.OverlapSphereNonAlloc(
                hitboxCenter,
                _currentAction.hitboxRadius,
                _overlapBuffer,
                _currentAction.targetLayers);

            for (int i = 0; i < count; i++)
            {
                Collider col = _overlapBuffer[i];
                if (col == null) continue;
                if (col.gameObject == gameObject) continue;

                ProcessCollider(col, hitboxCenter);
            }
        }

        private void ProcessCollider(Collider col, Vector3 hitboxCenter)
        {
            // --- Path 1: Generic ICombatTarget ---
            ICombatTarget combatTarget = col.GetComponent<ICombatTarget>();
            if (combatTarget != null)
            {
                if (combatTarget.IsDead) return;
                if (_hitRegistry.Contains(combatTarget)) return;

                _hitRegistry.Add(combatTarget);
                DeliverHitToCombatTarget(combatTarget, col.ClosestPoint(hitboxCenter));
                return;
            }

            // --- Path 2: SH_CaptiveCore — ethical destroy path ---
            SH_CaptiveCore captiveCore = col.GetComponent<SH_CaptiveCore>();
            if (captiveCore != null)
            {
                if (!captiveCore.IsAvailable) return;
                captiveCore.ForceDestroy(_context);
                return;
            }
        }

        private void DeliverHitToCombatTarget(ICombatTarget target, Vector3 hitPoint)
        {
            SH_CombatStats defenderStats = null;
            if (target is MonoBehaviour mb)
                defenderStats = mb.GetComponent<SH_CombatStats>();

            if (defenderStats == null)
            {
                defenderStats = ScriptableObject.CreateInstance<SH_CombatStats>();
                defenderStats.Defense = 0f;
                Debug.LogWarning(
                    $"[SH_HitboxController] Target {((MonoBehaviour)target)?.gameObject.name} " +
                    $"has no SH_CombatStats. Using zero defense.");
            }

            Vector3 defenderForward = target is MonoBehaviour mbTarget
                ? mbTarget.transform.forward
                : Vector3.forward;

            SH_DamagePayload payload = SH_DamageCalculator.BuildPayload(
                attackerStats: _playerStats,
                defenderStats: defenderStats,
                settings: _combatSettings,
                attackType: _currentAttackType,
                attackerSurgeActive: _surgeActiveAtActivation,
                defenderSurgeActive: false,
                target: target,
                attackerPosition: transform.position,
                staggerImpulse: _currentAction.staggerImpulse,
                hitstopDuration: _currentAction.hitstopDuration,
                hitPoint: hitPoint,
                defenderForward: defenderForward);

            target.ReceiveHit(payload);

            // --- Stage B: Surge bar accumulation from damage dealt ---
            _context.SurgeSystem?.NotifyDamageDealt(payload.EffectiveDamage);

            // --- Stage B: Difficulty tracker RDIR measurement ---
            _context.DifficultyManager?.NotifyDamageDealt(payload.EffectiveDamage);

            // --- Stage B: Elite Energy Flux — only for IsElite enemies ---
            // IsElite is now read via a public property on SH_EnemyController,
            // eliminating the Reflection access that was present in the hot path.
            // The RollEnergyEventOnEliteEncounter is a probabilistic call (15%).
            // Called on any hit against an Elite regardless of hit outcome.
            if (target is MonoBehaviour enemyMb)
            {
                var enemyCtrl = enemyMb.GetComponent<Game.Enemy.SH_EnemyController>();
                if (enemyCtrl != null && enemyCtrl.IsElite)
                    _context.EconomicEvents?.RollEnergyEventOnEliteEncounter();
            }

            Debug.Log(
                $"[SH_HitboxController] Hit: {payload.EffectiveDamage:F1} kinetic, " +
                $"{payload.PostureDamage:F1} posture. " +
                $"Critical={payload.IsCritical}, Parried={payload.WasParried}, " +
                $"Blocked={payload.WasBlocked}.");
        }

        #endregion

        #region Gizmos

        private void OnDrawGizmosSelected()
        {
            if (_currentAction == null || _currentAction.hitboxRadius <= 0f) return;

            Vector3 center = transform.TransformPoint(_currentAction.hitboxOffset);
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.35f);
            Gizmos.DrawWireSphere(center, _currentAction.hitboxRadius);
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.1f);
            Gizmos.DrawSphere(center, _currentAction.hitboxRadius);
        }

        #endregion
    }
}
