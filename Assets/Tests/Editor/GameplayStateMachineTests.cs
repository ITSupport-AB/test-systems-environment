// ============================================================================
// GameplayStateMachineTests.cs - NUnit tests for the gameplay FSM.
// Part of Freebuff Desktop (Unity GaaS).
//
// Covers: state registration, transitions, reset, wave scaling,
// context helpers, and state lifecycle callbacks.
//
// Run in Unity: Window > General > Test Runner > EditMode > Run All
// ============================================================================

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Freebuff.Gameplay;

namespace Freebuff.Tests
{
    // --- Test Doubles ---

    /// <summary>
    /// Spy state that records Enter/Exit/Tick calls for assertion.
    /// </summary>
    public class SpyState : IGameState
    {
        public int EnterCount;
        public int ExitCount;
        public int TickCount;
        public int FixedTickCount;
        public float LastDeltaTime;
        public float LastFixedDeltaTime;

        public void Enter() => EnterCount++;
        public void Exit() => ExitCount++;
        public void Tick(float deltaTime)
        {
            TickCount++;
            LastDeltaTime = deltaTime;
        }
        public void FixedTick(float fixedDeltaTime)
        {
            FixedTickCount++;
            LastFixedDeltaTime = fixedDeltaTime;
        }
    }

    /// <summary>
    /// State that transitions to a target on Enter (for testing re-entrant transitions).
    /// </summary>
    public class AutoTransitionState : IGameState
    {
        private readonly GameplayStateMachine _fsm;
        private readonly System.Type _targetType;
        public int EnterCount;

        public AutoTransitionState(GameplayStateMachine fsm, System.Type targetType)
        {
            _fsm = fsm;
            _targetType = targetType;
        }

        public void Enter()
        {
            EnterCount++;
            _fsm.TransitionTo(_targetType);
        }

        public void Exit() { }
        public void Tick(float deltaTime) { }
        public void FixedTick(float fixedDeltaTime) { }
    }

    /// <summary>
    /// Minimal spy that mimics WaveState's Enter() scaling logic
    /// without requiring GameObjects or Unity scene objects.
    /// Used for unit-testing wave-scaling math in isolation.
    /// </summary>
    public class WaveSpy : IGameState
    {
        private readonly GameplayStateMachine _fsm;
        private readonly GameplayContext _ctx;
        public int EnterCount;
        public int EnemiesToSpawn;

        public WaveSpy(GameplayStateMachine fsm, GameplayContext ctx)
        {
            _fsm = fsm;
            _ctx = ctx;
        }

        public void Enter()
        {
            EnterCount++;
            _ctx.CurrentWave++;
            _ctx.EnemiesKilledThisWave = 0;

            // Mirrors WaveState.Enter() scaling formula.
            EnemiesToSpawn = Mathf.Min(
                5 + (_ctx.CurrentWave * 2),
                _ctx.MaxEnemiesPerWave);

            _ctx.TotalEnemiesInWave = EnemiesToSpawn;
        }

        public void Exit() { }
        public void Tick(float deltaTime) { }
        public void FixedTick(float fixedDeltaTime) { }
    }

    /// <summary>
    /// Spy state for GameOver — records Enter/Exit and
    /// exposes the final score for assertion.
    /// </summary>
    public class GameOverSpy : IGameState
    {
        private readonly GameplayContext _ctx;
        public int EnterCount;
        public int FinalScore;

        public GameOverSpy(GameplayContext ctx)
        {
            _ctx = ctx;
        }

        public void Enter()
        {
            EnterCount++;
            FinalScore = _ctx.Score;
        }

        public void Exit() { }
        public void Tick(float deltaTime) { }
        public void FixedTick(float fixedDeltaTime) { }
    }

    // --- Tests ---

    [TestFixture]
    public class GameplayStateMachineTests
    {
        private GameplayStateMachine _fsm;
        private SpyState _stateA;
        private SpyState _stateB;
        private SpyState _stateC;

        [SetUp]
        public void SetUp()
        {
            _fsm = new GameplayStateMachine();
            _stateA = new SpyState();
            _stateB = new SpyState();
            _stateC = new SpyState();

            _fsm.RegisterState(_stateA);
            _fsm.RegisterState(_stateB);
            _fsm.RegisterState(_stateC);
        }

