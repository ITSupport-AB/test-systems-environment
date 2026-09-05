// ============================================================================
// GameOverState.cs - Handles game over screen and restart.
// Part of Freebuff Desktop (Unity GaaS).
// ============================================================================

using UnityEngine;

namespace Freebuff.Gameplay
{
    /// <summary>
    /// Entered when the player dies or fails. Shows the Game Over UI,
    /// records the score, and waits for restart input.
    ///
    /// The MonoBehaviour that owns this FSM should listen to
    /// PlayerController.OnRestart and call RequestRestart().
    /// </summary>
    public class GameOverState : IGameState
    {
        private readonly GameplayContext _ctx;
        private readonly GameplayStateMachine _fsm;

        /// <summary>Fired when GameOver is entered (for UI system to subscribe).</summary>
        public System.Action<int, int> OnGameOverEntered;

        /// <summary>Fired when the player requests restart.</summary>
        public System.Action OnRestartRequested;

        public GameOverState(GameplayContext ctx, GameplayStateMachine fsm)
        {
            _ctx = ctx;
            _fsm = fsm;
        }

        public void Enter()
        {
            Debug.Log($"[GameOverState] Game Over! Wave: {_ctx.CurrentWave}, Score: {_ctx.Score}");

            // Freeze time (optional — can be toggled for dramatic effect).
            // Time.timeScale = 0f;

            // Notify listeners (UI, analytics, etc.).
            OnGameOverEntered?.Invoke(_ctx.CurrentWave, _ctx.Score);

            // In production, save the high score here:
            // if (_ctx.Score > SaveManager.Instance.Data.HighScore)
            // {
            //     SaveManager.Instance.Data.HighScore = _ctx.Score;
            //     SaveManager.Instance.QueueSave();
            // }
        }

        public void Exit()
        {
            // Restore time if it was frozen.
            // Time.timeScale = 1f;

            // Clean up any remaining enemies.
            Debug.Log("[GameOverState] Cleaning up for restart.");
        }

        public void Tick(float deltaTime) { }

        public void FixedTick(float fixedDeltaTime) { }

        /// <summary>
        /// Call this from PlayerController.OnRestart or a UI button.
        /// Resets context and transitions back to SpawnState.
        /// </summary>
        public void RequestRestart()
        {
            OnRestartRequested?.Invoke();
            _fsm.Reset();
            _fsm.TransitionTo<SpawnState>();
        }
    }
}
