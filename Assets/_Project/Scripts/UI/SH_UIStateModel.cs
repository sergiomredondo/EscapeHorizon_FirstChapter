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

        /// <summary>
        /// True while the Energy Surge state is active and boosting combat attributes.
        /// </summary>
        public bool IsSurgeActive => _isSurgeActive;

        /// <summary>
        /// True during the post-Surge cooldown penalty window.
        /// Mutually exclusive with IsSurgeActive.
        /// </summary>
        public bool IsInSurgeCooldown => _isInSurgeCooldown;

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
        #region Utilities

        /// <summary>
        /// Floating-point equality check with a tolerance of 0.001 to avoid
        /// firing change events for sub-pixel value differences that would produce
        /// no visible change in the UI.
        /// </summary>
        private static bool Approximately(float a, float b) => Math.Abs(a - b) < 0.001f;

        #endregion
    }
}