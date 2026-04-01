using System;
using UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI
{
    /// <summary>
    /// Subscribes to SH_UIStateModel events and translates state changes
    /// into visual updates on the UI Toolkit document defined in HUD.uxml.
    ///
    /// Operates exclusively on the presentation layer: no gameplay logic,
    /// no resource queries, no direct access to any gameplay MonoBehaviour.
    /// All data arrives through the model injected by SH_UIBridge.
    ///
    /// Lifecycle contract with SH_UIBridge:
    ///   SH_UIBridge.Awake() calls InjectModel() before this component's
    ///   OnEnable() fires, so the model is always available when subscriptions
    ///   are set up. If InjectModel() is called after OnEnable() (e.g. due to
    ///   an unusual scene load order), the guard in InjectModel() re-subscribes
    ///   immediately so no events are missed.
    ///
    /// UI Toolkit element names expected in HUD.uxml:
    ///   "hp-bar"          — ProgressBar  : Mecha durability fill.
    ///   "energy-bar"      — ProgressBar  : Energy pool fill.
    ///   "scrap-label"     — Label        : Scrap (SC) numeric counter.
    ///   "ic-label"        — Label        : Identity Core (IC) numeric counter.
    ///   "surge-indicator" — VisualElement: Surge state color indicator.
    ///
    /// CSS classes managed at runtime (defined in HUD.uss):
    ///   "surge-active"    — Applied to surge-indicator when Surge is active.
    ///   "surge-cooldown"  — Applied to surge-indicator during post-Surge cooldown.
    ///
    /// Responsibility boundaries:
    ///   OWNS: UI element queries, subscription lifecycle, visual updates.
    ///   DOES NOT OWN: State values or change detection (SH_UIStateModel).
    ///   DOES NOT OWN: Gameplay event subscriptions (SH_UIBridge).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public class SH_HUDController : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        #region CSS Class Name Constants

        // Keeping class names as constants avoids allocation from repeated
        // string literals and makes refactoring safe — change once, applies everywhere.
        private const string CssSurgeActive = "surge-active";
        private const string CssSurgeCooldown = "surge-cooldown";

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region UXML Element Name Constants

        private const string ElementHPBar = "hp-bar";
        private const string ElementEnergyBar = "energy-bar";
        private const string ElementScrapLabel = "scrap-label";
        private const string ElementICLabel = "ic-label";
        private const string ElementSurgeIndicator = "surge-indicator";

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Runtime References

        /// <summary>
        /// The UIDocument component on this GameObject.
        /// Queried in Awake() rather than serialized to avoid mismatches
        /// between the assigned asset and the runtime component.
        /// </summary>
        private UIDocument _document;

        // ─── Cached visual elements ───────────────────────────────────────────
        // Queried once in CacheElements() to avoid per-update Q<T> calls,
        // which perform a tree walk each time they are called.

        private ProgressBar _hpBar;
        private ProgressBar _energyBar;
        private Label _scrapLabel;
        private Label _icLabel;
        private VisualElement _surgeIndicator;

        /// <summary>
        /// The state model injected by SH_UIBridge before OnEnable() fires.
        /// Null until InjectModel() is called.
        /// </summary>
        private SH_UIStateModel _model;

        /// <summary>
        /// True once the model has been injected and subscriptions are active.
        /// Guards all event handlers against firing before setup is complete.
        /// </summary>
        private bool _isInitialized;

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Unity Lifecycle

        private void Awake()
        {
            _document = GetComponent<UIDocument>();

            if (_document == null)
                Debug.LogError("[SH_HUDController] UIDocument component not found. " +
                               "Add a UIDocument component to this GameObject and assign HUD.uxml.");
        }

        private void OnEnable()
        {
            // If the model was already injected before this component enabled,
            // subscribe immediately. Otherwise, InjectModel() will subscribe
            // when it is called from SH_UIBridge.Awake().
            if (_model != null)
                Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Public API — Called by SH_UIBridge

        /// <summary>
        /// Injects the shared UIStateModel and activates the subscription.
        /// Called by SH_UIBridge.Awake() before this component's OnEnable().
        ///
        /// If this method is called after OnEnable() (unusual load order),
        /// it sets up subscriptions immediately so no events are dropped.
        /// </summary>
        /// <param name="model">
        /// The model produced by SH_UIBridge. Must not be null.
        /// </param>
        public void InjectModel(SH_UIStateModel model)
        {
            if (model == null)
            {
                Debug.LogError("[SH_HUDController] InjectModel: model is null. " +
                               "SH_UIBridge must create the model before calling InjectModel().");
                return;
            }

            // Unsubscribe from a previous model if this is a hot-reload scenario.
            if (_model != null)
                Unsubscribe();

            _model = model;

            // If already enabled, subscribe now. If not yet enabled, OnEnable()
            // will subscribe when it fires after Awake() completes.
            if (isActiveAndEnabled)
                Subscribe();
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Subscription Lifecycle

        private void Subscribe()
        {
            if (_model == null || _isInitialized) return;

            // Cache element references once, before registering any callbacks
            // that would try to use them.
            if (!CacheElements()) return;

            _model.OnHPChanged += OnHPChanged;
            _model.OnEnergyChanged += OnEnergyChanged;
            _model.OnScrapChanged += OnScrapChanged;
            _model.OnIdentityCoresChanged += OnIdentityCoresChanged;
            _model.OnSurgeStateChanged += OnSurgeStateChanged;

            // Sync the HUD to whatever values the model already holds.
            // Without this, all bars show their default UXML values until
            // the next gameplay event fires.
            PushCurrentModelState();

            _isInitialized = true;
        }

        private void Unsubscribe()
        {
            if (_model == null || !_isInitialized) return;

            _model.OnHPChanged -= OnHPChanged;
            _model.OnEnergyChanged -= OnEnergyChanged;
            _model.OnScrapChanged -= OnScrapChanged;
            _model.OnIdentityCoresChanged -= OnIdentityCoresChanged;
            _model.OnSurgeStateChanged -= OnSurgeStateChanged;

            _isInitialized = false;
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Element Caching

        /// <summary>
        /// Queries all required UXML elements by name and stores references.
        /// Returns false if any critical element is missing, which prevents
        /// subscriptions from being set up with broken references.
        /// </summary>
        private bool CacheElements()
        {
            if (_document?.rootVisualElement == null)
            {
                Debug.LogError("[SH_HUDController] UIDocument root is null. " +
                               "Ensure HUD.uxml is assigned to the UIDocument component " +
                               "and the document has fully loaded before Subscribe() runs.");
                return false;
            }

            VisualElement root = _document.rootVisualElement;

            _hpBar = root.Q<ProgressBar>(ElementHPBar);
            _energyBar = root.Q<ProgressBar>(ElementEnergyBar);
            _scrapLabel = root.Q<Label>(ElementScrapLabel);
            _icLabel = root.Q<Label>(ElementICLabel);
            _surgeIndicator = root.Q<VisualElement>(ElementSurgeIndicator);

            bool allFound = true;

            if (_hpBar == null)
            {
                Debug.LogError($"[SH_HUDController] Element '{ElementHPBar}' not found in HUD.uxml.");
                allFound = false;
            }
            if (_energyBar == null)
            {
                Debug.LogError($"[SH_HUDController] Element '{ElementEnergyBar}' not found in HUD.uxml.");
                allFound = false;
            }
            if (_scrapLabel == null)
            {
                Debug.LogError($"[SH_HUDController] Element '{ElementScrapLabel}' not found in HUD.uxml.");
                allFound = false;
            }
            if (_icLabel == null)
            {
                Debug.LogError($"[SH_HUDController] Element '{ElementICLabel}' not found in HUD.uxml.");
                allFound = false;
            }
            if (_surgeIndicator == null)
            {
                Debug.LogError($"[SH_HUDController] Element '{ElementSurgeIndicator}' not found in HUD.uxml.");
                allFound = false;
            }

            return allFound;
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Event Handlers

        /// <summary>
        /// Updates the HP progress bar fill.
        /// ProgressBar.value in UI Toolkit represents a normalized fraction [0,1]
        /// when lowValue = 0 and highValue = 1, which is the configuration in HUD.uxml.
        /// </summary>
        private void OnHPChanged(float currentHP, float maxHP)
        {
            if (_hpBar == null) return;
            _hpBar.value = maxHP > 0f ? currentHP / maxHP : 0f;
        }

        /// <summary>
        /// Updates the Energy progress bar fill using the same normalized pattern.
        /// The bar color shift from Cían (normal) to Magenta (surge) is handled
        /// by OnSurgeStateChanged via CSS class toggling, not here.
        /// </summary>
        private void OnEnergyChanged(float currentEnergy, float maxEnergy)
        {
            if (_energyBar == null) return;
            _energyBar.value = maxEnergy > 0f ? currentEnergy / maxEnergy : 0f;
        }

        /// <summary>
        /// Updates the Scrap counter label.
        /// Format: "SC: 0" — prefix communicates resource type without an icon,
        /// which is sufficient for this prototype step. The icon-based layout
        /// is part of Step 2 (full HUD visual pass).
        /// </summary>
        private void OnScrapChanged(float currentScrap)
        {
            if (_scrapLabel == null) return;
            _scrapLabel.text = $"SC: {Mathf.FloorToInt(currentScrap)}";
        }

        /// <summary>
        /// Updates the Identity Core counter label.
        /// Format: "IC: 0" — same rationale as OnScrapChanged.
        /// </summary>
        private void OnIdentityCoresChanged(int currentCores)
        {
            if (_icLabel == null) return;
            _icLabel.text = $"IC: {currentCores}";
        }

        /// <summary>
        /// Applies or removes CSS classes on the surge indicator element
        /// to reflect the current Surge state.
        ///
        /// State matrix:
        ///   surgeActive = true,  inCooldown = false → class "surge-active"   (Magenta)
        ///   surgeActive = false, inCooldown = true  → class "surge-cooldown" (Orange)
        ///   surgeActive = false, inCooldown = false → no class               (Transparent)
        ///
        /// CSS classes are defined in HUD.uss. Toggling via EnableInClassList
        /// is the correct UI Toolkit pattern — it adds the class if the condition
        /// is true, removes it if false, in a single call with no redundant DOM writes.
        /// </summary>
        private void OnSurgeStateChanged(bool surgeActive, bool inCooldown)
        {
            if (_surgeIndicator == null) return;

            _surgeIndicator.EnableInClassList(CssSurgeActive, surgeActive);
            _surgeIndicator.EnableInClassList(CssSurgeCooldown, inCooldown && !surgeActive);
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Initial State Sync

        /// <summary>
        /// Reads the current values from the model and applies them to all
        /// elements immediately after subscribing.
        ///
        /// This is necessary because the model may already hold non-zero values
        /// (pushed by SH_UIBridge.PushInitialState()) before this controller
        /// subscribes. Without this sync, the HUD would display stale default
        /// values from UXML until the next gameplay event.
        /// </summary>
        private void PushCurrentModelState()
        {
            OnHPChanged(_model.CurrentHP, _model.MaxHP);
            OnEnergyChanged(_model.CurrentEnergy, _model.MaxEnergy);
            OnScrapChanged(_model.CurrentScrap);
            OnIdentityCoresChanged(_model.CurrentIdentityCores);
            OnSurgeStateChanged(_model.IsSurgeActive, _model.IsInSurgeCooldown);
        }

        #endregion
    }
}