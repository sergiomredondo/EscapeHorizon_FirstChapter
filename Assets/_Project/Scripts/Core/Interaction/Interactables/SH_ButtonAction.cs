using UnityEngine;
using Core;

namespace Game.Interaction
{
    /// <summary>
    /// Generic interactable that triggers an Animator parameter on a target object.
    ///
    /// Use cases:
    /// - Doors (open/close)
    /// - Consoles (activate states)
    /// - Mechanisms (trigger sequences)
    ///
    /// Responsibility boundaries:
    ///   - OWNS: Dispatching animation events to a target Animator.
    ///   - DOES NOT OWN: Animation logic or state machine design.
    ///   - DOES NOT OWN: Interaction timing (handled by SH_InteractionController).
    /// </summary>
    public class SH_ButtonAction : SH_InteractableObject
    {
        #region Configuration

        [Header("Button Action — Target")]

        [Tooltip("Target GameObject that contains the Animator component.")]
        [SerializeField] private GameObject _targetObject;

        private Animator _targetAnimator;

        [Header("Button Action — Animator Parameter")]

        [Tooltip("Name of the parameter in the Animator.")]
        [SerializeField] private string _parameterName;

        [Tooltip("Type of the Animator parameter.")]
        [SerializeField] private AnimatorParameterType _parameterType;

        [Header("Values (used depending on type)")]

        [SerializeField] private bool _boolValue = true;
        [SerializeField] private int _intValue = 1;
        [SerializeField] private float _floatValue = 1f;

        #endregion

        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();

            if (_targetObject != null)
            {
                _targetAnimator = _targetObject.GetComponent<Animator>();

                if (_targetAnimator == null)
                {
                    Debug.LogError($"[SH_ButtonAction] '{persistentID}': Target object has no Animator.");
                }
            }
            else
            {
                Debug.LogWarning($"[SH_ButtonAction] '{persistentID}': No target object assigned.");
            }
        }

        #endregion

        #region Interaction Logic

        public override void Interact(SH_PlayerContext context)
        {
            if (!_isAvailable)
            {
                Debug.LogWarning($"[SH_ButtonAction] '{persistentID}' already used.");
                return;
            }

            if (_targetAnimator == null)
            {
                Debug.LogError($"[SH_ButtonAction] '{persistentID}': Animator not available.");
                return;
            }

            if (string.IsNullOrEmpty(_parameterName))
            {
                Debug.LogError($"[SH_ButtonAction] '{persistentID}': Parameter name is empty.");
                return;
            }

            ExecuteAnimatorAction();

            // Dependiendo del diseño:
            // - Si es reusable, NO llamar MarkConsumed()
            // - Si es de un solo uso, activarlo:
            // MarkConsumed();
        }

        #endregion

        #region Animator Dispatch

        private void ExecuteAnimatorAction()
        {
            switch (_parameterType)
            {
                case AnimatorParameterType.Bool:
                    _targetAnimator.SetBool(_parameterName, _boolValue);
                    break;

                case AnimatorParameterType.Int:
                    _targetAnimator.SetInteger(_parameterName, _intValue);
                    break;

                case AnimatorParameterType.Float:
                    _targetAnimator.SetFloat(_parameterName, _floatValue);
                    break;

                case AnimatorParameterType.Trigger:
                    _targetAnimator.SetTrigger(_parameterName);
                    break;

                default:
                    Debug.LogError($"[SH_ButtonAction] Unsupported parameter type.");
                    break;
            }
        }

        #endregion

        #region Editor Validation

        private void OnValidate()
        {
            if (_targetObject != null)
            {
                var animator = _targetObject.GetComponent<Animator>();
                if (animator == null)
                {
                    Debug.LogWarning($"[SH_ButtonAction] Assigned target has no Animator.");
                }
            }
        }

        #endregion
    }

    public enum AnimatorParameterType
    {
        Bool,
        Int,
        Float,
        Trigger
    }
}