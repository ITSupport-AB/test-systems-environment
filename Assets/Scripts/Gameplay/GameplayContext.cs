// ============================================================================
// GameplayContext.cs - Shared data container passed to all gameplay states.
// Part of Freebuff Desktop (Unity GaaS).
// ============================================================================

using UnityEngine;

namespace Freebuff.Gameplay
{
    /// <summary>
    /// Immutable-ish context that all gameplay states share. Avoids a god-object
    /// by giving states only what they need. Reference-typed so states can
    /// mutate score/wave counters without needing callbacks.
    /// </summary>
    public class GameplayContext
    {
        // --- Scene References (set by GameplayRunner on Awake) ---
        public Transform PlayerSpawnPoint { get; set; }
        public Transform[] EnemySpawnPoints { get; set; }
        public GameObject PlayerPrefab { get; set; }
        public GameObject EnemyPrefab { get; set; }

        // --- Runtime State (mutated by states) ---
        public int CurrentWave { get; set; }
        public int EnemiesAlive { get; set; }
        public int EnemiesKilledThisWave { get; set; }
        public int Score { get; set; }
        public int TotalEnemiesInWave { get; set; }
        public bool IsGameOver { get; set; }

        // --- Config (read from RemoteConfig) ---
        public int MaxEnemiesPerWave { get; set; } = 20;
        public float EnemySpawnRate { get; set; } = 2.0f;
        public float EnemyHealthMultiplier { get; set; } = 1.0f;
        public int XpPerWaveBase { get; set; } = 100;
        public float VipXpMultiplier { get; set; } = 1.5f;

        // --- Score Helpers ---
        public void AddKillScore(int basePoints = 100)
        {
            Score += basePoints;
            EnemiesKilledThisWave++;
            EnemiesAlive--;
        }

        public void AddWaveScore()
        {
            Score += CurrentWave * 500;
        }
    }
}
