using UnityEngine;

namespace Core.States
{
    /// <summary>
    /// Abstract base contract for the Player's Finite State Machine (FSM).
    /// Orchestrates behavioral logic by strictly decoupling input analysis, 
    /// state transition evaluation, and Newtonian physics integration.
    /// </summary>
    public abstract class SH_BaseState
    {
        // Reference to the orchestrator (FSM) and the shared Source of Truth (Context)
        protected SH_PlayerStateMachine stateMachine;
        protected SH_PlayerContext context;

        /// <summary>
        /// Initializes the state with required architectural dependencies.
        /// </summary>
        protected SH_BaseState(SH_PlayerStateMachine stateMachine, SH_PlayerContext context)
        {
            this.stateMachine = stateMachine;
            this.context = context;
        }

        #region Validation Logic

        /// <summary> Evaluates if the state can be legally activated. </summary>
        public virtual bool CanEnter() => true;

        /// <summary> Evaluates if the state can be legally interrupted or finished. </summary>
        public virtual bool CanExit() => true;

        #endregion

        #region Lifecycle Methods

        /// <summary> 
        /// Executed upon state activation. 
        /// Used for local parameter initialization and initial force application. 
        /// </summary>
        public virtual void Enter() { }

        /// <summary> 
        /// Analytical phase: Captures player intent from the InputHandler buffer. 
        /// Must remain free of transition logic to preserve determinism. 
        /// </summary>
        public virtual void HandleInput() { }

        /// <summary> 
        /// Decision-making phase: Executes per-frame logic and evaluates transition conditions. 
        /// </summary>
        public virtual void Update() { }

        /// <summary> 
        /// Execution phase: Performed at a fixed time step for Newtonian integration. 
        /// Direct interface for SH_CharacterController physical manipulations. 
        /// </summary>
        public virtual void PhysicsUpdate() { }

        /// <summary> 
        /// Cleanup phase: Executed upon state termination to reset buffers or persistent data. 
        /// </summary>
        public virtual void Exit() { }

        #endregion
    }
}