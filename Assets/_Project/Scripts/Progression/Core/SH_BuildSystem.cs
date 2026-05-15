using System;
using Core;
using Game.Combat.Data;
using Game.Economy;
using Game.Economy.Data;
using Game.Progression.Data;
using UnityEngine;

namespace Game.Progression
{
    /// <summary>
    /// Manages the Analysis Tree (GDD §5.4.2).
    /// Owns: active branch selection, node activation/deactivation,
    /// stat modifier application to SH_CombatStats, reanalysis transactions.
    ///
    /// Permanent progression (PD earned) lives in SH_ResourceSystem.
    /// Ephemeral specialization (active nodes) lives here and resets on defeat.
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_BuildSystem : MonoBehaviour
    {
        #region Dependencies

        private SH_PlayerContext _context;
        private SH_BuildTreeData _treeData;
        private SH_CombatStats _baseStats;
        private SH_ResourceSystem _resources;
        private bool _isInitialized;

        #endregion

        #region Serialized

        [Header("Build Tree Asset")]
        [SerializeField] private SH_BuildTreeData _buildTreeData;

        #endregion

        #region Runtime State

        public BuildBranch ActiveBranch { get; private set; } = BuildBranch.Attack;
        public int ActiveNodeCount { get; private set; } = 0;
        public bool HasActiveBuild => ActiveNodeCount > 0;
        public int ReanalysisCount { get; private set; } = 0;

        // Cached base values — read once on Initialize to avoid polluting the asset.
        private float _baseStrength;
        private float _baseDefense;
        private float _baseAgility;
        private float _basePostureMax;

        #endregion

        #region Events

        public event Action<BuildBranch, int> OnNodeActivated;
        public event Action OnBuildDeactivated;
        public event Action<BuildBranch, int> OnActivationFailed;

        #endregion

        #region Initialization

        public void Initialize(SH_PlayerContext context)
        {
            if (context == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_BuildSystem] Initialize: null context on {gameObject.name}.");
#endif
                return;
            }
            if (_buildTreeData == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_BuildSystem] BuildTreeData not assigned on {gameObject.name}.");
#endif
                return;
            }

            _context = context;
            _treeData = _buildTreeData;
            _baseStats = context.PlayerCombatStats;
            _resources = context.Resources;

            // Cache the clean base values from the asset at startup.
            _baseStrength = _baseStats.Strength;
            _baseDefense = _baseStats.Defense;
            _baseAgility = _baseStats.Agility;
            _basePostureMax = _baseStats.PostureMax;

            _isInitialized = true;
#if UNITY_EDITOR
            Debug.Log("[SH_BuildSystem] Initialized.");
#endif
        }

        #endregion

        #region Public API — Node Activation

        /// <summary>
        /// Attempts to activate the next node in the given branch.
        /// Validates PD available, Scrap, and sequential order.
        /// </summary>
        public bool TryActivateNextNode(BuildBranch branch)
        {
            if (!_isInitialized) return false;

            var nodes = _treeData.GetBranchNodes(branch);

            // If switching branch, require reanalysis first.
            if (HasActiveBuild && branch != ActiveBranch)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[SH_BuildSystem] Cannot activate a different branch without " +
                                 "reanalysis. Call TryReanalyze() first.");
#endif
                OnActivationFailed?.Invoke(branch, 0);
                return false;
            }

            int nextIndex = ActiveNodeCount; // 0-based index into the branch array.
            if (nextIndex >= 5)
            {
#if UNITY_EDITOR
                Debug.Log("[SH_BuildSystem] Branch fully activated.");
#endif
                return false;
            }

            SH_BuildNodeData node = nodes[nextIndex];
            if (node == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_BuildSystem] Node at index {nextIndex} in branch " +
                                 $"{branch} is not assigned in BuildTreeData.");
#endif
                return false;
            }

            // Validate PD available.
            int availablePD = _resources.AvailableDevelopmentPoints;
            if (availablePD < node.pdCost)
            {
#if UNITY_EDITOR
                Debug.Log($"[SH_BuildSystem] Not enough PD. Need {node.pdCost}, have {availablePD}.");
#endif
                OnActivationFailed?.Invoke(branch, nextIndex);
                return false;
            }

            // Validate Scrap.
            if (_resources.CurrentScrap < node.scrapCost)
            {
#if UNITY_EDITOR
                Debug.Log($"[SH_BuildSystem] Not enough Scrap. Need {node.scrapCost:F0}, " +
                          $"have {_resources.CurrentScrap:F0}.");
#endif
                OnActivationFailed?.Invoke(branch, nextIndex);
                return false;
            }

            // Consume resources.
            _resources.ConsumeResource(ResourceType.Scrap, node.scrapCost);
            _resources.SpendDevelopmentPoint(node.pdCost);

            ActiveBranch = branch;
            ActiveNodeCount = nextIndex + 1;

            ApplyBuildModifiers();

            OnNodeActivated?.Invoke(branch, ActiveNodeCount);
#if UNITY_EDITOR
            Debug.Log($"[SH_BuildSystem] Node {ActiveNodeCount} activated in branch {branch}. " +
                      $"'{node.nodeName}'");
