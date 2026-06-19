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
        private const string CssFocusActive = "focus-active";
        private const string CssFocusFaded = "focus-faded";

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region UXML Element Name Constants — HUD

        private const string ElementHPBar = "hp-bar";
        private const string ElementEnergyBar = "energy-bar";
        private const string ElementScrapLabel = "scrap-label";
        private const string ElementICLabel = "ic-label";
        private const string ElementSurgeBar = "surge-bar";
        private const string ElementSurgeIndicator = "surge-dot-active";
        private const string ElementSurgeCooldown = "surge-dot-cooldown";
        private const string ElementInteractionContainer = "interaction-container";
        private const string ElementInteractionLabel = "interaction-label";
        private const string ElementInteractionProgress = "interaction-progress";
        private const string ElementBuildReadonlyNotice = "build-readonly-notice";
        private const string ElementBuildPurgeNotice = "build-purge-notice";
        private const string ElementBuildPurgeSection = "build-purge-section";
        private const string ElementBuildPurgeYield = "build-purge-yield";
        private const string ElementBuildPurgeBtn = "build-purge-btn";
        private const string ElementReticleFocus = "reticle-focus";
        private const string ElementCooldownLight = "cooldown-arc-light";
        private const string ElementCooldownHeavy = "cooldown-arc-heavy";
        private const string ElementCooldownDash = "cooldown-arc-dash";
        private const string ElementAarcFill = "arc-fill";
        private const string ElementDataLogOverlay = "datalog-overlay";
        private const string ElementDataLogTitle = "datalog-title";
        private const string ElementDataLogSource = "datalog-source";
        private const string ElementDataLogBody = "datalog-body";
        private const string ElementDataLogCloseBtn = "datalog-close-btn";

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

        /// <summary>
        /// Fired when the player clicks the data log close button.
        /// </summary>
        public event Action OnDataLogClosePressed;

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Runtime References — HUD

        private UIDocument _document;
        private ProgressBar _hpBar;
        private ProgressBar _energyBar;
        private ProgressBar _surgeBar;
        private VisualElement _surgeDotCooldown;
        private Label _scrapLabel;
        private Label _icLabel;
        private VisualElement _surgeIndicator;
        private VisualElement _interactionContainer;
        private Label _interactionLabel;
        private ProgressBar _interactionProgress;
        private bool _isInteractionActive;
        private VisualElement _reticleFocus;
        private VisualElement _cooldownArcLight;
        private VisualElement _cooldownArcHeavy;
        private VisualElement _cooldownArcDash;
        private VisualElement _dataLogOverlay;
        private Label _dataLogTitle;
        private Label _dataLogSource;
        private Label _dataLogBody;

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
        private Label _buildPurgeNotice;
        private VisualElement _buildPurgeSection;
        private Label _buildPurgeYield;
        private Coroutine _noticeFadeCoroutine;
        private bool _readonlyNoticeExpired = true;
        private bool _purgeNoticeExpired = true;

        [Header("UI Configuration")]
        [Tooltip("Duration in seconds before the terminal warnings disappear.")]
        [SerializeField] private float _noticeDisplayDuration = 4.0f;

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
            _model.OnSurgeProgressChanged += OnSurgeProgressChanged;
            _model.OnInteractionFocusChanged += OnInteractionFocusChanged;
            _model.OnInteractionProgressChanged += OnInteractionProgressChanged;
            _model.OnActionCooldownsChanged += OnActionCooldownsChanged;

            // Build menu events.
            _model.OnBuildMenuOpenChanged += OnBuildMenuOpenChanged;
            _model.OnBuildTreeRefreshed += OnBuildTreeRefreshed;
            _model.OnBuildNarrativeChanged += OnBuildNarrativeChanged;
            _model.OnDataLogChanged += OnDataLogChanged;
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
            _model.OnSurgeProgressChanged -= OnSurgeProgressChanged;
            _model.OnInteractionFocusChanged -= OnInteractionFocusChanged;
            _model.OnInteractionProgressChanged -= OnInteractionProgressChanged;
            _model.OnActionCooldownsChanged -= OnActionCooldownsChanged;

            _model.OnBuildMenuOpenChanged -= OnBuildMenuOpenChanged;
            _model.OnBuildTreeRefreshed -= OnBuildTreeRefreshed;
            _model.OnBuildNarrativeChanged -= OnBuildNarrativeChanged;
            _model.OnDataLogChanged -= OnDataLogChanged;

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
            _surgeBar = root.Q<ProgressBar>(ElementSurgeBar);
            _surgeIndicator = root.Q<VisualElement>(ElementSurgeIndicator);
            _surgeDotCooldown = root.Q<VisualElement>(ElementSurgeCooldown);
            _interactionContainer = root.Q<VisualElement>(ElementInteractionContainer);
            _interactionLabel = root.Q<Label>(ElementInteractionLabel);
            _interactionProgress = root.Q<ProgressBar>(ElementInteractionProgress);
            _reticleFocus = root.Q<VisualElement>(ElementReticleFocus);
            _cooldownArcLight = root.Q<VisualElement>(ElementCooldownLight);
            _cooldownArcHeavy = root.Q<VisualElement>(ElementCooldownHeavy);
            _cooldownArcDash = root.Q<VisualElement>(ElementCooldownDash);

            
            if (_surgeBar != null)
            {
                _surgeBar.lowValue = 0f;
                _surgeBar.highValue = 1f;
                _surgeBar.value = 0f;
            }

            if (_reticleFocus != null)
                _reticleFocus.AddToClassList(CssFocusFaded);

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
            if (_surgeBar == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_HUDController] '{ElementSurgeBar}' not found.");