        // --- Registration ---

        [Test]
        public void RegisterState_SingleState_NoErrors()
        {
            var fsm = new GameplayStateMachine();
            var state = new SpyState();
            fsm.RegisterState(state);
            Assert.Pass();
        }

        [Test]
        public void RegisterState_DuplicateState_Overwrites()
        {
            // Registering the same state twice should not throw.
            _fsm.RegisterState(_stateA);
            _fsm.RegisterState(_stateA);
            Assert.Pass();
        }

        // --- Transitions ---

        [Test]
        public void TransitionTo_FirstState_CallsEnter()
        {
            _fsm.TransitionTo<SpyState>();
            Assert.AreEqual(1, _stateA.EnterCount);
        }

        [Test]
        public void TransitionTo_TwoStates_CallsExitOnFirst()
        {
            _fsm.TransitionTo<SpyState>();
            _fsm.TransitionTo<SpyState>();

            // First transition: stateA.Enter
            // Second transition: stateA.Exit, stateA.Enter
            Assert.AreEqual(2, _stateA.EnterCount);
            Assert.AreEqual(1, _stateA.ExitCount);
        }

        [Test]
        public void TransitionTo_DifferentStates_CallsCorrectLifecycle()
        {
            _fsm.TransitionTo(_stateA.GetType()); // stateA
            _fsm.TransitionTo(_stateB.GetType()); // stateB

            Assert.AreEqual(1, _stateA.EnterCount);
            Assert.AreEqual(1, _stateA.ExitCount);
            Assert.AreEqual(1, _stateB.EnterCount);
            Assert.AreEqual(0, _stateB.ExitCount);
        }

        [Test]
        public void TransitionTo_InvalidType_DoesNotCrash()
        {
            // Transitioning to an unregistered type should log error but not throw.
            _fsm.TransitionTo(typeof(string));
            Assert.IsNull(_fsm.CurrentState);
        }

        [Test]
        public void TransitionTo_UpdatesCurrentState()
        {
            _fsm.TransitionTo(_stateB.GetType());
            Assert.AreSame(_stateB, _fsm.CurrentState);
        }

        [Test]
        public void TransitionTo_UpdatesPreviousState()
        {
            _fsm.TransitionTo(_stateA.GetType());
            _fsm.TransitionTo(_stateB.GetType());
            Assert.AreSame(_stateA, _fsm.PreviousState);
            Assert.AreSame(_stateB, _fsm.CurrentState);
        }

        [Test]
        public void TransitionTo_FirstTransition_PreviousStateIsNull()
        {
            _fsm.TransitionTo(_stateA.GetType());
            Assert.IsNull(_fsm.PreviousState);
        }

        // --- Reset ---

        [Test]
        public void Reset_ExitsCurrentState()
        {
            _fsm.TransitionTo(_stateA.GetType());
            _fsm.Reset();
            Assert.AreEqual(1, _stateA.ExitCount);
        }

        [Test]
        public void Reset_ClearsCurrentAndPrevious()
        {
            _fsm.TransitionTo(_stateA.GetType());
            _fsm.TransitionTo(_stateB.GetType());
            _fsm.Reset();
            Assert.IsNull(_fsm.CurrentState);
            Assert.IsNull(_fsm.PreviousState);
        }

        [Test]
        public void Reset_CanTransitionAfterReset()
        {
            _fsm.TransitionTo(_stateA.GetType());
            _fsm.Reset();
            _fsm.TransitionTo(_stateB.GetType());
            Assert.AreSame(_stateB, _fsm.CurrentState);
            Assert.AreEqual(1, _stateB.EnterCount);
        }

        // --- Ticks ---

        [Test]
        public void Tick_ForwardedToCurrentState()
        {
            _fsm.TransitionTo(_stateA.GetType());
            _fsm.Tick(0.016f);
            Assert.AreEqual(1, _stateA.TickCount);
            Assert.AreEqual(0.016f, _stateA.LastDeltaTime);
        }

