using System;
using Game.Progression.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI
{
    /// <summary>
    /// Subscribes to SH_UIStateModel events and translates state changes
    /// into visual updates on the UI Toolkit document defined in HUD.uxml.
    ///
    /// Extended with the Analysis Tree build menu (GDD §5.4.2):
    ///   The overlay is shown/hidden via display flex/none driven by
    ///   OnBuildMenuOpenChanged. Node and reanalysis button presses fire
    ///   events consumed by SH_UIBridge, which owns the gameplay transaction.
    ///   Time.timeScale is managed by SH_UIBridge, not here.
    ///
    /// Responsibility boundaries:
    ///   OWNS: UI element queries, subscription lifecycle, visual updates,
    ///         button click event dispatch.
    ///   DOES NOT OWN: State values or change detection (SH_UIStateModel).
    ///   DOES NOT OWN: Gameplay event subscriptions (SH_UIBridge).
    ///   DOES NOT OWN: Time.timeScale, BuildSystem transactions.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public class SH_HUDController : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        #region CSS Class Name Constants

        private const string CssSurgeActive = "surge-active";
        private const string CssSurgeCooldown = "surge-cooldown";
        private const string CssNodeActive = "build-node-btn--active";
        private const string CssNodeNext = "build-node-btn--next";
        private const string CssNodeUnavailable = "build-node-btn--unavailable";

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region UXML Element Name Constants — HUD

        private const string ElementHPBar = "hp-bar";
        private const string ElementEnergyBar = "energy-bar";
        private const string ElementScrapLabel = "scrap-label";
        private const string ElementICLabel = "ic-label";
        private const string ElementSurgeIndicator = "surge-indicator";
        private const string ElementInteractionContainer = "interaction-container";
        private const string ElementInteractionLabel = "interaction-label";
        private const string ElementInteractionProgress = "interaction-progress";
        private const string ElementBuildReadonlyNotice = "build-readonly-notice";
        private const string ElementBuildPurgeSection = "build-purge-section";
        private const string ElementBuildPurgeYield = "build-purge-yield";
        private const string ElementBuildPurgeBtn = "build-purge-btn";

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region UXML Element Name Constants — Build Menu

        private const string ElementBuildOverlay = "build-menu-overlay";
        private const string ElementBuildLabelHP = "build-label-hp";
        private const string ElementBuildLabelEnergy = "build-label-energy";
        private const string ElementBuildLabelScrap = "build-label-scrap";
        private const string ElementBuildLabelIC = "build-label-ic";
        private const string ElementBuildLabelPD = "build-label-pd";
        private const string ElementBuildReanalysisCost = "build-reanalysis-cost";
        private const string ElementBuildNarrativePanel = "build-narrative-panel";
        private const string ElementBuildNarrativeText = "build-narrative-text";
        private const string ElementBuildCloseBtn = "build-close-btn";

        // Node buttons: "node-{branch}-{index}" e.g. "node-attack-0"
        private static readonly string[] BranchNames = { "attack", "defense", "agility" };

        // Reanalysis buttons: "reanalysis-{branch}"
        // Format: reanalysis-attack, reanalysis-defense, reanalysis-agility

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Public Events — Build Menu Actions (consumed by SH_UIBridge)

        /// <summary>
        /// Fired when the player clicks a node button.
        /// Parameters: (BuildBranch branch, int zeroBasedIndex).
        /// </summary>
        public event Action<BuildBranch, int> OnBuildNodePressed;

        /// <summary>
        /// Fired when the player clicks a reanalysis button.
        /// Parameter: (BuildBranch targetBranch).
        /// </summary>
        public event Action<BuildBranch> OnBuildReanalysisPressed;

        /// <summary>
        /// Fired when the player clicks the close button or Tab is detected
        /// by SH_UIBridge while the menu is open.
        /// </summary>
        public event Action OnBuildMenuClosePressed;

        /// <summary>
        /// Fired when the player clicks the purge button.
        /// Consumed by SH_UIBridge which executes the transaction.
        /// </summary>
        public event Action OnPurgePressed;

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Runtime References — HUD

        private UIDocument _document;
        private ProgressBar _hpBar;
        private ProgressBar _energyBar;
        private Label _scrapLabel;
        private Label _icLabel;
        private VisualElement _surgeIndicator;
        private VisualElement _interactionContainer;
        private Label _interactionLabel;
        private ProgressBar _interactionProgress;
        private bool _isInteractionActive;

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Runtime References — Build Menu

        private VisualElement _buildOverlay;
        private Label _buildLabelHP;
        private Label _buildLabelEnergy;
        private Label _buildLabelScrap;
        private Label _buildLabelIC;
        private Label _buildLabelPD;
        private Label _buildReanalysisCostLabel;
        private VisualElement _buildNarrativePanel;
        private Label _buildNarrativeText;
        private Label _buildReadonlyNotice;
        private VisualElement _buildPurgeSection;
        private Label _buildPurgeYield;

        // [branch 0-2, node 0-4]
        private readonly Button[,] _nodeButtons = new Button[3, 5];
        private readonly Button[] _reanalysisButtons = new Button[3];

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Model & Init State

        private SH_UIStateModel _model;
        private bool _isInitialized;

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Unity Lifecycle

        private void Awake()
        {
            _document = GetComponent<UIDocument>();

            if (_document == null)
            {
#if UNITY_EDITOR
                Debug.LogError("[SH_HUDController] UIDocument component not found. " +
                               "Add a UIDocument component to this GameObject and assign HUD.uxml.");
#endif
            }
        }

        private void OnEnable()
        {
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

        public void InjectModel(SH_UIStateModel model)
        {
            if (model == null)
            {
#if UNITY_EDITOR
                Debug.LogError("[SH_HUDController] InjectModel: model is null.");
#endif
                return;
            }

            if (_model != null)
                Unsubscribe();

            _model = model;

            if (isActiveAndEnabled)
                Subscribe();
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Subscription Lifecycle

        private void Subscribe()
        {
            if (_model == null || _isInitialized) return;
            if (!CacheElements()) return;

            // HUD events.
            _model.OnHPChanged += OnHPChanged;
            _model.OnEnergyChanged += OnEnergyChanged;
            _model.OnScrapChanged += OnScrapChanged;
            _model.OnIdentityCoresChanged += OnIdentityCoresChanged;
            _model.OnSurgeStateChanged += OnSurgeStateChanged;
            _model.OnInteractionFocusChanged += OnInteractionFocusChanged;
            _model.OnInteractionProgressChanged += OnInteractionProgressChanged;

            // Build menu events.
            _model.OnBuildMenuOpenChanged += OnBuildMenuOpenChanged;
            _model.OnBuildTreeRefreshed += OnBuildTreeRefreshed;
            _model.OnBuildNarrativeChanged += OnBuildNarrativeChanged;
            _model.OnPurgeDataChanged += OnPurgeDataChanged;

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
            _model.OnInteractionFocusChanged -= OnInteractionFocusChanged;
            _model.OnInteractionProgressChanged -= OnInteractionProgressChanged;

            _model.OnBuildMenuOpenChanged -= OnBuildMenuOpenChanged;
            _model.OnBuildTreeRefreshed -= OnBuildTreeRefreshed;
            _model.OnBuildNarrativeChanged -= OnBuildNarrativeChanged;

            _isInitialized = false;
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Element Caching

        private bool CacheElements()
        {
            if (_document?.rootVisualElement == null)
            {
#if UNITY_EDITOR
                Debug.LogError("[SH_HUDController] UIDocument root is null.");
#endif
                return false;
            }

            VisualElement root = _document.rootVisualElement;

            // ── HUD elements ──────────────────────────────────────────────────
            _hpBar = root.Q<ProgressBar>(ElementHPBar);
            _energyBar = root.Q<ProgressBar>(ElementEnergyBar);
            _scrapLabel = root.Q<Label>(ElementScrapLabel);
            _icLabel = root.Q<Label>(ElementICLabel);
            _surgeIndicator = root.Q<VisualElement>(ElementSurgeIndicator);
            _interactionContainer = root.Q<VisualElement>(ElementInteractionContainer);
            _interactionLabel = root.Q<Label>(ElementInteractionLabel);
            _interactionProgress = root.Q<ProgressBar>(ElementInteractionProgress);

            bool allFound = true;
            if (_hpBar == null) 
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_HUDController] '{ElementHPBar}' not found.");
#endif
                allFound = false; 
            }
            if (_energyBar == null) 
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_HUDController] '{ElementEnergyBar}' not found.");
#endif
                allFound = false;
            }
            if (_scrapLabel == null) 
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_HUDController] '{ElementScrapLabel}' not found.");
#endif
                allFound = false;
            }
            if (_icLabel == null) 
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_HUDController] '{ElementICLabel}' not found.");
#endif
                allFound = false;
            }
            if (_surgeIndicator == null) 
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_HUDController] '{ElementSurgeIndicator}' not found.");
#endif
                allFound = false;
            }

            // ── Build menu elements ───────────────────────────────────────────
            _buildOverlay = root.Q<VisualElement>(ElementBuildOverlay);
            _buildLabelHP = root.Q<Label>(ElementBuildLabelHP);
            _buildLabelEnergy = root.Q<Label>(ElementBuildLabelEnergy);
            _buildLabelScrap = root.Q<Label>(ElementBuildLabelScrap);
            _buildLabelIC = root.Q<Label>(ElementBuildLabelIC);
            _buildLabelPD = root.Q<Label>(ElementBuildLabelPD);
            _buildReanalysisCostLabel = root.Q<Label>(ElementBuildReanalysisCost);
            _buildNarrativePanel = root.Q<VisualElement>(ElementBuildNarrativePanel);
            _buildNarrativeText = root.Q<Label>(ElementBuildNarrativeText);
            _buildPurgeSection = root.Q<VisualElement>(ElementBuildPurgeSection);
            _buildPurgeYield = root.Q<Label>(ElementBuildPurgeYield);

            Button purgeBtn = root.Q<Button>(ElementBuildPurgeBtn);
            if (purgeBtn != null)
                purgeBtn.RegisterCallback<ClickEvent>(_ => OnPurgePressed?.Invoke());
            _buildReadonlyNotice = root.Q<Label>(ElementBuildReadonlyNotice);

            if (_buildOverlay == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_HUDController] '{ElementBuildOverlay}' not found. " +
                                 $"Build menu will not function.");
