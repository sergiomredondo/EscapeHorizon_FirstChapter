using UnityEngine;
using System;
using Core;
using Game.Economy;
using Game.Economy.Data;
using Game.Interaction.Data;

namespace Game.Interaction
{
    /// <summary>
    /// Represents a Captive Automaton — the primary ethical interaction object
    /// of Escape Horizon's core loop (GDD §5.2.1, §4.3).
    ///
    /// Implements the Destroy/Rescue binary decision that drives the economy:
    ///
    ///   RESCUE path  — player completes the hold interaction uninterrupted.
    ///                  Delivers Identity Cores (IC) for permanent progression.
    ///                  Delivers reduced Scrap as consolation.
    ///                  Calls SH_ResourceDropData.DeliverRescueRewards().
    ///
    ///   DESTROY path — player calls ForceDestroy() (future: via combat hit)
    ///                  OR the hold is interrupted and the player explicitly
    ///                  triggers destruction.
    ///                  Delivers Scrap immediately. No IC awarded.
    ///                  Calls SH_ResourceDropData.DeliverDestroyRewards().
    ///
    /// This class closes two of the three pending integration points from the
    /// Game.Economy implementation:
    ///   ✓ DeliverDestroyRewards() — called on ForceDestroy()
    ///   ✓ DeliverRescueRewards()  — called on hold completion
    ///
    /// The third point (RollEnergyEventOnEliteEncounter) belongs to 5.3 Combat.
    ///
    /// Responsibility boundaries:
    ///   - OWNS: Rescue/Destroy outcome resolution.
    ///   - OWNS: Drop data delivery via SH_ResourceDropData.
    ///   - OWNS: OnDefeated subscription to trigger ForceDestroy when
    ///           the Captive's own durability reaches zero (future 5.3).
    ///   - DOES NOT OWN: Hold timer (SH_InteractionController).
    ///   - DOES NOT OWN: Resource values (SH_ResourceDropData asset).
    ///   - DOES NOT OWN: Economic event modifiers (SH_EconomicEventManager).
    /// </summary>
    public class SH_CaptiveCore : SH_InteractableObject
    {
        #region Configuration

        [Header("Captive Core — Drop Configuration")]

        [Tooltip("Drop data asset defining IC, Scrap, and Energy rewards " +
                 "for both Rescue and Destroy paths. " +
                 "Create one asset per enemy archetype via " +
                 "ScapeHorizon/Economy/Resource Drop Data.")]
        [SerializeField] private SH_ResourceDropData _dropData;

        [Header("Captive Core — Visual Feedback (Assign in Inspector)")]

        [Tooltip("Optional: Renderer to tint when focused. " +
                 "Set to the primary mesh renderer of this Captive object.")]
        [SerializeField] private Renderer _renderer;

        [Tooltip("Highlight color applied when the player is in interaction range.")]
        [SerializeField] private Color _focusColor = new Color(0.2f, 1f, 0.4f, 1f);

        [Tooltip("Base color to restore when focus is lost.")]
        [SerializeField] private Color _baseColor = Color.white;

        [Header("Captive Core — Reveal Effects")]

        [Tooltip("Prefab instantiated once at reveal moment (flash pulse). " +
         "Configure Stop Action → Destroy on its Particle System or VFX Graph.")]
        [SerializeField] private GameObject _revealFlashPrefab;

        [Tooltip("Prefab instantiated when Vulnerable state begins and destroyed on resolution. " +
                 "Attach pulsing light, looping VFX and AudioSource with loop=true inside.")]
        [SerializeField] private GameObject _captivePulsePrefab;

        [Tooltip("AudioClip played once at the reveal moment via AudioSource.PlayClipAtPoint.")]
        [SerializeField] private AudioClip _revealSoundClip;

        [Tooltip("Fallback lifetime (seconds) for the flash prefab if it does not self-terminate.")]
        [Min(0.1f)]
        [SerializeField] private float _revealFlashAutoDestroy = 1f;

        [Tooltip("Layer name assigned to this GameObject when the core becomes interactable. " +
         "Must match the layer used in SH_InteractionSettings.interactableLayer.")]
        [SerializeField] private string _interactableLayerName = "Interactable";