#endif
            return true;
        }

        /// <summary>
        /// Switches the active branch. Deactivates current build, charges reanalysis cost.
        /// </summary>
        public bool TryReanalyze(BuildBranch newBranch)
        {
            if (!_isInitialized) return false;
            if (newBranch == ActiveBranch && HasActiveBuild)
            {
#if UNITY_EDITOR
                Debug.Log("[SH_BuildSystem] Already on this branch.");
#endif
                return false;
            }

            float reanalysisCost = _treeData.reanalysisCostBase
                * Mathf.Pow(_treeData.reanalysisCostMultiplier, ReanalysisCount);

            if (_resources.CurrentScrap < reanalysisCost)
            {
#if UNITY_EDITOR
                Debug.Log($"[SH_BuildSystem] Not enough Scrap for reanalysis. " +
                          $"Need {reanalysisCost:F0}, have {_resources.CurrentScrap:F0}.");
#endif
                return false;
            }

            _resources.ConsumeResource(ResourceType.Scrap, reanalysisCost);
            ResetActiveBuild();
            ActiveBranch = newBranch;
            ReanalysisCount++;
#if UNITY_EDITOR
            Debug.Log($"[SH_BuildSystem] Reanalysis performed. New branch: {newBranch}. " +
                      $"Cost: {reanalysisCost:F0} Scrap.");
#endif
            return true;
        }

        /// <summary>
        /// Deactivates all active nodes and restores PD. Called on defeat.
        /// </summary>
        public void DeactivateBuild()
        {
            if (!HasActiveBuild) return;
            ResetActiveBuild();
            ReanalysisCount = 0;
            OnBuildDeactivated?.Invoke();
#if UNITY_EDITOR
            Debug.Log("[SH_BuildSystem] Build deactivated on defeat. PD returned.");
#endif
        }

        #endregion

        #region Public Queries

        public float GetCurrentDashCostReduction()
        {
            if (!HasActiveBuild) return 0f;
            return AccumulateModifier(n => n.dashEnergyCostReduction);
        }

        public float GetFlatDamageReduction()
        {
            if (!HasActiveBuild) return 0f;
            return AccumulateModifier(n => n.flatDamageReduction);
        }

        public float GetPostureIgnoreChance()
        {
            if (!HasActiveBuild) return 0f;
            return AccumulateModifier(n => n.postureIgnoreChance);
        }

        public float GetPerfectDashNegateChance()
        {
            if (!HasActiveBuild) return 0f;
            return AccumulateModifier(n => n.perfectDashNegateChance);
        }

        public float GetCriticalDamageBonus()
        {
            if (!HasActiveBuild) return 0f;
            return AccumulateModifier(n => n.criticalDamageBonus);
        }

        public float GetPostureDamageBonus()
        {
            if (!HasActiveBuild) return 0f;
            return AccumulateModifier(n => n.postureDamageBonus);
        }

        public float GetReanalysisCost()
        {
            if (_treeData == null) return 0f;
            return _treeData.reanalysisCostBase
                * Mathf.Pow(_treeData.reanalysisCostMultiplier, ReanalysisCount);
        }

        public SH_BuildNodeData GetNode(BuildBranch branch, int zeroBasedIndex)
        {
            if (_treeData == null) return null;
            var nodes = _treeData.GetBranchNodes(branch);
            if (zeroBasedIndex < 0 || zeroBasedIndex >= nodes.Length) return null;
            return nodes[zeroBasedIndex];
        }

        #endregion

        #region Internal

        private void ResetActiveBuild()
        {
            // Return PD spent on active nodes to the available pool.
            if (ActiveNodeCount > 0)
                _resources.ReturnDevelopmentPoints(ActiveNodeCount);

            ActiveNodeCount = 0;
            RestoreBaseStats();
        }

        private void ApplyBuildModifiers()
        {
            if (_baseStats == null) return;

            float str = _baseStrength;
            float def = _baseDefense;
            float agi = _baseAgility;

            var nodes = _treeData.GetBranchNodes(ActiveBranch);
            for (int i = 0; i < ActiveNodeCount && i < nodes.Length; i++)
            {
                if (nodes[i] == null) continue;
                str += _baseStrength * nodes[i].strengthModifier;
                def += _baseDefense * nodes[i].defenseModifier;
                agi += _baseAgility * nodes[i].agilityModifier;
            }

            _baseStats.Strength = str;
            _baseStats.Defense = def;
            _baseStats.Agility = agi;
        }

        private void RestoreBaseStats()
        {
            if (_baseStats == null) return;
            _baseStats.Strength = _baseStrength;
            _baseStats.Defense = _baseDefense;
            _baseStats.Agility = _baseAgility;
            _baseStats.PostureMax = _basePostureMax;
        }

        private float AccumulateModifier(Func<SH_BuildNodeData, float> selector)
        {
            var nodes = _treeData.GetBranchNodes(ActiveBranch);
            float total = 0f;
            for (int i = 0; i < ActiveNodeCount && i < nodes.Length; i++)
            {
                if (nodes[i] != null)
                    total += selector(nodes[i]);
            }
            return total;
        }

        #endregion

        #region Debug

        [ContextMenu("Debug — Activate Next Attack Node")]
        private void Debug_AttackNode()
        {
            if (Application.isPlaying) TryActivateNextNode(BuildBranch.Attack);
        }

        [ContextMenu("Debug — Deactivate Build")]
        private void Debug_Deactivate()
        {
            if (Application.isPlaying) DeactivateBuild();
        }

        #endregion
    }
}