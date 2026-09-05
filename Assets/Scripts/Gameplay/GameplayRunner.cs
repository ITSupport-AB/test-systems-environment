// ============================================================================
// GameplayRunner.cs - MonoBehaviour that owns the FSM and Unity lifecycle.
// Part of Freebuff Desktop (Unity GaaS).
//
// Attach to a GameObject in the Gameplay scene. Assign inspector fields,
// then call StartGame() when the player presses Play.
// ============================================================================

using UnityEngine;
using Freebuff.Core;

namespace Freebuff.Gameplay
{
    /// <summary>
    /// Bridges Unity MonoBehaviour lifecycle to the GameplayStateMachine.
    /// Owns the context, states, and player controller reference.
    /// </summary>
    public class GameplayRunner : MonoBehaviour
    {
        // --- Inspector ---
        [Header("Prefabs & Spawn Points")]
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private Transform playerSpawnPoint;
        [SerializeField] private Transform[] enemySpawnPoints;
        [SerializeField] private GameObject enemyPrefab;

        [Header("References")]
        [SerializeField] private PlayerController playerController;

        // --- State Machine ---
        private GameplayStateMachine _fsm;
        private GameplayContext _ctx;
        private SpawnState _spawnState;
        private WaveState _waveState;
        private GameOverState _gameOverState;

        // --- Public Accessors ---
        public GameplayStateMachine FSM => _fsm;
        public GameplayContext Context => _ctx;
        public int CurrentScore => _ctx?.Score ?? 0;
        public int CurrentWave => _ctx?.CurrentWave ?? 0;

        // --- Lifecycle ---

        private void Awake()
        {
            InitializeStateMachine();
        }

        private void Update()
        {
            _fsm?.Tick(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            _fsm?.FixedTick(Time.fixedDeltaTime);
        }

        private void OnEnable()
        {
            if (playerController != null)
                playerController.OnRestart += HandleRestart;
        }

        private void OnDisable()
        {
            if (playerController != null)
                playerController.OnRestart -= HandleRestart;
        }

        // --- Setup ---

        private void InitializeStateMachine()
        {
            _ctx = new GameplayContext
            {
                PlayerPrefab = playerPrefab,
                PlayerSpawnPoint = playerSpawnPoint,
                EnemySpawnPoints = enemySpawnPoints,
                EnemyPrefab = enemyPrefab,
            };

            _fsm = new GameplayStateMachine();
            _spawnState = new SpawnState(_ctx, _fsm);
            _waveState = new WaveState(_ctx, _fsm);
            _gameOverState = new GameOverState(_ctx, _fsm);

            _fsm.RegisterState(_spawnState);
            _fsm.RegisterState(_waveState);
            _fsm.RegisterState(_gameOverState);
        }

        // --- Public API ---

        /// <summary>Start a new game. Call from UI "Play" button.</summary>
        public void StartGame()
        {
            Debug.Log("[GameplayRunner] Starting new game.");
            _fsm.Reset();
            _fsm.TransitionTo<SpawnState>();
        }

        /// <summary>Signal that the player has died. Called by health system.</summary>
        public void TriggerGameOver()
        {
            if (_ctx != null)
                _ctx.IsGameOver = true;
        }

        // --- Event Handlers ---

        private void HandleRestart()
        {
            if (_fsm.CurrentState is GameOverState goState)
            {
                goState.RequestRestart();
            }
        }
    }
}
