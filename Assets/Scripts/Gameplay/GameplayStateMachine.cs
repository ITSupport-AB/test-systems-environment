// ============================================================================
// GameplayStateMachine.cs - Generic FSM for the core gameplay loop.
// Part of Freebuff Desktop (Unity GaaS).
//
// Attaches to a GameplayRunner MonoBehaviour that owns Update/FixedUpdate
// and forwards ticks to the current state.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Freebuff.Gameplay
{
    /// <summary>
    /// Generic finite state machine. States are plain C# objects (not MonoBehaviours)
    /// for easy unit testing and zero GC allocation after setup.
    /// </summary>
    public class GameplayStateMachine
    {
        // --- State Registry ---
        private readonly Dictionary<Type, IGameState> _states = new();
        private IGameState _currentState;
        private IGameState _previousState;

        /// <summary>The currently active state, or null before first transition.</summary>
        public IGameState CurrentState => _currentState;

        /// <summary>The state that was active before the current one.</summary>
        public IGameState PreviousState => _previousState;

        // --- Setup ---

        /// <summary>
        /// Register all possible states. Call once during initialization.
        /// </summary>
        public void RegisterState(IGameState state)
        {
            Type type = state.GetType();
            if (_states.ContainsKey(type))
            {
                Debug.LogWarning($"[FSM] State {type.Name} already registered, overwriting.");
            }
            _states[type] = state;
        }

        // --- Transitions ---

        /// <summary>
        /// Transition to a new state by type. Calls Exit() on the old state
        /// and Enter() on the new state.
        /// </summary>
        public void TransitionTo<T>() where T : class, IGameState
        {
            Type targetType = typeof(T);

            if (!_states.TryGetValue(targetType, out IGameState nextState))
            {
                Debug.LogError($"[FSM] State {targetType.Name} not registered.");
                return;
            }

            _currentState?.Exit();
            _previousState = _currentState;
            _currentState = nextState;
            _currentState.Enter();

            Debug.Log($"[FSM] -> {targetType.Name}");
        }

        /// <summary>
        /// Transition using a Type reference (for data-driven transitions).
        /// </summary>
        public void TransitionTo(Type stateType)
        {
            if (!typeof(IGameState).IsAssignableFrom(stateType))
            {
                Debug.LogError($"[FSM] {stateType.Name} does not implement IGameState.");
                return;
            }

            if (!_states.TryGetValue(stateType, out IGameState nextState))
            {
                Debug.LogError($"[FSM] State {stateType.Name} not registered.");
                return;
            }

            _currentState?.Exit();
            _previousState = _currentState;
            _currentState = nextState;
            _currentState.Enter();

            Debug.Log($"[FSM] -> {stateType.Name}");
        }

        // --- Ticks ---

        /// <summary>Forward MonoBehaviour.Update to the current state.</summary>
        public void Tick(float deltaTime)
        {
            _currentState?.Tick(deltaTime);
        }

        /// <summary>Forward MonoBehaviour.FixedUpdate to the current state.</summary>
        public void FixedTick(float fixedDeltaTime)
        {
            _currentState?.FixedTick(fixedDeltaTime);
        }

        /// <summary>Reset the FSM (call when restarting the game).</summary>
        public void Reset()
        {
            _currentState?.Exit();
            _currentState = null;
            _previousState = null;
        }
    }
}
