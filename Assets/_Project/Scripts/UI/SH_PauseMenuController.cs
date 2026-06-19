using Core.Input;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace UI
{
    /// <summary>
    /// Manages the in-game pause menu and the shared Parameters screen.
    ///
    /// Channels:
    ///   Pause overlay      — Resume / Retry / Parameters / Exit.
    ///   Parameters overlay — three tabs: Settings, Controls, Narrative.
    ///
    /// Navigation:
    ///   W / S / Arrow keys  — select pause buttons.
    ///   A / D / Arrow keys  — switch parameters tabs.
    ///   Gamepad left stick and D-pad also work.
    ///   E / Enter / Space / Gamepad South — confirm.
    ///   Escape / Gamepad Start            — toggle pause or close parameters.
    ///
    /// Post-processing:
    ///   Brightness → ColorAdjustments.postExposure.
    ///   Contrast   → ColorAdjustments.contrast.
    ///   Gamma      → LiftGammaGain.gamma (w channel).
    ///   Requires a Volume with both overrides in the scene.
    ///
    /// Settings persist via PlayerPrefs and load at Start().
    /// OpenStandalone() allows the title screen to open Parameters
    ///   without entering the pause state.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public class SH_PauseMenuController : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────
        public static SH_PauseMenuController Instance { get; private set; }

        // ── Inspector ──────────────────────────────────────────────────────

        [Header("Narrative Tab Content")]
        [TextArea(5, 20)]
        [SerializeField] private string _narrativeText = "";

        [TextArea(2, 6)]
        [SerializeField] private string _creditsText = "";

        [Header("Post Processing (optional)")]
        [Tooltip("URP Volume that contains ColorAdjustments and LiftGammaGain overrides.")]
        [SerializeField] private Volume _postProcessVolume;

        // ── Runtime state ──────────────────────────────────────────────────

        private UIDocument _document;
        private VisualElement _root;

        private List<VisualElement> _activeTabElements = new List<VisualElement>();
        private int _selectedElementIndex = -1;
        private ScrollView _settingsScrollView;
        private const float ScrollSpeed = 450f;
        private bool _scrollFocused;

        // Overlays
        private VisualElement _pauseOverlay;
        private VisualElement _parametersOverlay;

        // Pause buttons
        private List<Button> _pauseButtons;
        private int _selectedPauseIndex;

        // Parameters tabs + panels
        private Button[] _tabButtons;
        private VisualElement[] _tabPanels;
        private int _activeTab;

        // Settings elements
        private Slider _sliderVolume, _sliderBrightness, _sliderContrast, _sliderGamma;
        private Label _labelVolume, _labelBrightness, _labelContrast, _labelGamma;
        private DropdownField _dropdownResolution;
        private Toggle _toggleFullscreen;

        // Narrative elements
        private Label _narrativeLabel, _creditsLabel;

        // URP post-processing
        private ColorAdjustments _colorAdjustments;
        private LiftGammaGain _liftGammaGain;

        // State flags
        private bool _isPaused;
        private bool _parametersOpen;
        private bool _standaloneMode;  // true = opened from title, no pause underneath

        // Navigation cooldown (unscaled)
        private float _navCooldown;
        private const float NavInterval = 0.18f;

        // PlayerPrefs keys
        private const string PrefVolume = "SH_Volume";
        private const string PrefBrightness = "SH_Brightness";
        private const string PrefContrast = "SH_Contrast";
        private const string PrefGamma = "SH_Gamma";
        private const string PrefResolution = "SH_Resolution";
        private const string PrefFullscreen = "SH_Fullscreen";

        // Resolution presets
        private static readonly (int w, int h, string label)[] Resolutions =
        {
            (1280,  720,  "1280 × 720"),
            (1920, 1080,  "1920 × 1080"),
            (2560, 1440,  "2560 × 1440"),
            (3840, 2160,  "3840 × 2160"),
        };

        // ── Unity lifecycle ────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _document = GetComponent<UIDocument>();
        }

        private void Start()
        {
            if (_document?.rootVisualElement == null) return;
            _root = _document.rootVisualElement;

            CacheElements();
            BindButtons();
            LoadSettings();
            ApplyNarrativeContent();
            ResolvePostProcessing();

            Show(_pauseOverlay, false);
            Show(_parametersOverlay, false);
        }

        private void Update()
        {
            if (_standaloneMode && EscapePressed())
            {
                HandleEscape();
                return;
            }

            if (!_isPaused && !_standaloneMode) return;

            _navCooldown -= Time.unscaledDeltaTime;

            if (_parametersOpen)
            {
                if (_navCooldown <= 0f) PollTabNavigation();
            }
            else if (_isPaused)
            {
                if (_navCooldown <= 0f) PollPauseNavigation();
                if (ConfirmPressed()) { ConfirmPauseSelection(); }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Public API ─────────────────────────────────────────────────────

        /// <summary> Called by SH_UIBridge on Escape when no other overlay is open. </summary>
        public void TogglePause()
        {
            if (_parametersOpen) { CloseParameters(); return; }
            if (_isPaused) Resume(); else OpenPause();
        }

        /// <summary> Opens Parameters without pausing — for use from the title screen. </summary>
        public void OpenStandalone()
        {
            _standaloneMode = true;
            Show(_pauseOverlay, false);
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }
            OpenParameters();
        }

        // ── Pause actions ──────────────────────────────────────────────────

        private void OpenPause()
        {
            _isPaused = true;
            _standaloneMode = false;
            Time.timeScale = 0f;
            _selectedPauseIndex = 0;
            RefreshPauseSelection();
            Show(_pauseOverlay, true);
        }

        private void Resume()
        {
            _isPaused = false;
            Time.timeScale = 1f;
            Show(_pauseOverlay, false);
        }

        private void Retry()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void ExitGame()
        {
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ── Parameters actions ─────────────────────────────────────────────

        private void OpenParameters()
        {
            _parametersOpen = true;
            _activeTab = 0;
            SwitchTab(0);
            Show(_pauseOverlay, false);
            Show(_parametersOverlay, true);
        }

        private void CloseParameters()
        {
            _parametersOpen = false;
            PlayerPrefs.Save();
            Show(_parametersOverlay, false);

            if (_standaloneMode)
            {
                _standaloneMode = false;
                var titleController = GameObject.Find("MainCanvas");
                if (titleController != null && UnityEngine.EventSystems.EventSystem.current != null)
                {
                    var btn = titleController.GetComponentInChildren<UnityEngine.UI.Button>();
                    if (btn != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(btn.gameObject);
                }

                return;
            }

            Show(_pauseOverlay, true);
            RefreshPauseSelection();
        }

        private void SwitchTab(int index)
        {
            _activeTab = Mathf.Clamp(index, 0, _tabButtons.Length - 1);
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                _tabButtons[i]?.EnableInClassList("pm-tab--active", i == _activeTab);
                Show(_tabPanels[i], i == _activeTab);
            }

            _activeTabElements.Clear();
            _selectedElementIndex = -1;

            if (_activeTab == 0)
            {
                if (_settingsScrollView != null) _activeTabElements.Add(_settingsScrollView);
                if (_sliderVolume != null) _activeTabElements.Add(_sliderVolume);
                if (_sliderBrightness != null) _activeTabElements.Add(_sliderBrightness);
                if (_sliderContrast != null) _activeTabElements.Add(_sliderContrast);
                if (_sliderGamma != null) _activeTabElements.Add(_sliderGamma);
                if (_dropdownResolution != null) _activeTabElements.Add(_dropdownResolution);
                if (_toggleFullscreen != null) _activeTabElements.Add(_toggleFullscreen);
            }

            UpdateElementVisuals();
        }

        // ── Navigation ─────────────────────────────────────────────────────

        private void PollPauseNavigation()
        {
            float v = VerticalAxis();
            if (Mathf.Approximately(v, 0f)) return;

            int dir = v > 0f ? -1 : 1;
            _selectedPauseIndex = Mathf.Clamp(
                _selectedPauseIndex + dir, 0, _pauseButtons.Count - 1);
            RefreshPauseSelection();
            _navCooldown = NavInterval;
        }

        private void PollTabNavigation()
        {
            ProcessRightStickScroll();

            float v = VerticalAxis();
            int h = HorizontalAxisInt();

            if (!Mathf.Approximately(v, 0f))
            {
                int dir = v > 0f ? -1 : 1;
                int nextIndex = _selectedElementIndex + dir;
                if (nextIndex >= -1 && nextIndex < _activeTabElements.Count)
                {
                    _selectedElementIndex = nextIndex;
                    UpdateElementVisuals();
                    EnsureSelectedElementIsVisible();
                    _navCooldown = NavInterval;
                }
                return;
            }

            if (h != 0)
            {
                if (_selectedElementIndex == -1)
                {
                    SwitchTab(_activeTab + h);
                    _navCooldown = NavInterval;
                }
                else
                {
                    ModifySelectedElementValue(h);
                    _navCooldown = NavInterval;
                }
                return;
            }

            if (ConfirmPressed() && _selectedElementIndex >= 0)
            {
                ExecuteElementAction();
                _navCooldown = NavInterval;
            }
        }

        private void ProcessRightStickScroll()
        {
            if (_tabPanels == null || _activeTab < 0 || _activeTab >= _tabPanels.Length) return;

            var scroll = _tabPanels[_activeTab] as ScrollView ?? _tabPanels[_activeTab]?.Q<ScrollView>();
            if (scroll == null) return;

            float rightStickY = 0f;
            if (Gamepad.current != null)
            {
                rightStickY = Gamepad.current.rightStick.y.ReadValue();
            }

            if (Mathf.Abs(rightStickY) > 0.15f)
            {
                Vector2 offset = scroll.scrollOffset;
                offset.y -= rightStickY * ScrollSpeed * Time.unscaledDeltaTime;
                scroll.scrollOffset = offset;
            }
        }

        private void RefreshPauseSelection()
        {
            for (int i = 0; i < _pauseButtons.Count; i++)
                _pauseButtons[i]?.EnableInClassList("pm-btn--selected", i == _selectedPauseIndex);
        }

        private void ConfirmPauseSelection()
        {
            switch (_selectedPauseIndex)
            {
                case 0: Resume(); break;
                case 1: Retry(); break;
                case 2: OpenParameters(); break;
                case 3: ExitGame(); break;
            }
        }

        private void ModifySelectedElementValue(int direction)
        {
            if (_selectedElementIndex < 0 || _selectedElementIndex >= _activeTabElements.Count) return;

            VisualElement currentElement = _activeTabElements[_selectedElementIndex];

            if (currentElement is ScrollView) return;

            if (currentElement is Slider slider)
            {
                float step = (slider.highValue - slider.lowValue) * 0.05f;
                slider.value = Mathf.Clamp(slider.value + (direction * step), slider.lowValue, slider.highValue);
            }
            else if (currentElement is DropdownField dropdown)
            {
                int nextIdx = Mathf.Clamp(dropdown.index + direction, 0, dropdown.choices.Count - 1);
                if (dropdown.index != nextIdx)
                {
                    dropdown.index = nextIdx;
                    dropdown.value = dropdown.choices[nextIdx];

                    using (var changeEvent = ChangeEvent<string>.GetPooled(dropdown.choices[dropdown.index], dropdown.choices[nextIdx]))
                    {
                        changeEvent.target = dropdown;
                        dropdown.SendEvent(changeEvent);
                    }
                }
            }
        }

        private void ExecuteElementAction()
        {
            if (_selectedElementIndex < 0 || _selectedElementIndex >= _activeTabElements.Count) return;

            VisualElement currentElement = _activeTabElements[_selectedElementIndex];

            if (currentElement is Toggle toggle)
            {
                bool newValue = !toggle.value;
                toggle.value = newValue;

                using (var changeEvent = ChangeEvent<bool>.GetPooled(!newValue, newValue))
                {
                    changeEvent.target = toggle;
                    toggle.SendEvent(changeEvent);
                }
            }
        }

        private void EnsureSelectedElementIsVisible()
        {
            if (_settingsScrollView == null || _selectedElementIndex <= 0) return;

            VisualElement target = _activeTabElements[_selectedElementIndex];
            _settingsScrollView.ScrollTo(target);
        }

        private void HandleEscape()
        {
            if (_parametersOpen) { CloseParameters(); return; }
            if (_isPaused) { Resume(); return; }
            if (_standaloneMode) { CloseParameters(); return; }
            OpenPause();
        }

        // ── Input helpers ──────────────────────────────────────────────────

        private static bool EscapePressed()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return true;
            if (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame) return true;
            return false;
        }

        private static bool ConfirmPressed()
        {
            if (Keyboard.current != null &&
                (Keyboard.current.enterKey.wasPressedThisFrame ||
                 Keyboard.current.spaceKey.wasPressedThisFrame ||
                 Keyboard.current.eKey.wasPressedThisFrame))
                return true;
            if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame) return true;
            return false;
        }

        private static float VerticalAxis()
        {
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) return 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) return -1f;
            }
            if (Gamepad.current != null)
            {
                float v = Gamepad.current.leftStick.y.ReadValue();
                if (Mathf.Abs(v) > 0.4f) return v;
                if (Gamepad.current.dpad.up.isPressed) return 1f;
                if (Gamepad.current.dpad.down.isPressed) return -1f;
            }
            return 0f;
        }

        private static int HorizontalAxisInt()
        {
            if (Keyboard.current != null)
            {
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) return 1;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) return -1;
            }
            if (Gamepad.current != null)
            {
                float h = Gamepad.current.leftStick.x.ReadValue();
                if (h > 0.4f) return 1;
                if (h < -0.4f) return -1;
                if (Gamepad.current.dpad.right.isPressed) return 1;
                if (Gamepad.current.dpad.left.isPressed) return -1;
            }
            return 0;
        }

        // ── Element caching ────────────────────────────────────────────────

        private void CacheElements()
        {
            _pauseOverlay = _root.Q<VisualElement>("pause-overlay");
            _parametersOverlay = _root.Q<VisualElement>("parameters-overlay");
            _settingsScrollView = _root.Q<ScrollView>("panel-settings");

            var btnResume = _root.Q<Button>("btn-resume");
            var btnRetry = _root.Q<Button>("btn-retry");
            var btnParameters = _root.Q<Button>("btn-parameters");
            var btnExit = _root.Q<Button>("btn-exit");
            _pauseButtons = new List<Button> { btnResume, btnRetry, btnParameters, btnExit };

            _tabButtons = new Button[]
            {
                _root.Q<Button>("tab-settings-btn"),
                _root.Q<Button>("tab-controls-btn"),
                _root.Q<Button>("tab-narrative-btn"),
            };

            _tabPanels = new VisualElement[]
            {
                _root.Q<VisualElement>("panel-settings"),
                _root.Q<VisualElement>("panel-controls"),
                _root.Q<VisualElement>("panel-narrative"),
            };

            _sliderVolume = _root.Q<Slider>("slider-volume");
            _sliderBrightness = _root.Q<Slider>("slider-brightness");
            _sliderContrast = _root.Q<Slider>("slider-contrast");
            _sliderGamma = _root.Q<Slider>("slider-gamma");
            _labelVolume = _root.Q<Label>("label-volume");
            _labelBrightness = _root.Q<Label>("label-brightness");
            _labelContrast = _root.Q<Label>("label-contrast");
            _labelGamma = _root.Q<Label>("label-gamma");
            _dropdownResolution = _root.Q<DropdownField>("dropdown-resolution");
            _toggleFullscreen = _root.Q<Toggle>("toggle-fullscreen");
            _narrativeLabel = _root.Q<Label>("narrative-text");
            _creditsLabel = _root.Q<Label>("credits-text");

            // Populate resolution dropdown
            if (_dropdownResolution != null)
            {
                _dropdownResolution.choices = new List<string>();
                foreach (var r in Resolutions) _dropdownResolution.choices.Add(r.label);
            }
        }

        // ── Button binding ─────────────────────────────────────────────────

        private void BindButtons()
        {
            _pauseButtons[0]?.RegisterCallback<ClickEvent>(_ => Resume());
            _pauseButtons[1]?.RegisterCallback<ClickEvent>(_ => Retry());
            _pauseButtons[2]?.RegisterCallback<ClickEvent>(_ => OpenParameters());
            _pauseButtons[3]?.RegisterCallback<ClickEvent>(_ => ExitGame());

            for (int i = 0; i < _tabButtons.Length; i++)
            {
                int captured = i;
                _tabButtons[i]?.RegisterCallback<ClickEvent>(_ => SwitchTab(captured));
            }

            _root.Q<Button>("parameters-close-btn")
                 ?.RegisterCallback<ClickEvent>(_ => CloseParameters());

            // Volume
            _sliderVolume?.RegisterValueChangedCallback(evt =>
            {
                AudioListener.volume = evt.newValue;
                if (_labelVolume != null) _labelVolume.text = $"{Mathf.RoundToInt(evt.newValue * 100)}%";
                PlayerPrefs.SetFloat(PrefVolume, evt.newValue);
            });

            // Brightness
            _sliderBrightness?.RegisterValueChangedCallback(evt =>
            {
                ApplyBrightness(evt.newValue);
                if (_labelBrightness != null) _labelBrightness.text = $"{evt.newValue:F1}";
                PlayerPrefs.SetFloat(PrefBrightness, evt.newValue);
            });

            // Contrast
            _sliderContrast?.RegisterValueChangedCallback(evt =>
            {
                ApplyContrast(evt.newValue);
                if (_labelContrast != null) _labelContrast.text = $"{Mathf.RoundToInt(evt.newValue)}";
                PlayerPrefs.SetFloat(PrefContrast, evt.newValue);
            });

            // Gamma
            _sliderGamma?.RegisterValueChangedCallback(evt =>
            {
                ApplyGamma(evt.newValue);
                if (_labelGamma != null) _labelGamma.text = $"{evt.newValue:F1}";
                PlayerPrefs.SetFloat(PrefGamma, evt.newValue);
            });

            // Resolution
            _dropdownResolution?.RegisterValueChangedCallback(evt =>
            {
                int idx = _dropdownResolution.index;
                if (idx >= 0 && idx < Resolutions.Length)
                {
                    var r = Resolutions[idx];
                    Screen.SetResolution(r.w, r.h, Screen.fullScreen);
                    PlayerPrefs.SetInt(PrefResolution, idx);
                }
            });

            // Fullscreen
            _toggleFullscreen?.RegisterValueChangedCallback(evt =>
            {
                Screen.fullScreen = evt.newValue;
                PlayerPrefs.SetInt(PrefFullscreen, evt.newValue ? 1 : 0);
            });
        }

        // ── Settings ───────────────────────────────────────────────────────

        private void LoadSettings()
        {
            float volume = PlayerPrefs.GetFloat(PrefVolume, 1f);
            float brightness = PlayerPrefs.GetFloat(PrefBrightness, 0f);
            float contrast = PlayerPrefs.GetFloat(PrefContrast, 0f);
            float gamma = PlayerPrefs.GetFloat(PrefGamma, 1f);
            int resIdx = PlayerPrefs.GetInt(PrefResolution, 1);
            bool fullscreen = PlayerPrefs.GetInt(PrefFullscreen, 1) == 1;

            AudioListener.volume = volume;
            SetSlider(_sliderVolume, volume, _labelVolume,
                $"{Mathf.RoundToInt(volume * 100)}%");

            ApplyBrightness(brightness);
            SetSlider(_sliderBrightness, brightness, _labelBrightness, $"{brightness:F1}");

            ApplyContrast(contrast);
            SetSlider(_sliderContrast, contrast, _labelContrast,
                $"{Mathf.RoundToInt(contrast)}");

            ApplyGamma(gamma);
            SetSlider(_sliderGamma, gamma, _labelGamma, $"{gamma:F1}");

            if (_dropdownResolution != null && resIdx < Resolutions.Length)
                _dropdownResolution.SetValueWithoutNotify(Resolutions[resIdx].label);

            _toggleFullscreen?.SetValueWithoutNotify(fullscreen);
        }

        private static void SetSlider(Slider slider, float value, Label label, string text)
        {
            slider?.SetValueWithoutNotify(value);
            if (label != null) label.text = text;
        }

        private void ApplyNarrativeContent()
        {
            if (_narrativeLabel != null) _narrativeLabel.text = _narrativeText;
            if (_creditsLabel != null) _creditsLabel.text = _creditsText;
        }

        // ── Post-processing ────────────────────────────────────────────────

        private void ResolvePostProcessing()
        {
            if (_postProcessVolume == null)
                _postProcessVolume = FindFirstObjectByType<Volume>();

            if (_postProcessVolume == null) return;

            _postProcessVolume.profile.TryGet(out _colorAdjustments);
            _postProcessVolume.profile.TryGet(out _liftGammaGain);
        }

        private void ApplyBrightness(float value)
        {
            if (_colorAdjustments == null) return;
            _colorAdjustments.postExposure.Override(value);
        }

        private void ApplyContrast(float value)
        {
            if (_colorAdjustments == null) return;
            _colorAdjustments.contrast.Override(value);
        }

        private void ApplyGamma(float value)
        {
            // LiftGammaGain.gamma is a Vector4 where the w channel is the
            // overall gamma offset. URP centers at 0 (value 1.0 → offset 0).
            if (_liftGammaGain == null) return;
            float offset = value - 1f;
            _liftGammaGain.gamma.Override(new Vector4(1f, 1f, 1f, offset));
        }

        // ── Utility ────────────────────────────────────────────────────────

        private void UpdateElementVisuals()
        {
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                _tabButtons[i]?.EnableInClassList("pm-tab--selected", _selectedElementIndex == -1 && i == _activeTab);
            }

            for (int i = 0; i < _activeTabElements.Count; i++)
            {
                _activeTabElements[i]?.EnableInClassList("pm-element--selected", i == _selectedElementIndex);
            }
        }

        private static void Show(VisualElement el, bool visible)
        {
            if (el == null) return;
            el.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}