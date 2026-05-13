using UnityEngine;

namespace Game.Progression.Data
{
    [CreateAssetMenu(
        fileName = "BuildTreeData",
        menuName = "ScapeHorizon/Progression/BuildTreeData",
        order = 401)]
    public class SH_BuildTreeData : ScriptableObject
    {
        [Header("Attack Branch — 5 nodes in sequence")]
        public SH_BuildNodeData[] attackNodes = new SH_BuildNodeData[5];

        [Header("Defense Branch — 5 nodes in sequence")]
        public SH_BuildNodeData[] defenseNodes = new SH_BuildNodeData[5];

        [Header("Agility Branch — 5 nodes in sequence")]
        public SH_BuildNodeData[] agilityNodes = new SH_BuildNodeData[5];

        [Header("Reanalysis Cost")]
        [Tooltip("Base Scrap cost to switch the active branch.")]
        [Min(0f)]
        public float reanalysisCostBase = 200f;

        [Tooltip("Multiplier applied per reanalysis already performed this run.")]
        [Min(1f)]
        public float reanalysisCostMultiplier = 2f;

        public SH_BuildNodeData[] GetBranchNodes(BuildBranch branch) => branch switch
        {
            BuildBranch.Attack => attackNodes,
            BuildBranch.Defense => defenseNodes,
            BuildBranch.Agility => agilityNodes,
            _ => attackNodes
        };

        private void OnValidate()
        {
            if (attackNodes.Length != 5) System.Array.Resize(ref attackNodes, 5);
            if (defenseNodes.Length != 5) System.Array.Resize(ref defenseNodes, 5);
            if (agilityNodes.Length != 5) System.Array.Resize(ref agilityNodes, 5);
        }
    }
}