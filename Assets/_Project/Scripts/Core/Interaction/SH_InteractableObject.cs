using Core;
using Game.Interaction.Data;
using Game.World;
using System;
using UnityEngine;

namespace Game.Interaction
{
    /// <summary>
    /// Abstract base class for all interactable world entities in Escape Horizon.
    /// Implements the shared lifecycle state management, dirty flag tracking for
    /// persistence (GDD §5.2.4), and focus visual feedback scaffolding.
    ///
    /// Concrete implementations: SH_CaptiveCore, SH_ScrapPile.
    /// Future implementations: SH_LootCrate, SH_ProgressionMechanism,
    /// SH_DockingStation (GDD §5.2.1).
    ///
    /// Responsibility boundaries:
    ///   - OWNS: Availability state and dirty flag.
    ///   - OWNS: Focus enter/exit and interruption scaffolding.
    ///   - OWNS: Unique persistent ID (set via Inspector or procedurally).
    ///   - DOES NOT OWN: Hold timer logic (SH_InteractionController).
    ///   - DOES NOT OWN: Resource delivery (SH_ResourceSystem via concrete types).
    ///   - DOES NOT OWN: Serialization (future SH_PersistenceManager, GDD §5.2.4).
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class SH_InteractableObject : MonoBehaviour, IInteractable
    {
        #region Serialized Identity & Configuration

        [Header("Interactable Identity")]

        [Tooltip("Unique identifier for this object's persistent state. " +
                 "Format: {Class}_{SectorID}_{UniqueNumber} (GDD §5.2.4). " +
                 "Example: 'NC_A01_001', 'SC_A01_025'. " +
                 "Must be unique within the scene. Set manually or procedurally at spawn.")]
        [SerializeField] protected string persistentID = "UNSET_ID";

        [Tooltip("Interaction type for this object instance. " +
                 "Determines whether SH_InteractionController uses a hold or press. " +
                 "Override in concrete classes with a fixed default.")]
        [SerializeField] protected InteractionType interactionType = InteractionType.Hold;

        [Header("Proximity Highlight")]
        [Tooltip("Material applied when Bear is within interaction range.")]
        [SerializeField] protected Material _focusMaterial;

        #endregion

        #region Runtime State

        /// <summary>
        /// Whether this object has been interacted with and is no longer available.
        /// Serialized to the save file via the dirty flag system (GDD §5.2.4).
        /// </summary>
        protected bool _isAvailable = true;

        /// <summary>
        /// Dirty flag for the persistence system. Set to true on any state change
        /// that must be serialized. Reset after save. (GDD §5.2.4)
        /// </summary>
        protected bool _isDirty = false;


        /// <summary>
        /// Whether this object is currently focused by SH_InteractionController.
        /// Guards against redundant OnFocusEnter/Exit calls.
        /// </summary>
        private bool _isFocused = false;

        private SH_ScannableObject _scannable;

        #endregion

        #region State Management API
        public bool IsFocused => _isFocused;
        public Material FocusMaterial => _focusMaterial;

        #endregion

        #region Events

        /// <summary>
        /// Fired when this object's availability state changes.
        /// Parameters: (string persistentID, bool newAvailability).
        /// Consumed by: SH_PersistenceManager (future), UI prompt system.
        /// </summary>
        public event Action<string, bool> OnAvailabilityChanged;

        #endregion

        #region IInteractable Implementation

        /// <inheritdoc/>
        public InteractionType InteractionType => interactionType;

        /// <inheritdoc/>
        public Vector3 WorldPosition => transform.position;

        /// <inheritdoc/>
        public bool IsAvailable => _isAvailable;

        /// <inheritdoc/>
        public abstract void Interact(SH_PlayerContext context);

        /// <inheritdoc/>
        public virtual void OnFocusEnter()
        {
            _isFocused = true;
            
            if (_scannable == null) return;
            
            if (_focusMaterial != null && _scannable.IsDetected == true)
            {
                _scannable.ChangeMaterial(false);
            }
        }

        /// <inheritdoc/>
        public virtual void OnFocusExit()
        {
            _isFocused = false;

            if (_scannable == null) return;

            if (_scannable.IsDetected == true)
            {
                _scannable.ChangeMaterial(false);
            }
            else
            {
                _scannable.ChangeMaterial(true);
            }
            
        }

        /// <inheritdoc/>
        public void OnInteractionInterrupted()
        {
            OnInterruptedInternal();
        }

        #endregion

        #region Protected Lifecycle Hooks

        /// <summary>
        /// Called once when this object receives focus from SH_InteractionController.
        /// Override to activate highlight, prompt, or audio feedback.
        /// </summary>
        protected virtual void OnFocusEnterInternal()
        {
            
        }

        /// <summary>
        /// Called once when this object loses focus.
        /// Override to deactivate highlight, prompt, or audio feedback.
        /// </summary>
        protected virtual void OnFocusExitInternal()
        {
            
        }

        /// <summary>
        /// Called when an in-progress hold interaction is interrupted.
        /// Override to reset radial progress bar or audio feedback.
        /// </summary>
        protected virtual void OnInterruptedInternal()
        {
            // Base: no-op. Override in concrete types.
        }

        #endregion

        #region State Management API

        /// <summary>
        /// Marks this object as consumed (interaction resolved).
        /// Sets _isAvailable to false, raises the dirty flag,
        /// and fires OnAvailabilityChanged for listeners.
        /// Call from Interact() in concrete implementations
        /// after successfully delivering rewards.
        /// </summary>
        protected void MarkConsumed()
        {
            if (!_isAvailable) return;

            _isAvailable = false;
            _isDirty = true;
            OnAvailabilityChanged?.Invoke(persistentID, false);

            // Deactivate collider to prevent re-detection by the overlap scan.
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        /// <summary>
        /// Returns the persistent ID for this object.
        /// Used by the future SH_PersistenceManager to key save data (GDD §5.2.4).
        /// </summary>
        public string PersistentID => persistentID;

        /// <summary>
        /// Returns and clears the dirty flag.
        /// Called by SH_PersistenceManager when building the save delta.
        /// Returns true if this object has unsaved state changes.
        /// </summary>
        public bool ConsumeDirtyFlag()
        {
            bool wasDirty = _isDirty;
            _isDirty = false;
            return wasDirty;
        }

        /// <summary>
        /// Restores the object state from persisted data.
        /// Called by SH_PersistenceManager on scene load.
        /// Override in concrete types to restore additional state.
        /// </summary>
        /// <param name="isAvailable">
        /// Whether this object should be available after load.
        /// False means it was already consumed in a previous session.
        /// </param>
        public virtual void RestoreState(bool isAvailable)
        {
            _isAvailable = isAvailable;
            _isDirty = false;

            var col = GetComponent<Collider>();
            if (col != null) col.enabled = isAvailable;

            if (!isAvailable)
            {
                OnDestroyVisualOnLoad();
            }
        }

        /// <summary>
        /// Called by RestoreState() when loading a consumed object.
        /// Override to hide the mesh, swap to destroyed state, etc.
        /// </summary>
        protected virtual void OnDestroyVisualOnLoad() { }

        #endregion

        #region Unity Lifecycle

        protected virtual void Awake()
        {
            if (persistentID == "UNSET_ID")
            {
                Debug.LogWarning($"[SH_InteractableObject] '{gameObject.name}' has no " +
                                 $"persistent ID assigned. Persistence will not work correctly. " +
                                 $"Assign a unique ID in the Inspector (GDD §5.2.4).");
            }
        }

        protected virtual void Start()
        {
            _scannable = GetComponent<SH_ScannableObject>();
        }

        #endregion
    }
}
