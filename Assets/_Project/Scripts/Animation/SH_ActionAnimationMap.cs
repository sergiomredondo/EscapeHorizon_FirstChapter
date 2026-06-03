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
        // The structure of this asset is organized into thematic regions for clarity and ease of use in the Inspector.
        #region Data Structure

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

        // This serialized list is the source of truth for the mappings and is designed for ease of use in the Inspector.
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

        // The runtime cache is designed for efficient lookups during gameplay, converting the serialized list into
        // a dictionary on demand.
        #region Runtime Cache

        private Dictionary<SH_ActionData, List<AnimationClip>> _cache;
        private Dictionary<SH_ActionData, int> _lastIndex = new();

        #endregion

        #region Public API

        /// <summary>
        /// Returns all clips mapped to this action as an array.
        /// Index 0 = Bear's clip, Index 1 = Luisa's clip, etc.
        /// Matches the Animator array order in SH_AnimatorBridge.
        /// Falls back to [_fallbackClip] if no entry exists.
        /// </summary>
        /// <param name="actionData">The action contract to look up.</param>
        public AnimationClip[] GetClips(SH_ActionData actionData)
        {
            if (actionData == null)
                return _fallbackClip != null ? new[] { _fallbackClip } : null;

            var entry = _entries.FirstOrDefault(e => e.actionData == actionData);
            if (entry == null || entry.clip == null || entry.clip.Count == 0)
                return _fallbackClip != null ? new[] { _fallbackClip } : null;

            return entry.clip.ToArray();
        }

        /// <summary>
        /// Returns true if this map contains a direct entry for the given action.
        /// Returns false if only the fallback would be used.
        /// Useful for debug logging and editor tooling.
        /// </summary>
        /// <param name="actionData">The action contract to check for.</param>
        /// <returns>True if a direct entry exists, false if only fallback applies.</returns>
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
        /// <remarks>
        /// This method is designed to be idempotent and efficient, only building the cache once and reusing
        /// it for subsequent lookups. The validation checks help catch common data entry errors in the Inspector,
        /// improving robustness without throwing exceptions that could disrupt gameplay. If more complex cache
        /// management is needed in the future (e.g., partial updates, event-driven invalidation), this is the
        /// central place to implement it while keeping the rest of the codebase decoupled from cache management details.
        /// </remarks>
        private void BuildCacheIfNeeded()
        {
            if (_cache != null) return;

            _cache = new Dictionary<SH_ActionData, List<AnimationClip>>(_entries.Count);

            foreach (var entry in _entries)
            {
                if (entry.actionData == null)
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"[SH_ActionAnimationMap] '{name}': " +
                                     $"An entry has a null SH_ActionData reference. " +
                                     $"The entry will be skipped.");
#endif
                    continue;
                }

                if (_cache.ContainsKey(entry.actionData))
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"[SH_ActionAnimationMap] '{name}': " +
                                     $"Duplicate entry for action '{entry.actionData.name}'. " +
                                     $"Only the first entry will be used.");
#endif
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
        /// <remarks>
        /// This method is intentionally simple to avoid unintended side effects.
        /// If more complex cache invalidation logic is needed in the future (e.g., partial invalidation,
        /// event-driven updates), this is the central place to implement it while keeping the rest of the
        /// codebase decoupled from cache management details.
        /// </remarks>
        private void InvalidateCache() => _cache = null;

        #endregion

        #region Editor Validation
        
        /// <summary>
        /// Called by Unity when the asset is edited in the Inspector.
        /// Invalidates the runtime cache so that changes to the mappings take effect immediately without needing
        /// to restart Play mode. This ensures a smooth iteration experience for designers configuring the action-to-clip mappings.
        /// </summary>
        /// <remarks>
        /// While OnValidate provides a convenient way to automatically invalidate the cache when changes are made in the Inspector,
        /// it's important to note that it is only called in the Editor and does not run in builds. This means that any logic placed
        /// in OnValidate will not have any performance impact on the final game, making it a safe place to handle editor-specific
        /// concerns like cache invalidation and data validation without worrying about runtime overhead.
        /// </remarks>
        private void OnValidate()
        {
            InvalidateCache();
        }

        #endregion
    }
}