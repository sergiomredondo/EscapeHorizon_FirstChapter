using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.World
{
    /// <summary>
    /// Central controller for all narrative presentation channels.
    /// Manages two independent channels:
    ///   Channel A — Text log panel via UI Toolkit (SH_UIBridge reads events).
    ///   Channel B — Image sequence via Canvas Animator (already working in scene).
    ///
    /// All SH_DataLog components call this singleton.
    /// Time.timeScale and input lock are managed by SH_UIBridge on Channel A,
    /// and by this component on Channel B.
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_NarrativeSequencer : MonoBehaviour
    {
        #region Singleton

        public static SH_NarrativeSequencer Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        #endregion

        #region Inspector

        [Header("Channel B — Image Sequence")]
        [Tooltip("Root Canvas GameObject for image sequences. Enabled/disabled by this component.")]
        [SerializeField] private GameObject _imageSequenceCanvas;

        [Tooltip("Animator on the image sequence Canvas.")]
        [SerializeField] private Animator _imageSequenceAnimator;

        [Tooltip("Parameter name in the Animator that triggers a specific sequence state.")]
        [SerializeField] private string _animSequenceParam = "SequenceName";

        [Tooltip("Boolean parameter set to true while a sequence is playing. " +
                 "The Animator sets it back to false when the sequence ends.")]
        [SerializeField] private string _animPlayingParam = "IsPlaying";

        [Tooltip("Input key that skips or closes the image sequence.")]
        [SerializeField] private KeyCode _skipKey = KeyCode.E;

        #endregion

        #region Events

        // Channel A — consumed by SH_UIBridge to drive the UI Toolkit text panel.
        public static event Action<string, string, string> OnTextLogRequested;
        public static event Action OnTextLogCloseRequested;
        public static event Action OnNarrativeSequenceWillStart;
        public static event Action OnNarrativeSequenceEnded;

        #endregion

        #region Runtime State

        private bool _imageSequencePlaying;
        private int _animPlayingHash;
        private bool _initialized;

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            if (_imageSequenceAnimator != null)
                _animPlayingHash = Animator.StringToHash(_animPlayingParam);

            if (_imageSequenceCanvas != null)
                _imageSequenceCanvas.SetActive(false);

            _initialized = true;
        }

        private void Update()
        {
            if (!_imageSequencePlaying) return;

            // Poll animator to detect natural sequence end.
            bool animStillPlaying = _imageSequenceAnimator != null
                && _imageSequenceAnimator.GetBool(_animPlayingHash);

            bool playerSkipped = Keyboard.current.escapeKey.wasPressedThisFrame;

            if (!animStillPlaying || playerSkipped)
                CloseImageSequence();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Shows the text log panel via UI Toolkit.
        /// SH_UIBridge subscribes to OnTextLogRequested and handles timeScale.
        /// </summary>
        public void ShowTextLog(string title, string source, string body)
        {
            OnTextLogRequested?.Invoke(title, source, body);
        }

        /// <summary>
        /// Plays an image sequence by triggering a named state in the Canvas Animator.
        /// Pauses game time and hides the HUD canvas while playing.
        /// </summary>
        public void ShowImageSequence(string stateName)
        {
            if (!_initialized || _imageSequenceAnimator == null) return;
            if (_imageSequencePlaying) return;

            OnNarrativeSequenceWillStart?.Invoke();
            StartCoroutine(ExecuteSequenceAfterFade(stateName));

        }

        private System.Collections.IEnumerator ExecuteSequenceAfterFade(string stateName)
        {
            yield return new WaitForSecondsRealtime(3f);
            
            _imageSequencePlaying = true;
            Time.timeScale = 0f;

            if (_imageSequenceCanvas != null)
                _imageSequenceCanvas.SetActive(true);

            _imageSequenceAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
            _imageSequenceAnimator.Play(stateName);
            _imageSequenceAnimator.SetBool(_animPlayingHash, true);
        }

        /// <summary>
        /// Closes the active text log. Called by SH_UIBridge button handler
        /// or by SH_DataLog when InteractPressed fires while log is open.
        /// </summary>
        public void CloseTextLog()
        {
            OnTextLogCloseRequested?.Invoke();
        }

        #endregion

        #region Internal

        private void CloseImageSequence()
        {
            if (!_imageSequencePlaying) return;
            _imageSequencePlaying = false;

            Time.timeScale = 1f;

            if (_imageSequenceAnimator != null)
                _imageSequenceAnimator.SetBool(_animPlayingHash, false);

            if (_imageSequenceCanvas != null)
                _imageSequenceCanvas.SetActive(false);

            OnNarrativeSequenceEnded?.Invoke();
        }

        #endregion
    }
}