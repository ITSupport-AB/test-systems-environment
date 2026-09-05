// ============================================================================
// IGameState.cs — Interface for the Gameplay State Machine.
// Part of Freebuff Desktop (Unity GaaS).
// ============================================================================

namespace Freebuff.Gameplay
{
    /// <summary>
    /// Contract for every state in the gameplay FSM.
    /// States are non-MonoBehaviour plain classes for easy unit testing
    /// and dependency-free composition.
    /// </summary>
    public interface IGameState
    {
        /// <summary>Called once when transitioning INTO this state.</summary>
        void Enter();

        /// <summary>Called once when transitioning OUT of this state.</summary>
        void Exit();

        /// <summary>Called every frame while this state is active.</summary>
        /// <param name="deltaTime">Time.deltaTime forwarded from MonoBehaviour.</param>
        void Tick(float deltaTime);

        /// <summary>Called every FixedUpdate while this state is active.</summary>
        /// <param name="fixedDeltaTime">Time.fixedDeltaTime forwarded from MonoBehaviour.</param>
        void FixedTick(float fixedDeltaTime);
    }
}
