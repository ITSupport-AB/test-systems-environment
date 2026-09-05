// ============================================================================
// WaveState.cs - Spawns enemy waves and tracks wave-clear progress.
// Part of Freebuff Desktop (Unity GaaS).
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Freebuff.Gameplay
{
    /// <summary>
    /// Core gameplay state: spawns enemies in waves, tracks kills, and
    /// transitions to the next wave or GameOverState.
    ///
    /// Scaling: each wave increases enemy count by CurrentWave * 2,
    /// health multiplier scales with wave number.
    /// </summary>
    public class WaveState : IGameState
    {
        private readonly GameplayContext _ctx;
        private readonly GameplayStateMachine _fsm;

        // --- Wave Timing ---
        private float _spawnTimer;
        private int _enemiesSpawnedThisWave;
        private int _enemiesToSpawn;

        // --- Active Enemies (for cleanup) ---
        private readonly List<GameObject> _activeEnemies = new();
        private static readonly Predicate<GameObject> IsNull = e => e == null;

        public WaveState(GameplayContext ctx, GameplayStateMachine fsm)
        {
            _ctx = ctx;
            _fsm = fsm;
        }

        public void Enter()
        {
            _ctx.CurrentWave++;
            _ctx.EnemiesKilledThisWave = 0;
            _ctx.EnemiesAlive = 0;

            // Wave scaling: more enemies each wave.
            _enemiesToSpawn = Mathf.Min(
                5 + (_ctx.CurrentWave * 2),
                _ctx.MaxEnemiesPerWave);

            _ctx.TotalEnemiesInWave = _enemiesToSpawn;
            _enemiesSpawnedThisWave = 0;
            _spawnTimer = 0f;

            Debug.Log($"[WaveState] Wave {_ctx.CurrentWave} started. " +
                      $"Spawning {_enemiesToSpawn} enemies.");
        }

        public void Exit()
        {
            Debug.Log($"[WaveState] Wave {_ctx.CurrentWave} complete. " +
                      $"Score: {_ctx.Score}");
        }

        public void Tick(float deltaTime)
        {
            // --- Spawn Enemies ---
            if (_enemiesSpawnedThisWave < _enemiesToSpawn)
            {
                _spawnTimer += deltaTime;
                float spawnInterval = _ctx.EnemySpawnRate /
                    Mathf.Max(1f, _ctx.CurrentWave * 0.5f);

                if (_spawnTimer >= spawnInterval)
                {
                    _spawnTimer = 0f;
                    SpawnEnemy();
                }
            }

            // --- Cleanup Destroyed Enemies ---
            _activeEnemies.RemoveAll(IsNull);

            // --- Check Wave Clear ---
            bool allSpawned = _enemiesSpawnedThisWave >= _enemiesToSpawn;
            bool allDead = _activeEnemies.Count == 0;

            if (allSpawned && allDead)
            {
                _ctx.AddWaveScore();
                Debug.Log($"[WaveState] Wave {_ctx.CurrentWave} cleared! " +
                          $"Total Score: {_ctx.Score}");

                // Check if game should end (e.g., player died mid-wave).
                if (_ctx.IsGameOver)
                {
                    _fsm.TransitionTo<GameOverState>();
                    return;
                }

                // Continue to next wave (transitions back to itself via Exit/Enter).
                _fsm.TransitionTo<WaveState>();
            }

            // --- Check Game Over ---
            if (_ctx.IsGameOver)
            {
                CleanupEnemies();
                _fsm.TransitionTo<GameOverState>();
            }
        }

        public void FixedTick(float fixedDeltaTime) { }

        // --- Private Helpers ---

        private void SpawnEnemy()
        {
            if (_ctx.EnemyPrefab == null || _ctx.EnemySpawnPoints == null
                || _ctx.EnemySpawnPoints.Length == 0)
            {
                Debug.LogWarning("[WaveState] Missing EnemyPrefab or SpawnPoints.");
                _enemiesSpawnedThisWave++;
                return;
            }

            // Pick a random spawn point.
            var spawnPoint = _ctx.EnemySpawnPoints[
                Random.Range(0, _ctx.EnemySpawnPoints.Length)];

            var enemy = Object.Instantiate(
                _ctx.EnemyPrefab,
                spawnPoint.position,
                spawnPoint.rotation);

            // Scale enemy health with wave number.
            // In production, you'd set a health component on the enemy prefab.
            // var health = enemy.GetComponent<EnemyHealth>();
            // if (health != null) health.MaxHealth *= _ctx.EnemyHealthMultiplier;

            _activeEnemies.Add(enemy);
            _ctx.EnemiesAlive++;
            _enemiesSpawnedThisWave++;
        }

        private void CleanupEnemies()
        {
            foreach (var enemy in _activeEnemies)
            {
                if (enemy != null) Object.Destroy(enemy);
            }
            _activeEnemies.Clear();
            _ctx.EnemiesAlive = 0;
        }
    }
}