#endif
            }
            // Cache node buttons and register click callbacks.
            for (int b = 0; b < 3; b++)
            {
                for (int n = 0; n < 5; n++)
                {
                    string btnName = $"node-{BranchNames[b]}-{n}";
                    Button btn = root.Q<Button>(btnName);
                    _nodeButtons[b, n] = btn;

                    if (btn != null)
                    {
                        int capturedB = b;
                        int capturedN = n;
                        btn.RegisterCallback<ClickEvent>(_ =>
                            OnBuildNodePressed?.Invoke((BuildBranch)capturedB, capturedN));
                    }
                }

                string reaBtnName = $"reanalysis-{BranchNames[b]}";
                Button reaBtn = root.Q<Button>(reaBtnName);
                _reanalysisButtons[b] = reaBtn;

                if (reaBtn != null)
                {
                    int capturedB = b;
                    reaBtn.RegisterCallback<ClickEvent>(_ =>
                        OnBuildReanalysisPressed?.Invoke((BuildBranch)capturedB));
                }
            }

            Button closeBtn = root.Q<Button>(ElementBuildCloseBtn);
            if (closeBtn != null)
                closeBtn.RegisterCallback<ClickEvent>(_ => OnBuildMenuClosePressed?.Invoke());

            return allFound;
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region HUD Event Handlers

        private void OnHPChanged(float currentHP, float maxHP)
        {
            if (_hpBar == null) return;
            _hpBar.value = maxHP > 0f ? currentHP / maxHP : 0f;
        }

        private void OnEnergyChanged(float currentEnergy, float maxEnergy)
        {
            if (_energyBar == null) return;
            _energyBar.value = maxEnergy > 0f ? currentEnergy / maxEnergy : 0f;
        }

        private void OnScrapChanged(float currentScrap)
        {
            if (_scrapLabel == null) return;
            _scrapLabel.text = $"SC: {Mathf.FloorToInt(currentScrap)}";
        }

        private void OnIdentityCoresChanged(int currentCores)
        {
            if (_icLabel == null) return;
            _icLabel.text = $"IC: {currentCores}";
        }

        private void OnSurgeStateChanged(bool surgeActive, bool inCooldown)
        {
            if (_surgeIndicator == null) return;
            _surgeIndicator.EnableInClassList(CssSurgeActive, surgeActive);
            _surgeIndicator.EnableInClassList(CssSurgeCooldown, inCooldown && !surgeActive);
        }

        private void OnInteractionFocusChanged(bool isVisible, string targetName)
        {
            _isInteractionActive = isVisible;
            if (_interactionContainer == null) return;
            _interactionContainer.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
            if (isVisible && _interactionLabel != null)
                _interactionLabel.text = targetName;
        }

        private void OnInteractionProgressChanged(float progress)
        {
            if (_interactionProgress != null)
                _interactionProgress.value = progress * 100f;
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Build Menu Event Handlers

        private void OnBuildMenuOpenChanged(bool isOpen)
        {
            if (_buildOverlay == null) return;
            _buildOverlay.style.display = isOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (isOpen && _buildReadonlyNotice != null)
            {
                _buildReadonlyNotice.style.display = _model.BuildMenuInteractionEnabled
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }
        }

        private void OnBuildTreeRefreshed()
        {
            if (_model == null) return;

            // ── Resource chips ─────────────────────────────────────────────
            if (_buildLabelHP != null)
                _buildLabelHP.text = $"HP: {Mathf.FloorToInt(_model.CurrentHP)} / {Mathf.FloorToInt(_model.MaxHP)}";
            if (_buildLabelEnergy != null)
                _buildLabelEnergy.text = $"EN: {Mathf.FloorToInt(_model.CurrentEnergy)}";
            if (_buildLabelScrap != null)
                _buildLabelScrap.text = $"SC: {Mathf.FloorToInt(_model.CurrentScrap)}";
            if (_buildLabelIC != null)
                _buildLabelIC.text = $"IC: {_model.CurrentIdentityCores}";
            if (_buildLabelPD != null)
                _buildLabelPD.text = $"PD: {_model.BuildAvailablePD}";
            bool interactionEnabled = _model.BuildMenuInteractionEnabled;

            // Node buttons.
            for (int b = 0; b < 3; b++)
            {
                for (int n = 0; n < 5; n++)
                {
                    Button btn = _nodeButtons[b, n];
                    if (btn == null) continue;

                    SH_UIStateModel.BuildNodeDisplayData data =
                        _model.GetNodeDisplay((BuildBranch)b, n);

                    btn.text = data.State == SH_UIStateModel.BuildNodeDisplayState.Active
                        ? $"✓  {data.NodeName}"
                        : $"{data.NodeName}\n{data.CostLabel}";

                    btn.EnableInClassList(CssNodeActive,
                        data.State == SH_UIStateModel.BuildNodeDisplayState.Active);
                    btn.EnableInClassList(CssNodeNext,
                        data.State == SH_UIStateModel.BuildNodeDisplayState.Next);
                    btn.EnableInClassList(CssNodeUnavailable,
                        data.State == SH_UIStateModel.BuildNodeDisplayState.Unavailable
                     || data.State == SH_UIStateModel.BuildNodeDisplayState.Locked);

                    // Only enable interaction when opened from the terminal.
                    bool canInteract = interactionEnabled
                                    && data.State == SH_UIStateModel.BuildNodeDisplayState.Next;
                    btn.SetEnabled(canInteract);
                }
            }

            // Reanalysis buttons.
            float reaCost = _model.BuildReanalysisCost;
            if (_buildReanalysisCostLabel != null)
                _buildReanalysisCostLabel.text = _model.BuildHasActiveBuild && interactionEnabled
                    ? $"Reanalysis cost: {Mathf.FloorToInt(reaCost)} SC"
                    : _model.BuildHasActiveBuild
                        ? "Return to base to reanalyze"
                        : string.Empty;

            for (int b = 0; b < 3; b++)
            {
                Button reaBtn = _reanalysisButtons[b];
                if (reaBtn == null) continue;

                bool canReanalyze = interactionEnabled
                                 && _model.BuildHasActiveBuild
                                 && (int)_model.BuildActiveBranch != b
                                 && _model.CurrentScrap >= reaCost;
                reaBtn.SetEnabled(canReanalyze);
            }
        }

        private void OnBuildNarrativeChanged(bool isVisible, string text)
        {
            if (_buildNarrativePanel == null) return;
            _buildNarrativePanel.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
            if (_buildNarrativeText != null)
                _buildNarrativeText.text = text ?? string.Empty;
        }

        private void OnPurgeDataChanged(int icAvailable, int dpYield, bool enabled)
        {
            if (_buildPurgeSection == null) return;

            // Show the purge section only when in terminal mode and there are IC to purge.
            bool showSection = _model.BuildMenuInteractionEnabled && icAvailable > 0;
            _buildPurgeSection.style.display = showSection
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            if (_buildPurgeYield != null)
            {
                _buildPurgeYield.text = dpYield > 0
                    ? $"{icAvailable} IC  →  +{dpYield} PD"
                    : $"{icAvailable} IC  →  no PD gain";
            }

            // Find and update the purge button's enabled state.
            Button purgeBtn = _buildPurgeSection?.Q<Button>(ElementBuildPurgeBtn);
            if (purgeBtn != null)
                purgeBtn.SetEnabled(enabled && dpYield > 0);
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Initial State Sync

        private void PushCurrentModelState()
        {
            OnHPChanged(_model.CurrentHP, _model.MaxHP);
            OnEnergyChanged(_model.CurrentEnergy, _model.MaxEnergy);
            OnScrapChanged(_model.CurrentScrap);
            OnIdentityCoresChanged(_model.CurrentIdentityCores);
            OnSurgeStateChanged(_model.IsSurgeActive, _model.IsInSurgeCooldown);

            // Build menu starts closed — no tree refresh needed at init.
            if (_buildOverlay != null)
                _buildOverlay.style.display = DisplayStyle.None;
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Interaction Position (LateUpdate)

        private void LateUpdate()
        {
            if (!_isInteractionActive || _interactionContainer == null) return;

            Vector2 screenPos = RuntimePanelUtils.CameraTransformWorldToPanel(
                _interactionContainer.panel,
                _model.TargetWorldPosition + Vector3.up * 1.5f,
                Camera.main);

            _interactionContainer.style.left =
                screenPos.x - (_interactionContainer.layout.width / 2f);
            _interactionContainer.style.top =
                screenPos.y - (_interactionContainer.layout.height / 2f);
        }

        #endregion
    }
}