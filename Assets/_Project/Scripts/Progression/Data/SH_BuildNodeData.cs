using UnityEngine;

namespace Game.Progression.Data
{
    public enum BuildBranch { Attack, Defense, Agility }

    [CreateAssetMenu(
        fileName = "BuildNode",
        menuName = "ScapeHorizon/Progression/BuildNode",
        order = 400)]
    public class SH_BuildNodeData : ScriptableObject
    {
        [Header("Identity")]
        public string nodeName = "Recuerdo";
        public BuildBranch branch = BuildBranch.Attack;
        [Range(1, 5)]
        public int nodeIndex = 1;

        [Header("Costs")]
        [Tooltip("Always 1 per node per GDD §5.4.2.")]
        public int pdCost = 1;
        [Min(0f)]
        public float scrapCost = 50f;

        [Header("Stat Modifiers (additive fractions, e.g. 0.05 = +5%)")]
        public float strengthModifier = 0f;
        public float defenseModifier = 0f;
        public float agilityModifier = 0f;

        [Header("Special Effects")]
        [Tooltip("Multiplier added to posture damage dealt. 0 = no effect.")]
        public float postureDamageBonus = 0f;

        [Tooltip("Multiplier added to critical hit damage. 0 = no effect.")]
        public float criticalDamageBonus = 0f;

        [Tooltip("Fraction of dash energy cost removed. 0.2 = 20% cheaper.")]
        public float dashEnergyCostReduction = 0f;

        [Tooltip("Flat damage reduction applied before % mitigation.")]
        public float flatDamageReduction = 0f;

        [Tooltip("Probability (0-1) to ignore the first posture hit per stagger cycle.")]
        public float postureIgnoreChance = 0f;

        [Tooltip("Probability (0-1) to negate damage on a perfect dash.")]
        public float perfectDashNegateChance = 0f;

        [Header("Narrative")]
        [TextArea(3, 6)]
        public string description = "";

        [TextArea(3, 8)]
        [Tooltip("Short narrative memory shown in the build menu when this node is activated.")]
        public string captiveMemoryText = "";

        private void OnValidate()
        {
            pdCost = Mathf.Max(1, pdCost);
            scrapCost = Mathf.Max(0f, scrapCost);
            nodeIndex = Mathf.Clamp(nodeIndex, 1, 5);
        }
    }
}