        #endregion

        #region Runtime State

        /// <summary>
        /// Whether this Captive has been rescued (true) or destroyed (false).
        /// Only valid once _isAvailable is false.
        /// Used by the persistence system to record the outcome.
        /// </summary>
        private bool _wasRescued = false;
        private bool _isRevealed = false;
        private GameObject _activePulseInstance;

        #endregion

        #region Events

        /// <summary>
        /// Fired when the Rescue path is completed.
        /// Parameters: (string persistentID).
        /// Consumed by: Narrative system, SH_EconomicEventManager
        /// (future: NotifyRegionChange on sector clear).
        /// </summary>
        public event Action<string> OnRescued;

        /// <summary>
        /// Fired when the Destroy path is resolved.
        /// Parameters: (string persistentID).
        /// Consumed by: Narrative system, SH_Debugger telemetry.
        /// </summary>
        public event Action<string> OnDestroyed;

        #endregion

        #region SH_InteractableObject Overrides

        protected override void Awake()
        {
            base.Awake();

            interactionType = InteractionType.Hold;

            _isAvailable = false;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;

            // Own renderer is hidden by default — the pulse prefab provides the visual on reveal.
            if (_renderer != null)
                _renderer.enabled = false;

            if (_dropData == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_CaptiveCore] '{gameObject.name}' has no " +
                                 $"SH_ResourceDropData assigned. No rewards will be " +
                                 $"delivered on interaction. Assign a drop data asset.");
#endif
            }
        }

        public void ActivateCaptiveReveal()
        {
            if (_isRevealed) return;
            _isRevealed = true;

            // Enable the collider on this child GameObject (Interactable layer, set in prefab).
            // The enemy root CharacterController stays on Combat layer — attacks still register.
            _isAvailable = true;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = true;

            if (_revealFlashPrefab != null)
            {
                GameObject flash = Instantiate(
                    _revealFlashPrefab,
                    transform.position,
                    transform.rotation);
                Destroy(flash, _revealFlashAutoDestroy);
            }

            if (_captivePulsePrefab != null)
            {
                _activePulseInstance = Instantiate(
                    _captivePulsePrefab,
                    transform.position,
                    transform.rotation,
                    transform);
            }

            if (_revealSoundClip != null)
                AudioSource.PlayClipAtPoint(_revealSoundClip, transform.position);
        }

        public void ResetCaptiveState()
        {
            _isRevealed = false;
            _wasRescued = false;
            _isAvailable = false;

            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;

            if (_renderer != null)
                _renderer.enabled = false;

            if (_activePulseInstance != null)
            {
                Destroy(_activePulseInstance);
                _activePulseInstance = null;
            }
        }

        /// <summary>
        /// Resolves the RESCUE path.
        /// Called by SH_InteractionController when the hold timer completes
        /// without interruption (GDD §5.2.1: hold = commitment = ethical choice).
        /// Delivers IC and residual Scrap to the resource system.
        /// Marks the core as consumed and fires OnRescued.
        /// </summary>
        /// <param name="context">
        /// The player context. Provides access to SH_ResourceSystem.
        /// </param>
        public override void Interact(SH_PlayerContext context)
        {
            if (!_isAvailable)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_CaptiveCore] Interact called on already-consumed " +
                                 $"core '{persistentID}'. Ignoring.");
#endif
                return;
            }

            if (context == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_CaptiveCore] Interact called with null context on " +
                               $"'{persistentID}'. Cannot deliver rewards.");
#endif
                return;
            }

            if (_dropData != null && context.Resources != null)
            {
                _dropData.DeliverRescueRewards(context.Resources);
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_CaptiveCore] '{persistentID}': missing drop data " +
                                 $"or resource system. Rescue resolved with no rewards.");
#endif
            }

            _wasRescued = true;

            if (_activePulseInstance != null)
            {
                Destroy(_activePulseInstance);
                _activePulseInstance = null;
            }

            MarkConsumed();
            OnRescued?.Invoke(persistentID);
