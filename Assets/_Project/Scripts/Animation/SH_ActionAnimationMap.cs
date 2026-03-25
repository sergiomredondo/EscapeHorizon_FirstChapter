using Actions.Data;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Animation
{
    /// <summary>
    /// Presentation-layer asset that maps SH_ActionData contracts to AnimationClips.
    ///
    /// Responsibility boundary:
    ///   OWNS: The association between a gameplay action contract and its visual clip.
    ///   DOES NOT OWN: Timing, damage, physics, or any gameplay logic.
    ///                 Those remain exclusively in SH_ActionData.
    ///
    /// One asset per controllable entity archetype:
    ///   - One for Bear (player).
    ///   - One per enemy archetype (Assailant, Tank, Flanker).
    ///
    /// Usage:
    ///   Assign to SH_PlayerStateMachine (player) or SH_EnemyController (enemies)
    ///   via the Inspector. SH_AnimatorBridge reads this map at action dispatch time
    ///   to resolve the clip to override into the Action layer slot.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ActionAnimationMap",
        menuName = "ScapeHorizon/Animation/ActionAnimationMap",
        order = 150)]
    public class SH_ActionAnimationMap : ScriptableObject
    {
        #region Data Structure

        /// <summary>
        /// Single entry pairing a gameplay action contract with its animation clip.
        /// </summary>
        [System.Serializable]
        public class ActionAnimationEntry
        {
            [Tooltip("Gameplay action contract (SH_ActionData asset). " +
                     "This is the key used to look up the clip at runtime.")]
            public SH_ActionData actionData;

            [Tooltip("Animation clip to play when this action is dispatched. " +
                     "Duration does not need to match SH_ActionData.TotalDuration — " +
                     "SH_AnimatorBridge normalizes playback speed automatically.")]
            public List<AnimationClip> clip;
        }

        #endregion

        #region Serialized Fields

        [Header("Action — Clip Mappings")]
        [Tooltip("List of action-to-clip mappings for this entity archetype. " +
                 "Each SH_ActionData asset should appear at most once. " +
                 "Actions with no entry here play the fallback clip if one is assigned.")]
        [SerializeField] private List<ActionAnimationEntry> _entries = new();

        [Header("Fallback")]
        [Tooltip("Clip played when an action has no entry in this map. " +
                 "Assign any generic attack clip to prevent the Action layer " +
                 "from freezing on an unregistered action during prototyping.")]
        [SerializeField] private AnimationClip _fallbackClip;

        #endregion

        #region Runtime Cache

        /// <summary>
        /// Dictionary built from _entries on first access.
        /// Provides O(1) lookup at action dispatch time instead of O(n) list scan.
        /// </summary>
        private Dictionary<SH_ActionData, List<AnimationClip>> _cache;

        private Dictionary<SH_ActionData, int> _lastIndex = new();

        #endregion

        #region Public API

        /// <summary>
        /// Returns the AnimationClip mapped to the given SH_ActionData.
        /// Falls back to _fallbackClip if no entry exists.
        /// Returns null if neither a mapped entry nor a fallback is configured,
        /// in which case SH_AnimatorBridge will skip the override for this action.
        /// </summary>
        /// <param name="actionData">
        /// The action contract whose clip is being requested.
        /// </param>
        public AnimationClip GetClip(SH_ActionData actionData)
        {
            if (actionData == null) return _fallbackClip;

            var entry = _entries.FirstOrDefault(e => e.actionData == actionData);
            if (entry == null || entry.clip == null || entry.clip.Count == 0)
                return _fallbackClip;
            int newIndex;

            BuildCacheIfNeeded();

            do
            {
                newIndex = Random.Range(0, entry.clip.Count);
            }
            while (_lastIndex.ContainsKey(actionData) && newIndex == _lastIndex[actionData] && entry.clip.Count > 1);

            _lastIndex[actionData] = newIndex;

            if (_cache.TryGetValue(actionData, out List<AnimationClip> clip))
                return entry.clip[newIndex];

            return _fallbackClip;
        }

        /// <summary>
        /// Returns true if this map contains a direct entry for the given action.
        /// Returns false if only the fallback would be used.
        /// Useful for debug logging and editor tooling.
        /// </summary>
        public bool HasDirectEntry(SH_ActionData actionData)
        {
            if (actionData == null) return false;
            BuildCacheIfNeeded();
            return _cache.ContainsKey(actionData);
        }

        #endregion

        #region Cache Management
        /// <summary>
        /// Builds the runtime cache dictionary from the serialized list if it hasn't been built yet.
        /// Performs validation checks for null references and duplicate keys, logging warnings as needed.
        /// </summary>
        private void BuildCacheIfNeeded()
        {
            if (_cache != null) return;

            _cache = new Dictionary<SH_ActionData, List<AnimationClip>>(_entries.Count);

            foreach (var entry in _entries)
            {
                if (entry.actionData == null)
                {
                    Debug.LogWarning($"[SH_ActionAnimationMap] '{name}': " +
                                     $"An entry has a null SH_ActionData reference. " +
                                     $"The entry will be skipped.");
                    continue;
                }

                if (_cache.ContainsKey(entry.actionData))
                {
                    Debug.LogWarning($"[SH_ActionAnimationMap] '{name}': " +
                                     $"Duplicate entry for action '{entry.actionData.name}'. " +
                                     $"Only the first entry will be used.");
                    continue;
                }

                _cache[entry.actionData] = entry.clip;
            }
        }

        /// <summary>
        /// Forces the runtime cache to rebuild on next access.
        /// Called automatically by OnValidate so Inspector edits take effect
        /// without restarting Play mode.
        /// </summary>
        private void InvalidateCache() => _cache = null;

        #endregion

        #region Editor Validation

        private void OnValidate()
        {
            InvalidateCache();
        }

        #endregion
    }
}