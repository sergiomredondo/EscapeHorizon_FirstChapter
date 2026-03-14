using UnityEngine;
using System;
using Core;
using Game.Economy;
using Game.Economy.Data;

namespace Game.Interaction
{
    /// <summary>
    /// Represents a Pila de Chatarra (Scrap Pile) — a secondary resource container
    /// scattered throughout the sector (GDD §5.2.1).
    ///
    /// Interaction: Hold (GDD §5.2.1 — "Mantener Presionado para destrucción y loot").
    /// Reward: Scrap (SC) delivered to SH_ResourceSystem on hold completion.
    ///
    /// Scrap Piles are the primary source of Scrap outside of enemy destruction.
    /// They do not yield Identity Cores. Their interaction enforces the same
    /// exposure-to-reward tradeoff as Captive Cores, but at lower stakes.
    ///
    /// Responsibility boundaries:
    ///   - OWNS: Scrap delivery on interaction completion.
    ///   - OWNS: Scatter variance (base amount ± variance).
    ///   - DOES NOT OWN: Hold timer (SH_InteractionController).
    ///   - DOES NOT OWN: Resource state (SH_ResourceSystem).
    /// </summary>
    public class SH_ScrapPile : SH_InteractableObject
    {
        #region Configuration

        [Header("Scrap Pile — Rewards")]

        [Tooltip("Base Scrap (SC) awarded when this pile is looted.")]
        [Min(0f)]
        [SerializeField] private float _scrapAmount = 30f;

        [Tooltip("Variance range applied to scrapAmount at delivery time. " +
                 "Final drop = scrapAmount ± scrapVariance. " +
                 "Set to 0 for deterministic drops.")]
        [Min(0f)]
        [SerializeField] private float _scrapVariance = 8f;

        [Header("Scrap Pile — Visual Feedback")]

        [Tooltip("Optional: Renderer to tint when focused.")]
        [SerializeField] private Renderer _renderer;

        [Tooltip("Highlight color applied when the player is in interaction range.")]
        [SerializeField] private Color _focusColor = new Color(1f, 0.85f, 0.2f, 1f);

        [Tooltip("Base color to restore when focus is lost.")]
        [SerializeField] private Color _baseColor = Color.white;

        #endregion

        #region Events

        /// <summary>
        /// Fired when this pile is successfully looted.
        /// Parameters: (string persistentID, float scrapDelivered).
        /// Consumed by: Narrative system, SH_Debugger telemetry.
        /// </summary>
        public event Action<string, float> OnLooted;

        #endregion

        #region SH_InteractableObject Overrides

        protected override void Awake()
        {
            base.Awake();

            // Scrap Piles always use Hold interaction (GDD §5.2.1).
            interactionType = InteractionType.Hold;
        }

        /// <summary>
        /// Resolves the loot interaction.
        /// Called by SH_InteractionController when the hold timer completes.
        /// Rolls Scrap with variance and delivers to the resource system.
        /// </summary>
        public override void Interact(SH_PlayerContext context)
        {
            if (!_isAvailable)
            {
                Debug.LogWarning($"[SH_ScrapPile] Interact called on already-consumed " +
                                 $"pile '{persistentID}'. Ignoring.");
                return;
            }

            if (context == null)
            {
                Debug.LogError($"[SH_ScrapPile] Interact called with null context on " +
                               $"'{persistentID}'. Cannot deliver rewards.");
                return;
            }

            float variance = _scrapVariance > 0f
                ? UnityEngine.Random.Range(-_scrapVariance, _scrapVariance)
                : 0f;

            float finalScrap = Mathf.Max(0f, _scrapAmount + variance);

            if (context.Resources != null)
            {
                context.Resources.AddResource(ResourceType.Scrap, finalScrap);
            }
            else
            {
                Debug.LogWarning($"[SH_ScrapPile] '{persistentID}': SH_ResourceSystem is null. " +
                                 $"Scrap not delivered.");
            }

            MarkConsumed();
            OnLooted?.Invoke(persistentID, finalScrap);

            //Debug.Log($"[SH_ScrapPile] '{persistentID}' looted: {finalScrap:F1} SC delivered.");
        }

        #endregion

        #region Focus Visual Feedback

        protected override void OnFocusEnterInternal()
        {
            if (_renderer == null) return;
            _renderer.material.color = _focusColor;
        }

        protected override void OnFocusExitInternal()
        {
            if (_renderer == null) return;
            _renderer.material.color = _baseColor;
        }

        protected override void OnInterruptedInternal()
        {
            Debug.Log($"[SH_ScrapPile] '{persistentID}' hold interrupted. Loot reset.");
        }

        protected override void OnDestroyVisualOnLoad()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            foreach (var r in renderers) r.enabled = false;
        }

        #endregion

        #region Editor Validation

        private void OnValidate()
        {
            _scrapAmount   = Mathf.Max(0f, _scrapAmount);
            _scrapVariance = Mathf.Max(0f, _scrapVariance);
        }

        #endregion
    }
}