#if UNITY_EDITOR
            Debug.Log($"[SH_CaptiveCore] '{persistentID}' RESCUED. " +
                      $"IC and residual Scrap delivered.");
#endif
        }

        /// <summary>
        /// Resolves the DESTROY path.
        /// Called by the combat system (SH_AnimatorBridge.OnHitImpact → 5.3)
        /// when this Captive is struck with lethal force, or by designer tools.
        /// Delivers Scrap to the resource system. No IC awarded.
        /// Marks the core as consumed and fires OnDestroyed.
        /// </summary>
        /// <param name="context">
        /// The player context. Provides access to SH_ResourceSystem.
        /// </param>
        public void ForceDestroy(SH_PlayerContext context)
        {
            if (!_isAvailable)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_CaptiveCore] ForceDestroy called on already-consumed " +
                                 $"core '{persistentID}'. Ignoring.");
#endif
                return;
            }

            if (context == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_CaptiveCore] ForceDestroy called with null context on " +
                               $"'{persistentID}'. Cannot deliver rewards.");
#endif
                return;
            }

            if (_dropData != null && context.Resources != null)
            {
                _dropData.DeliverDestroyRewards(context.Resources);
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_CaptiveCore] '{persistentID}': missing drop data " +
                                 $"or resource system. Destroy resolved with no rewards.");
#endif
            }

            _wasRescued = false;

            if (_activePulseInstance != null)
            {
                Destroy(_activePulseInstance);
                _activePulseInstance = null;
            }

            MarkConsumed();
            OnDestroyed?.Invoke(persistentID);
#if UNITY_EDITOR
            Debug.Log($"[SH_CaptiveCore] '{persistentID}' DESTROYED. " +
                      $"Scrap delivered. IC forfeited.");
#endif
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
            // Reset focus color on interruption — the hold was not completed.
            if (_renderer != null)
                _renderer.material.color = _focusColor;

#if UNITY_EDITOR
            Debug.Log($"[SH_CaptiveCore] '{persistentID}' hold interrupted. " +
                      $"Rescue progress reset.");
#endif
        }

        protected override void OnDestroyVisualOnLoad()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            foreach (var r in renderers) r.enabled = false;

            if (_activePulseInstance != null)
            {
                Destroy(_activePulseInstance);
                _activePulseInstance = null;
            }
        }

        #endregion

        #region Persistence API

        /// <summary>
        /// Returns the outcome of the last interaction for persistence.
        /// Only meaningful when IsAvailable is false.
        /// True = was rescued. False = was destroyed.
        /// </summary>
        public bool WasRescued => _wasRescued;

        /// <summary>
        /// Restores state from save data.
        /// Extended to restore the rescue/destroy outcome flag.
        /// </summary>
        public void RestoreState(bool isAvailable, bool wasRescued)
        {
            base.RestoreState(isAvailable);
            _wasRescued = wasRescued;
        }

        #endregion

        #region Editor Debug

        [ContextMenu("Debug — Simulate Rescue")]
        private void Debug_SimulateRescue()
        {
            if (!Application.isPlaying)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[SH_CaptiveCore] Debug actions only available in Play Mode.");
#endif
                return;
            }
            // Context-less debug: fires the event without delivering rewards.
            _wasRescued = true;
            MarkConsumed();
            OnRescued?.Invoke(persistentID);
#if UNITY_EDITOR
            Debug.Log($"[SH_CaptiveCore] DEBUG: '{persistentID}' marked as rescued " +
                      $"(no rewards delivered — context not available in debug call).");
#endif
        }

        [ContextMenu("Debug — Simulate Destroy")]
        private void Debug_SimulateDestroy()
        {
            if (!Application.isPlaying)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[SH_CaptiveCore] Debug actions only available in Play Mode.");
#endif
                return;
            }
            _wasRescued = false;
            MarkConsumed();
            OnDestroyed?.Invoke(persistentID);
#if UNITY_EDITOR
            Debug.Log($"[SH_CaptiveCore] DEBUG: '{persistentID}' marked as destroyed " +
                      $"(no rewards delivered — context not available in debug call).");
#endif
        }

        #endregion
    }
}
