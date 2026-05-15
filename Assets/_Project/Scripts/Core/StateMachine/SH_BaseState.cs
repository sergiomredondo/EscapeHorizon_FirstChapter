using UnityEngine;

namespace Core.StateMachine
{
    /// <summary>
    /// Abstract foundation for all Mecha states within the State-driven Architecture.
    /// Implements the priority-based arbitration logic and the standard execution lifecycle.
    /// Ensures all derived states have mandatory access to the SH_PlayerContext (SSOT).
    /// </summary>
    public abstract class SH_BaseState
    {
        #region Dependencies

        /// <summary> Reference to the validated system container (SSOT) providing access to locomotion, physics, and settings. </summary>
        protected readonly SH_PlayerContext _context;

        /// <summary> Reference to the central FSM orchestrator for state transitions. </summary>
        protected readonly SH_PlayerStateMachine _stateMachine;

        #endregion

        #region Properties

        /// <summary> 
        /// Defines the state's hierarchy level. 
        /// Higher priority values allow this state to interrupt others with lower values, managed by the StateMachine.
        /// </summary>
        public abstract int Priority { get; }

        #endregion

        #region Construc

        /// <summary>
        /// Protected constructor to enforce dependency injection throughout the state hierarchy.
        /// Ensures no state exists without a valid context and machine reference.
        /// </summary>
        /// <param name="context">The Single Source of Truth for the player entity.</param>
        /// <param name="stateMachine">The owner machine managing this state's lifecycle.</param>
        protected SH_BaseState(SH_PlayerContext context, SH_PlayerStateMachine stateMachine)
        {
            if (context == null) 
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_BaseState] Construction failed: SH_PlayerContext reference is null. Ensure that a valid context is passed when instantiating states.");
#endif
                return; 
            }
            if (stateMachine == null) 
            { 
#if UNITY_EDITOR
                Debug.LogError($"[SH_BaseState] Construction failed: SH_PlayerStateMachine reference is null. Ensure that a valid state machine is passed when instantiating states.");
#endif
                return; 
            }

            _context = context;
            _stateMachine = stateMachine;
        }

        #endregion

        #region Execution Lifecycle

        /// <summary> 
        /// Called once when the StateMachine transitions into this state. 
        /// Used for initializing animations, resetting timers, or locking locomotion. 
        /// </summary>
        public virtual void Enter() { }

        /// <summary> 
        /// Virtual method for sampling input data from the SH_InputHandler before logic processing.
        /// Allows states to interpret raw input according to their specific context.
        /// </summary>
        public virtual void HandleInput() { }

        /// <summary> 
        /// Called every frame (Update) to process non-physics logic and evaluate transition conditions. 
        /// </summary>
        public virtual void Update() { }

        /// <summary> 
        /// Called during FixedUpdate for physics-related operations and kinematic calculations.
        /// </summary>
        /// <param name="dt">The fixed delta time injected by the StateMachine for deterministic physics.</param>
        public virtual void PhysicsUpdate(float dt) 
        {
            if (dt <= 0) { 
#if UNITY_EDITOR
                Debug.LogError($"[SH_BaseState] PhysicsUpdate failed: Invalid delta time (dt={dt}). Ensure that a positive fixed delta time is passed when calling PhysicsUpdate."); 
#endif
                return; 
            }
        }

        /// <summary> 
        /// Called once before the StateMachine transitions out of this state. 
        /// Used for cleaning up temporary effects, unlocking movement, or triggering exit animations. 
        /// </summary>
        public virtual void Exit() { }

        #endregion

        #region Arbitration Logic

        /// <summary>
        /// Optional validation check to determine if the state can be legally entered under current gameplay conditions.
        /// </summary>
        /// <returns>True if the transition is allowed.</returns>
        public virtual bool CanEnter() => true;

        #endregion
    }
}