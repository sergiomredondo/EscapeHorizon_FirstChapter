using Game.Progression.Data;
using System;
using UnityEngine;

namespace UI
{
    /// <summary>
    /// Pure C# state container for all data the UI needs to display.
    /// Holds the current values of gameplay systems and fires events
    /// when those values change, so UI controllers can update reactively
    /// without polling and without coupling to gameplay scripts directly.
    ///
    /// Responsibility boundaries:
    ///   OWNS: Current display values and change notification events.
    ///   DOES NOT OWN: How values are computed (SH_UIBridge reads gameplay systems).
    ///   DOES NOT OWN: How values are rendered (SH_HUDController reads this model).
    ///
    /// Usage:
    ///   Instantiated once by SH_UIBridge in Awake().
    ///   Reference passed by injection to SH_HUDController before OnEnable() fires.
    ///   Never accessed via singleton or static field.
    /// </summary>
    public class SH_UIStateModel
    {
        // ─────────────────────────────────────────────────────────────────────
        #region Health

        /// <summary>
        /// Fired when the Mecha's current or maximum HP changes.
        /// Parameters: (float currentHP, float maxHP).
        /// Consumed by: SH_HUDController → hp-bar ProgressBar.
        /// </summary>
        public event Action<float, float> OnHPChanged;

        private float _currentHP;
        private float _maxHP;

        /// <summary> Current Mecha durability. Clamped to [0, MaxHP] by SH_UIBridge. </summary>
        public float CurrentHP => _currentHP;

        /// <summary> Maximum Mecha durability. Set once at initialization and updated on build changes. </summary>
        public float MaxHP => _maxHP;