#endif
                allFound = false;
            }
            if ( _surgeDotCooldown == null) {
#if UNITY_EDITOR
                Debug.LogError($"[SH_HUDController] '{ElementSurgeCooldown}' not found.");
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
            _buildPurgeNotice = root.Q<Label>(ElementBuildPurgeNotice);

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

            _dataLogOverlay = root.Q<VisualElement>(ElementDataLogOverlay);
            _dataLogTitle = root.Q<Label>(ElementDataLogTitle);
            _dataLogSource = root.Q<Label>(ElementDataLogSource);
            _dataLogBody = root.Q<Label>(ElementDataLogBody);

            Button dataLogCloseBtn = root.Q<Button>(ElementDataLogCloseBtn);
            if (dataLogCloseBtn != null)
                dataLogCloseBtn.RegisterCallback<ClickEvent>(_ => OnDataLogClosePressed?.Invoke());

            return allFound;
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region HUD Event Handlers

        private void OnHPChanged(float currentHP, float maxHP)
        {
            if (_hpBar == null) return;
            _hpBar.value = maxHP > 0f ? currentHP / maxHP : 1f;
            _hpBar.value = currentHP;
        }

        private void OnEnergyChanged(float currentEnergy, float maxEnergy)
        {
            if (_energyBar == null) return;
            _energyBar.value = maxEnergy > 0f ? currentEnergy / maxEnergy : 1f;
            _energyBar.value = currentEnergy;
        }

        private void OnScrapChanged(float currentScrap)
        {
            if (_scrapLabel == null) return;
            _scrapLabel.text = Mathf.FloorToInt(currentScrap).ToString("D4");
        }

        private void OnIdentityCoresChanged(int currentCores)
        {
            if (_icLabel == null) return;
            _icLabel.text = currentCores.ToString("D2");
        }

        private void OnSurgeStateChanged(bool surgeActive, bool inCooldown)
        {
            if (_surgeIndicator != null)
            {
                if (surgeActive)
                    _surgeIndicator.AddToClassList(CssSurgeActive);
                else
                    _surgeIndicator.RemoveFromClassList(CssSurgeActive);
            }

            if (_surgeDotCooldown != null)
            {
                if (inCooldown)
                    _surgeDotCooldown.AddToClassList(CssSurgeCooldown);
                else
                    _surgeDotCooldown.RemoveFromClassList(CssSurgeCooldown);
            }
        }

        private void OnSurgeProgressChanged(float progressNormalized)
        {
            if (_surgeBar != null)
            {
                _surgeBar.value = Mathf.Clamp01(progressNormalized);
            }
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

        private void OnActionCooldownsChanged(float light01, float heavy01, float dash01)
        {
            SetArcProgress(_cooldownArcLight, light01);
            SetArcProgress(_cooldownArcHeavy, heavy01);
            SetArcProgress(_cooldownArcDash, dash01);

            // Focus container: faded when all actions ready, active when any in cooldown.
            bool anyInCooldown = light01 < 0.999f || heavy01 < 0.999f || dash01 < 0.999f;
            if (_reticleFocus != null)
            {
                _reticleFocus.EnableInClassList(CssFocusActive, anyInCooldown);
                _reticleFocus.EnableInClassList(CssFocusFaded, !anyInCooldown);
            }
        }

        /// <summary>
        /// Drives a semicircular arc by setting --arc-progress custom property [0,1].
        /// The USS uses this variable to control the arc sweep via background rotation.
        /// </summary>
        private void SetArcProgress(VisualElement arcContainer, float progress01)
        {
            if (arcContainer == null) return;
            
            //VisualElement fill = arcContainer.hierarchy.childCount > 0
            //    ? arcContainer.hierarchy[0] : null;
            VisualElement fill = arcContainer.Q<VisualElement>(className: ElementAarcFill);
            if (fill == null) return;

            // Rotate fill element: -180° = empty arc, 0° = full arc
            float angleDeg = Mathf.Lerp(135f, -45f, progress01);
            fill.style.rotate = new StyleRotate(new Rotate(angleDeg));
            fill.style.opacity = progress01 < 0.999f ? 1f : 0.25f;

        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Build Menu Event Handlers

        private void OnBuildMenuOpenChanged(bool isOpen)
        {
            if (_buildOverlay == null) return;

            _buildOverlay.style.display = isOpen ? DisplayStyle.Flex : DisplayStyle.None;

            if (isOpen)
            {
                if (_buildReadonlyNotice != null)
                {
                    _buildReadonlyNotice.style.display = (!_model.BuildMenuInteractionEnabled && !_readonlyNoticeExpired)
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                }

                if (_buildPurgeNotice != null)
                {
                    _buildPurgeNotice.style.display = (_model.BuildPurgeRequirementsNotMet && !_purgeNoticeExpired)
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                }
            }
        }

        /// <summary>
        /// Public entry point to invoke or update terminal window warnings.
        /// Restarts the chronological real-time clock independently of panel visibility.
        /// </summary>
        public void TriggerNoticesTimeout()
        {
            if (_noticeFadeCoroutine != null)
            {
                StopCoroutine(_noticeFadeCoroutine);
            }

            _readonlyNoticeExpired = false;
            _purgeNoticeExpired = false;

            if (_buildOverlay != null && _buildOverlay.style.display == DisplayStyle.Flex)
            {
                if (_buildReadonlyNotice != null)
                {
                    _buildReadonlyNotice.style.display = !_model.BuildMenuInteractionEnabled ? DisplayStyle.Flex : DisplayStyle.None;
                }

                if (_buildPurgeNotice != null)
                {
                    _buildPurgeNotice.style.display = _model.BuildPurgeRequirementsNotMet ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }

            _noticeFadeCoroutine = StartCoroutine(FadeOutNoticesRoutine());
        }

        private void OnBuildTreeRefreshed()
        {
            if (_model == null) return;

            // ── Resource chips ─────────────────────────────────────────────
            if (_buildLabelHP != null)
                _buildLabelHP.text = $"Integridad: {Mathf.FloorToInt(_model.CurrentHP)} / {Mathf.FloorToInt(_model.MaxHP)}";
            if (_buildLabelEnergy != null)
                _buildLabelEnergy.text = $"Energía: {Mathf.FloorToInt(_model.CurrentEnergy)}";
            if (_buildLabelScrap != null)
                _buildLabelScrap.text = $"Chatarra: {Mathf.FloorToInt(_model.CurrentScrap)}";
            if (_buildLabelIC != null)
                _buildLabelIC.text = $"Núcleo \r\nIdentidad: {_model.CurrentIdentityCores}";
            if (_buildLabelPD != null)
                _buildLabelPD.text = $"Punto de \r\nDesarrollo: {_model.BuildAvailablePD}";
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
                    ? $"Costo de reanálisis: {Mathf.FloorToInt(reaCost)} CH"
                    : _model.BuildHasActiveBuild
                        ? "Regresa al faro para reanálisis"
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
                    ? $"{icAvailable} Núcleos de Identidad  →  +{dpYield} PD"
                    : $"{icAvailable} Núcleos de Identidad  →  no se obtendrán Puntos de Desarrollo";
            }

            // Find and update the purge button's enabled state.
            Button purgeBtn = _buildPurgeSection?.Q<Button>(ElementBuildPurgeBtn);
            if (purgeBtn != null)
                purgeBtn.SetEnabled(enabled && dpYield > 0);
        }

        private void OnDataLogChanged(bool isOpen, string title, string source, string body)
        {
            if (_dataLogOverlay == null) return;

            _dataLogOverlay.style.display = isOpen ? DisplayStyle.Flex : DisplayStyle.None;

            if (!isOpen) return;

            if (_dataLogTitle != null) _dataLogTitle.text = title;
            if (_dataLogSource != null) _dataLogSource.text = source;
            if (_dataLogBody != null) _dataLogBody.text = body;
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
            OnSurgeProgressChanged(_model.MaxSurgeProgress > 0f ? _model.CurrentSrugeProgress / _model.MaxSurgeProgress : 0f);

            // Build menu starts closed — no tree refresh needed at init.
            if (_buildOverlay != null)
                _buildOverlay.style.display = DisplayStyle.None;

            if (_dataLogOverlay != null)
                _dataLogOverlay.style.display = DisplayStyle.None;
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

        #region Terminal Notices Fade-Out Routine

        /// <summary>
        /// Execution routine that waits for real-time seconds and turns off
        /// the visibility state flags independently of current layout visibility.
        /// </summary>
        private System.Collections.IEnumerator FadeOutNoticesRoutine()
        {
            yield return new WaitForSecondsRealtime(_noticeDisplayDuration);

            _readonlyNoticeExpired = true;
            _purgeNoticeExpired = true;

            if (_buildReadonlyNotice != null)
            {
                _buildReadonlyNotice.style.display = DisplayStyle.None;
            }

            if (_buildPurgeNotice != null)
            {
                _buildPurgeNotice.style.display = DisplayStyle.None;
            }

            _noticeFadeCoroutine = null;
        }

        #endregion
    }
}