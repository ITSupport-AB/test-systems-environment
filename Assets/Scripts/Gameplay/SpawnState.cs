// ============================================================================
// SpawnState.cs - Spawns the player and any initial setup.
// Part of Freebuff Desktop (Unity GaaS).
// ============================================================================

using UnityEngine;

namespace Freebuff.Gameplay
{
    /// <summary>
    /// First state in the gameplay loop. Spawns the player at the start
    /// position, resets wave counters, then transitions to WaveState.
    /// </summary>
    public class SpawnState : IGameState
    {
        private readonly GameplayContext _ctx;
        private readonly GameplayStateMachine _fsm;
        private GameObject _playerInstance;

        /// <summary>Event fired when the player is spawned and ready.</summary>
        public System.Action<GameObject> OnPlayerSpawned;

        public SpawnState(GameplayContext ctx, GameplayStateMachine fsm)
        {
            _ctx = ctx;
            _fsm = fsm;
        }

        /// <summary>The spawned player GameObject, available after Enter().</summary>
        public GameObject PlayerInstance => _playerInstance;

        public void Enter()
        {
            Debug.Log("[SpawnState] Spawning player...");

            // Reset context for a fresh run.
            _ctx.CurrentWave = 0;
            _ctx.Score = 0;
            _ctx.EnemiesAlive = 0;
            _ctx.IsGameOver = false;

            // Instantiate player at spawn point.
            if (_ctx.PlayerPrefab != null && _ctx.PlayerSpawnPoint != null)
            {
                _playerInstance = Object.Instantiate(
                    _ctx.PlayerPrefab,
                    _ctx.PlayerSpawnPoint.position,
                    _ctx.PlayerSpawnPoint.rotation);
            }
            else
            {
                Debug.LogWarning("[SpawnState] PlayerPrefab or SpawnPoint is null.");
            }

            OnPlayerSpawned?.Invoke(_playerInstance);

            // Immediately transition to the first wave.
            _fsm.TransitionTo<WaveState>();
        }

        public void Exit() { }

        public void Tick(float deltaTime) { }

        public void FixedTick(float fixedDeltaTime) { }
    }
}