        /// <summary>
        /// Updates HP values and fires OnHPChanged only when at least one value differs
        /// from the previously stored value, preventing redundant UI redraws.
        /// </summary>
        /// <param name="current"> Current HP. Must be >= 0. </param>
        /// <param name="max"> Maximum HP. Must be > 0. </param>
        public void SetHP(float current, float max)
        {
            bool changed = !Approximately(_currentHP, current) || !Approximately(_maxHP, max);
            if (!changed) return;

            _currentHP = current;
            _maxHP = max;
            OnHPChanged?.Invoke(_currentHP, _maxHP);
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Energy

        /// <summary>
        /// Fired when the Mecha's current or maximum Energy changes.
        /// Parameters: (float currentEnergy, float maxEnergy).
        /// Consumed by: SH_HUDController → energy-bar ProgressBar.
        /// </summary>
        public event Action<float, float> OnEnergyChanged;

        private float _currentEnergy;
        private float _maxEnergy;

        /// <summary> Current Energy pool available for actions and Surge. </summary>
        public float CurrentEnergy => _currentEnergy;

        /// <summary> Maximum Energy pool defined in SH_EconomySettings. </summary>
        public float MaxEnergy => _maxEnergy;

        /// <summary>
        /// Updates Energy values and fires OnEnergyChanged only when at least one value differs.
        /// </summary>
        /// <param name="current"> Current energy. Must be >= 0. </param>
        /// <param name="max"> Maximum energy. Must be > 0. </param>
        public void SetEnergy(float current, float max)
        {
            bool changed = !Approximately(_currentEnergy, current) || !Approximately(_maxEnergy, max);
            if (!changed) return;

            _currentEnergy = current;
            _maxEnergy = max;
            OnEnergyChanged?.Invoke(_currentEnergy, _maxEnergy);
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Scrap

        /// <summary>
        /// Fired when the Scrap resource amount changes.
        /// Parameters: (float currentScrap).
        /// Consumed by: SH_HUDController → scrap-label Label.
        /// </summary>
        public event Action<float> OnScrapChanged;

        private float _currentScrap;

        /// <summary> Current Scrap (SC) owned by the player. </summary>
        public float CurrentScrap => _currentScrap;

        /// <summary>
        /// Updates the Scrap value and fires OnScrapChanged only when the value differs.
        /// </summary>
        /// <param name="current"> Current scrap amount. Must be >= 0. </param>
        public void SetScrap(float current)
        {
            if (Approximately(_currentScrap, current)) return;

            _currentScrap = current;
            OnScrapChanged?.Invoke(_currentScrap);
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Identity Cores

        /// <summary>
        /// Fired when the Identity Core count changes.
        /// Parameters: (int currentCores).
        /// Consumed by: SH_HUDController → ic-label Label.
        /// </summary>
        public event Action<int> OnIdentityCoresChanged;

        private int _currentIdentityCores;

        /// <summary> Current Identity Core (IC) count owned by the player. </summary>
        public int CurrentIdentityCores => _currentIdentityCores;

        /// <summary>
        /// Updates the Identity Core count and fires OnIdentityCoresChanged only when the value differs.
        /// </summary>
        /// <param name="current"> Current IC count. Must be >= 0. </param>
        public void SetIdentityCores(int current)
        {
            if (_currentIdentityCores == current) return;

            _currentIdentityCores = current;
            OnIdentityCoresChanged?.Invoke(_currentIdentityCores);
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Energy Surge State

        /// <summary>
        /// Fired when the Energy Surge active or cooldown state changes.
        /// Parameters: (bool isSurgeActive, bool isInSurgeCooldown).
        /// Consumed by: SH_HUDController → surge-indicator VisualElement CSS classes.
        /// </summary>
        public event Action<bool, bool> OnSurgeStateChanged;
        public event Action<bool, string> OnInteractionFocusChanged;
        public event Action<float> OnInteractionProgressChanged;

        private bool _isSurgeActive;
        private bool _isInSurgeCooldown;
        private bool _isInteractionVisible;
        private string _interactionTargetName;
        private float _interactionProgress;
        private Vector3 _targetWorldPosition;
        public Vector3 TargetWorldPosition => _targetWorldPosition;
        private float _currentSurgeProgress;
        private float _maxSurgeProgress;

        /// <summary>
        /// True while the Energy Surge state is active and boosting combat attributes.
        /// </summary>
        public bool IsSurgeActive => _isSurgeActive;

        /// <summary>
        /// True during the post-Surge cooldown penalty window.
        /// Mutually exclusive with IsSurgeActive.
        /// </summary>
        public bool IsInSurgeCooldown => _isInSurgeCooldown;

        /// <summary> Current progress of the surge system or cooldown timer. </summary>
        public float CurrentSrugeProgress => _currentSurgeProgress;

        /// <summary> Maximum threshold of the surge state duration or capacity. </summary>
        public float MaxSurgeProgress => _maxSurgeProgress;

        /// <summary>
        /// Updates the Surge state flags and fires OnSurgeStateChanged only when
        /// either flag differs from the previously stored value.
        /// </summary>
        /// <param name="surgeActive"> Whether the Surge boost is currently active. </param>
        /// <param name="inCooldown"> Whether the post-Surge cooldown penalty is active. </param>
        public void SetSurgeState(bool surgeActive, bool inCooldown)
        {
            if (_isSurgeActive == surgeActive && _isInSurgeCooldown == inCooldown) return;

            _isSurgeActive = surgeActive;
            _isInSurgeCooldown = inCooldown;
            OnSurgeStateChanged?.Invoke(_isSurgeActive, _isInSurgeCooldown);
        }

        /// <summary>
        /// Updates the Surge progress values and fires OnSurgeProgressChanged with a normalized ratio.
        /// </summary>
        /// <param name="current"> Current surge progress value. </param>
        /// <param name="max"> Maximum surge capacity value. Must be > 0. </param>
        public void SetSurgeProgress(float current, float max)
        {
            bool changed = !Approximately(_currentSurgeProgress, current) || !Approximately(_maxSurgeProgress, max);
            if (!changed) return;

            _currentSurgeProgress = current;
            _maxSurgeProgress = max;

            float normalized = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            OnSurgeProgressChanged?.Invoke(normalized);
        }

        /// <summary>
        /// Fired when the Energy Surge progress values change.
        /// Parameters: (float progressNormalized).
        /// Consumed by: SH_HUDController → surge-bar ProgressBar.
        /// </summary>
        public event Action<float> OnSurgeProgressChanged;

        public void SetInteractionFocus(bool isVisible, string targetName)
        {
            if (_isInteractionVisible == isVisible && _interactionTargetName == targetName) return;
            _isInteractionVisible = isVisible;
            _interactionTargetName = targetName;
            OnInteractionFocusChanged?.Invoke(_isInteractionVisible, _interactionTargetName);
        }

        public void SetInteractionProgress(float progress)
        {
            if (Approximately(_interactionProgress, progress)) return;
            _interactionProgress = progress;
            OnInteractionProgressChanged?.Invoke(_interactionProgress);
        }

        public void UpdateTargetPosition(Vector3 position)
        {
            _targetWorldPosition = position;
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Build Menu

        // ── Node display state ────────────────────────────────────────────────

        /// <summary>
        /// Represents the visual state of a single node button in the Analysis Tree.
        /// </summary>
        public enum BuildNodeDisplayState
        {
            /// <summary> Node is in the active branch and has been purchased. </summary>
            Active,
            /// <summary> Node is the next purchasable slot in the active (or unstarted) branch. </summary>
            Next,
            /// <summary> Node is in a non-active branch while another branch is active. </summary>
            Unavailable,
            /// <summary> Node requires a previous node to be purchased first. </summary>
            Locked
        }

        /// <summary> Snapshot of display data for a single node button. </summary>
        public struct BuildNodeDisplayData
        {
            public string NodeName;
            public string CostLabel;
            public BuildNodeDisplayState State;
        }

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when the build menu overlay should appear or disappear.
        /// Parameters: (bool isOpen).
        /// </summary>
        public event Action<bool> OnBuildMenuOpenChanged;

        /// <summary>
        /// Fired when any aspect of the tree state changes (node activated,
        /// reanalysis performed, build deactivated). Controller reads all
        /// public Build properties after receiving this event.
        /// </summary>
        public event Action OnBuildTreeRefreshed;

        /// <summary>
        /// Fired when the captive memory narrative panel should show or hide.
        /// Parameters: (bool isVisible, string narrativeText).
        /// </summary>
        public event Action<bool, string> OnBuildNarrativeChanged;

        // ── Backing fields ────────────────────────────────────────────────────

        private bool _buildMenuOpen;
        private BuildBranch _buildActiveBranch;
        private int _buildActiveNodeCount;
        private int _buildAvailablePD;
        private float _buildReanalysisCost;
        private bool _buildHasActiveBuild;

        /// <summary>
        /// When false, the menu is in read-only mode: node and reanalysis buttons
        /// are disabled and a context label explains the restriction.
        /// True only when opened from the Operations Terminal.
        /// </summary>
        public bool BuildMenuInteractionEnabled { get; private set; }
        public void SetBuildMenuInteractionEnabled(bool enabled)
        {
            BuildMenuInteractionEnabled = enabled;
        }

        /// <summary>
        /// True when the menu is opened from the terminal but the player 
        /// does not have enough Identity Cores to perform a purge.
        /// </summary>
        public bool BuildPurgeRequirementsNotMet { get; private set; }
        public void SetPurgeRequirementsNotMet(bool notMet)
        {
            BuildPurgeRequirementsNotMet = notMet;
        }

        // Node display data — 3 branches × 5 nodes.
        private readonly BuildNodeDisplayData[,] _nodeDisplayData =
            new BuildNodeDisplayData[3, 5];

        // ── Public read-only properties ───────────────────────────────────────

        public bool BuildMenuOpen => _buildMenuOpen;
        public BuildBranch BuildActiveBranch => _buildActiveBranch;
        public int BuildActiveNodeCount => _buildActiveNodeCount;
        public int BuildAvailablePD => _buildAvailablePD;
        public float BuildReanalysisCost => _buildReanalysisCost;
        public bool BuildHasActiveBuild => _buildHasActiveBuild;

        /// <summary>
        /// Returns the cached display data for the given branch and zero-based node index.
        /// </summary>
        public BuildNodeDisplayData GetNodeDisplay(BuildBranch branch, int index)
        {
            int b = (int)branch;
            if (b < 0 || b > 2 || index < 0 || index > 4)
                return default;
            return _nodeDisplayData[b, index];
        }

        // ── Setters ───────────────────────────────────────────────────────────

        /// <summary>
        /// Opens or closes the build menu overlay.
        /// Fires OnBuildMenuOpenChanged only on actual state transition.
        /// </summary>
        public void SetBuildMenuOpen(bool isOpen)
        {
            if (_buildMenuOpen == isOpen) return;
            _buildMenuOpen = isOpen;
            OnBuildMenuOpenChanged?.Invoke(_buildMenuOpen);
        }

        /// <summary>
        /// Pushes a full snapshot of the Analysis Tree state to the model.
        /// Called by SH_UIBridge whenever the build system reports any change.
        /// Fires OnBuildTreeRefreshed unconditionally so the controller always
        /// redraws after a transaction, even if aggregate values are unchanged.
        /// </summary>
        public void SetBuildTreeState(
            BuildBranch activeBranch,
            int activeNodeCount,
            bool hasActiveBuild,
            int availablePD,
            float reanalysisCost,
            BuildNodeDisplayData[,] nodeData)
        {
            _buildActiveBranch = activeBranch;
            _buildActiveNodeCount = activeNodeCount;
            _buildHasActiveBuild = hasActiveBuild;
            _buildAvailablePD = availablePD;
            _buildReanalysisCost = reanalysisCost;

            // Copy node display data into the backing array.
            for (int b = 0; b < 3; b++)
                for (int n = 0; n < 5; n++)
                    _nodeDisplayData[b, n] = nodeData[b, n];

            OnBuildTreeRefreshed?.Invoke();
        }

        /// <summary>
        /// Shows or hides the captive memory narrative panel.
        /// </summary>
        public void SetBuildNarrative(bool isVisible, string text)
        {
            OnBuildNarrativeChanged?.Invoke(isVisible, text);
        }

        // ── Purge state ───────────────────────────────────────────────────────

        /// <summary>
        /// Fired when the purge section data changes (IC count, DP yield, availability).
        /// </summary>
        public event Action<int, int, bool> OnPurgeDataChanged;

        private int _purgeICAvailable;
        private int _purgeDPYield;
        private bool _purgeEnabled;

        public int PurgeICAvailable => _purgeICAvailable;
        public int PurgeDPYield => _purgeDPYield;
        public bool PurgeEnabled => _purgeEnabled;

        /// <summary>
        /// Updates the purge section state.
        /// </summary>
        /// <param name="icAvailable"> Current Identity Core count. </param>
        /// <param name="dpYield"> Development Points that would be generated by purging. </param>
        /// <param name="enabled"> True only when opened from the terminal and IC > 0. </param>
        public void SetPurgeData(int icAvailable, int dpYield, bool enabled)
        {
            bool changed = _purgeICAvailable != icAvailable
                        || _purgeDPYield != dpYield
                        || _purgeEnabled != enabled;
            if (!changed) return;

            _purgeICAvailable = icAvailable;
            _purgeDPYield = dpYield;
            _purgeEnabled = enabled;
            OnPurgeDataChanged?.Invoke(_purgeICAvailable, _purgeDPYield, _purgeEnabled);
        }


        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Data Log

        /// <summary>
        /// Fired when the text log panel should open or update.
        /// Parameters: (bool isOpen, string title, string source, string body).
        /// </summary>
        public event Action<bool, string, string, string> OnDataLogChanged;

        public bool DataLogOpen { get; private set; }
        public string DataLogTitle { get; private set; }
        public string DataLogSource { get; private set; }
        public string DataLogBody { get; private set; }

        public void SetDataLog(bool isOpen, string title, string source, string body)
        {
            DataLogOpen = isOpen;
            DataLogTitle = title ?? string.Empty;
            DataLogSource = source ?? string.Empty;
            DataLogBody = body ?? string.Empty;
            OnDataLogChanged?.Invoke(DataLogOpen, DataLogTitle, DataLogSource, DataLogBody);
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Utilities

        /// <summary>
        /// Floating-point equality check with a tolerance of 0.001 to avoid
        /// firing change events for sub-pixel value differences that would produce
        /// no visible change in the UI.
        /// </summary>
        private static bool Approximately(float a, float b) => Math.Abs(a - b) < 0.001f;


        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Action Cooldowns

        /// <summary>
        /// Fired when any action cooldown progress changes.
        /// Parameters: (float lightAttack01, float heavyAttack01, float dash01).
        /// Values range [0, 1]: 0 = action executing/in cooldown (arc empty),
        /// 1 = action ready (arc full).
        /// Consumed by: SH_HUDController → reticle cooldown arcs.
        /// </summary>
        public event Action<float, float, float> OnActionCooldownsChanged;

        private float _lightAttackCooldown01 = 1f;
        private float _heavyAttackCooldown01 = 1f;
        private float _dashCooldown01 = 1f;

        public float LightAttackCooldown01 => _lightAttackCooldown01;
        public float HeavyAttackCooldown01 => _heavyAttackCooldown01;
        public float DashCooldown01 => _dashCooldown01;

        /// <summary>
        /// Updates the three action cooldown progress values and fires
        /// OnActionCooldownsChanged only when at least one value differs.
        /// </summary>
        public void SetActionCooldowns(float lightAttack01, float heavyAttack01, float dash01)
        {
            bool changed = !Approximately(_lightAttackCooldown01, lightAttack01)
                        || !Approximately(_heavyAttackCooldown01, heavyAttack01)
                        || !Approximately(_dashCooldown01, dash01);
            if (!changed) return;

            _lightAttackCooldown01 = lightAttack01;
            _heavyAttackCooldown01 = heavyAttack01;
            _dashCooldown01 = dash01;
            OnActionCooldownsChanged?.Invoke(
                _lightAttackCooldown01,
                _heavyAttackCooldown01,
                _dashCooldown01);
        }

        #endregion
    }
}