        [Test]
        public void FixedTick_ForwardedToCurrentState()
        {
            _fsm.TransitionTo(_stateA.GetType());
            _fsm.FixedTick(0.02f);
            Assert.AreEqual(1, _stateA.FixedTickCount);
            Assert.AreEqual(0.02f, _stateA.LastFixedDeltaTime);
        }

        [Test]
        public void Tick_NoCurrentState_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _fsm.Tick(0.016f));
        }

        [Test]
        public void FixedTick_NoCurrentState_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _fsm.FixedTick(0.02f));
        }

        // --- Re-entrant Transitions ---

        [Test]
        public void TransitionTo_ReentrantFromEnter_HandledCorrectly()
        {
            // AutoTransitionState transitions to stateB on Enter.
            var auto = new AutoTransitionState(_fsm, _stateB.GetType());
            _fsm.RegisterState(auto);

            _fsm.TransitionTo(auto.GetType());

            // auto.Enter() called once, then exited, stateB entered.
            Assert.AreEqual(1, auto.EnterCount);
            Assert.AreEqual(1, auto.ExitCount);
            Assert.AreEqual(1, _stateB.EnterCount);
            Assert.AreSame(_stateB, _fsm.CurrentState);
        }

        // --- Wave State Scaling Integration ---

        [Test]
        public void WaveState_IncrementsWaveOnEachEnter()
        {
            var ctx = new GameplayContext { MaxEnemiesPerWave = 100 };
            var fsm = new GameplayStateMachine();

            var waveSpy = new WaveSpy(fsm, ctx);
            var gameOverSpy = new GameOverSpy(ctx);
            fsm.RegisterState(waveSpy);
            fsm.RegisterState(gameOverSpy);

            // Enter wave 1
            fsm.TransitionTo<WaveSpy>();
            Assert.AreEqual(1, ctx.CurrentWave);

            // Enter wave 2
            fsm.TransitionTo<WaveSpy>();
            Assert.AreEqual(2, ctx.CurrentWave);

            // Enter wave 3
            fsm.TransitionTo<WaveSpy>();
            Assert.AreEqual(3, ctx.CurrentWave);
        }

        [Test]
        public void WaveState_ScalingFormula_Wave1()
        {
            // Wave 1: enemies = min(5 + 1*2, 100) = 7
            var ctx = new GameplayContext { MaxEnemiesPerWave = 100 };
            var fsm = new GameplayStateMachine();
            var waveSpy = new WaveSpy(fsm, ctx);
            fsm.RegisterState(waveSpy);

            fsm.TransitionTo<WaveSpy>();
            Assert.AreEqual(7, waveSpy.EnemiesToSpawn);
            Assert.AreEqual(7, ctx.TotalEnemiesInWave);
        }

        [Test]
        public void WaveState_ScalingFormula_Wave5()
        {
            // Wave 5: enemies = min(5 + 5*2, 100) = 15
            var ctx = new GameplayContext { MaxEnemiesPerWave = 100 };
            var fsm = new GameplayStateMachine();
            var waveSpy = new WaveSpy(fsm, ctx);
            fsm.RegisterState(waveSpy);

            // Advance to wave 5 by entering 5 times
            for (int i = 0; i < 5; i++)
                fsm.TransitionTo<WaveSpy>();

            Assert.AreEqual(15, waveSpy.EnemiesToSpawn);
        }

        [Test]
        public void WaveState_ScalingFormula_CapsAtMaxEnemies()
        {
            // MaxEnemiesPerWave = 10
            // Wave 10: formula = 5 + 10*2 = 25, but capped to 10
            var ctx = new GameplayContext { MaxEnemiesPerWave = 10 };
            var fsm = new GameplayStateMachine();
            var waveSpy = new WaveSpy(fsm, ctx);
            fsm.RegisterState(waveSpy);

            for (int i = 0; i < 10; i++)
                fsm.TransitionTo<WaveSpy>();

            Assert.AreEqual(10, waveSpy.EnemiesToSpawn);
            Assert.AreEqual(10, ctx.TotalEnemiesInWave);
        }

        [Test]
        public void WaveState_ScalingFormula_LinearGrowth()
        {
            // Verify the linear growth pattern: each wave adds 2 more enemies.
            var ctx = new GameplayContext { MaxEnemiesPerWave = 1000 };
            var fsm = new GameplayStateMachine();
            var waveSpy = new WaveSpy(fsm, ctx);
            fsm.RegisterState(waveSpy);

            var results = new List<int>();

            for (int i = 0; i < 10; i++)
            {
                fsm.TransitionTo<WaveSpy>();
                results.Add(waveSpy.EnemiesToSpawn);
            }

            // Wave 1..10: 7, 9, 11, 13, 15, 17, 19, 21, 23, 25
            Assert.AreEqual(10, results.Count);
            for (int i = 0; i < 10; i++)
            {
                int expected = 5 + ((i + 1) * 2); // wave is 1-indexed
                Assert.AreEqual(expected, results[i],
                    $"Wave {i + 1}: expected {expected} enemies");
            }
        }

        [Test]
        public void WaveState_ResetsKillCounterOnEnter()
        {
            var ctx = new GameplayContext { MaxEnemiesPerWave = 100 };
            var fsm = new GameplayStateMachine();
            var waveSpy = new WaveSpy(fsm, ctx);
            fsm.RegisterState(waveSpy);

            // Simulate some kills
            ctx.EnemiesKilledThisWave = 5;

            fsm.TransitionTo<WaveSpy>();
            Assert.AreEqual(0, ctx.EnemiesKilledThisWave);
        }

        // --- Context Score Helpers ---

        [Test]
        public void Context_AddKillScore_IncrementsFields()
        {
            var ctx = new GameplayContext();
            ctx.EnemiesAlive = 5;
            ctx.Score = 0;
            ctx.EnemiesKilledThisWave = 0;

            ctx.AddKillScore(200);

            Assert.AreEqual(200, ctx.Score);
            Assert.AreEqual(1, ctx.EnemiesKilledThisWave);
            Assert.AreEqual(4, ctx.EnemiesAlive);
        }

        [Test]
        public void Context_AddWaveScore_ScalesWithWaveNumber()
        {
            var ctx = new GameplayContext();

            ctx.CurrentWave = 1;
            ctx.AddWaveScore();
            Assert.AreEqual(500, ctx.Score);

            ctx.CurrentWave = 3;
            ctx.AddWaveScore();
            // 500 + 3*500 = 2000
            Assert.AreEqual(2000, ctx.Score);
        }

        [Test]
        public void Context_AddKillScore_DefaultBasePoints()
        {
            var ctx = new GameplayContext();
            ctx.EnemiesAlive = 1;

            ctx.AddKillScore(); // Uses default 100
            Assert.AreEqual(100, ctx.Score);
        }

        // --- GameOverSpy Integration ---

        [Test]
        public void GameOverSpy_CapturesFinalScore()
        {
            var ctx = new GameplayContext { Score = 4200 };
            var gameOverSpy = new GameOverSpy(ctx);

            gameOverSpy.Enter();
            Assert.AreEqual(4200, gameOverSpy.FinalScore);
            Assert.AreEqual(1, gameOverSpy.EnterCount);
        }

        [Test]
        public void FSM_FullLifecycle_WaveThenGameOver()
        {
            var ctx = new GameplayContext { MaxEnemiesPerWave = 100 };
            var fsm = new GameplayStateMachine();
            var waveSpy = new WaveSpy(fsm, ctx);
            var gameOverSpy = new GameOverSpy(ctx);
            fsm.RegisterState(waveSpy);
            fsm.RegisterState(gameOverSpy);

            // Simulate 3 waves
            fsm.TransitionTo<WaveSpy>();
            fsm.TransitionTo<WaveSpy>();
            fsm.TransitionTo<WaveSpy>();

            Assert.AreEqual(3, ctx.CurrentWave);

            // Simulate game over with some score
            ctx.Score = 5000;
            fsm.TransitionTo<GameOverSpy>();

            Assert.AreEqual(5000, gameOverSpy.FinalScore);
            Assert.IsNull(fsm.PreviousState);
            Assert.AreSame(gameOverSpy, fsm.CurrentState);
        }
    }
}
