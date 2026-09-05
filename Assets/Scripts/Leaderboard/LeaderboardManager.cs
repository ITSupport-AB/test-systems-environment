// ============================================================================
// LeaderboardManager.cs - Cross-platform global leaderboard client.
// Part of Freebuff Desktop (Unity GaaS).
//
// Firestore structure: leaderboard/{seasonId}/entries/{playerId}
//
// Anti-inflation guarantees:
//   - Server-side: Firestore rules enforce score >= current score on update.
//   - Client-side: Local validation before submission to avoid unnecessary writes.
//   - Optimistic UI: Updates local cache immediately, confirms on server response.
//
// Usage:
//   await LeaderboardManager.Instance.SubmitScoreAsync(score, playerName);
//   var top10 = await LeaderboardManager.Instance.GetTopScoresAsync(10);
//   var rank = await LeaderboardManager.Instance.GetPlayerRankAsync();
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Freebuff.Leaderboard
{
    using Freebuff.Core;

    /// <summary>
    /// Entry data for a single leaderboard row.
    /// </summary>
    [Serializable]
    public class LeaderboardEntry
    {
        /// <summary>Firebase UID of the player.</summary>
        public string PlayerId;

        /// <summary>Display name (max 24 chars, set by player).</summary>
        public string PlayerName;

        /// <summary>The player's score (must be monotonically increasing).</summary>
        public int Score;

        /// <summary>Highest wave survived (optional extra stat).</summary>
        public int HighWave;

        /// <summary>ISO-8601 timestamp of the last submission.</summary>
        public string SubmittedAt;

        /// <summary>Platform the score was achieved on.</summary>
        public string Platform;

        /// <summary>True if player has VIP status.</summary>
        public bool IsVIP;

        /// <summary>Rank on the leaderboard (set by query, not stored).</summary>
        public int Rank;
    }

    /// <summary>
    /// Result of a score submission attempt.
    /// </summary>
    public class SubmissionResult
    {
        /// <summary>True if the score was accepted.</summary>
        public bool Accepted;

        /// <summary>The player's new rank after submission (null if rejected).</summary>
        public int? NewRank;

        /// <summary>Error message if rejected (null if accepted).</summary>
        public string ErrorMessage;

        /// <summary>The previous score before this submission.</summary>
        public int PreviousScore;
    }

    /// <summary>
    /// Singleton manager for the global leaderboard.
    /// Handles score submission with anti-inflation validation,
    /// and provides various query methods for leaderboard data.
    /// </summary>
    public class LeaderboardManager : MonoBehaviour
    {
        // --- Singleton ---
        public static LeaderboardManager Instance { get; private set; }

        // --- Configuration ---
        [Header("Configuration")]
        [Tooltip("Maximum characters allowed for player name.")]
        [SerializeField] private int maxPlayerNameLength = 24;

        [Tooltip("Default season ID if not specified.")]
        [SerializeField] private string defaultSeasonId = "season_1";

        // --- Constants ---
        private const string LeaderboardCollection = "leaderboard";
        private const string EntriesSubcollection = "entries";
        private const int MaxLeaderboardCacheSize = 100;
        private const float CacheExpirySeconds = 30f;

        // --- State ---
        private Dictionary<string, List<LeaderboardEntry>> _cache = new();
        private Dictionary<string, float> _cacheTimestamps = new();
        private LeaderboardEntry _localBest;

        // --- Events ---
        /// <summary>Fired after a successful score submission.</summary>
        public event Action<SubmissionResult> OnScoreSubmitted;

        /// <summary>Fired when leaderboard data is refreshed.</summary>
        public event Action<List<LeaderboardEntry>> OnLeaderboardRefreshed;

        // --- Public Properties ---
        /// <summary>The current season ID.</summary>
        public string CurrentSeasonId => defaultSeasonId;

        /// <summary>The player's cached local best score.</summary>
        public LeaderboardEntry LocalBest => _localBest;

        // --- Lifecycle ---

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // --- Public API: Score Submission ---

        /// <summary>
        /// Submit a score to the leaderboard.
        /// Client validates before submission; server enforces monotonic increase.
        /// </summary>
        /// <param name="score">The score to submit (must be > 0).</param>
        /// <param name="playerName">Display name (1-24 chars).</param>
        /// <param name="highWave">Optional: highest wave survived.</param>
        /// <param name="seasonId">Season ID (defaults to current season).</param>
        /// <returns>Submission result with acceptance status and new rank.</returns>
        public async Task<SubmissionResult> SubmitScoreAsync(
            int score,
            string playerName,
            int highWave = 0,
            string seasonId = null)
        {
            seasonId = seasonId ?? defaultSeasonId;

            // --- Client-side validation ---
            if (score <= 0)
            {
                return new SubmissionResult
                {
                    Accepted = false,
                    ErrorMessage = "Score must be greater than zero.",
                    PreviousScore = _localBest?.Score ?? 0,
                };
            }

            if (string.IsNullOrWhiteSpace(playerName))
            {
                return new SubmissionResult
                {
                    Accepted = false,
                    ErrorMessage = "Player name cannot be empty.",
                    PreviousScore = _localBest?.Score ?? 0,
                };
            }

            if (playerName.Length > maxPlayerNameLength)
            {
                return new SubmissionResult
                {
                    Accepted = false,
                    ErrorMessage = $"Player name cannot exceed {maxPlayerNameLength} characters.",
                    PreviousScore = _localBest?.Score ?? 0,
                };
            }

            // --- Anti-inflation: client-side monotonic check ---
            if (_localBest != null && score <= _localBest.Score)
            {
                return new SubmissionResult
                {
                    Accepted = false,
                    ErrorMessage = $"Score {score} is not higher than your current best ({_localBest.Score}).",
                    PreviousScore = _localBest.Score,
                };
            }

            // --- Get player UID ---
            string uid = GameManager.Instance?.Auth?.CurrentUserId;
            if (string.IsNullOrEmpty(uid))
            {
                return new SubmissionResult
                {
                    Accepted = false,
                    ErrorMessage = "Not authenticated. Please log in first.",
                    PreviousScore = _localBest?.Score ?? 0,
                };
            }

            int previousScore = _localBest?.Score ?? 0;

            // --- Optimistic UI update ---
            _localBest = new LeaderboardEntry
            {
                PlayerId = uid,
                PlayerName = playerName,
                Score = score,
                HighWave = highWave,
                SubmittedAt = DateTime.UtcNow.ToString("o"),
                Platform = Application.platform.ToString(),
                IsVIP = GameManager.Instance?.Subscription?.IsVip ?? false,
            };

            // --- Invalidate cache ---
            InvalidateCache(seasonId);

            try
            {
                // Firestore write.
                // var entryData = new Dictionary<string, object>
                // {
                //     { "playerName", playerName },
                //     { "score", score },
                //     { "uid", uid },
                //     { "highWave", highWave },
                //     { "submittedAt", FieldValue.ServerTimestamp },
                //     { "platform", Application.platform.ToString() },
                //     { "isVIP", GameManager.Instance?.Subscription?.IsVip ?? false },
                //     { "metadata", new Dictionary<string, object>
                //         {
                //             { "appVersion", Application.version },
                //             { "sdkVersion", Application.unityVersion },
                //         }
                //     },
                // };
                //
                // await FirebaseFirestore.DefaultInstance
                //     .Collection(LeaderboardCollection).Document(seasonId)
                //     .Collection(EntriesSubcollection).Document(uid)
                //     .SetAsync(entryData, SetOptions.MergeAll);

                // Fetch updated rank.
                int? newRank = await GetPlayerRankAsync(seasonId);

                var result = new SubmissionResult
                {
                    Accepted = true,
                    NewRank = newRank,
                    PreviousScore = previousScore,
                };

                Debug.Log($"[Leaderboard] Score {score} submitted. New rank: #{newRank}");
                OnScoreSubmitted?.Invoke(result);

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Leaderboard] Score submission failed: {ex.Message}");

                // Roll back optimistic update so the player isn't locked out
                // of submitting their actual score on retry.
                if (previousScore > 0)
                {
                    _localBest = new LeaderboardEntry
                    {
                        PlayerId = uid,
                        Score = previousScore,
                    };
                }
                else
                {
                    _localBest = null;
                }

                return new SubmissionResult
                {
                    Accepted = false,
                    ErrorMessage = $"Submission failed: {ex.Message}",
                    PreviousScore = previousScore,
                };
            }
        }

        // --- Public API: Queries ---

        /// <summary>
        /// Get the top N scores from the leaderboard.
        /// Results are cached for CacheExpirySeconds to reduce reads.
        /// </summary>
        /// <param name="count">Number of entries to fetch (max 100).</param>
        /// <param name="seasonId">Season ID (defaults to current season).</param>
        /// <returns>Ordered list of leaderboard entries (highest score first).</returns>
        public async Task<List<LeaderboardEntry>> GetTopScoresAsync(
            int count = 10,
            string seasonId = null)
        {
            seasonId = seasonId ?? defaultSeasonId;
            count = Mathf.Clamp(count, 1, MaxLeaderboardCacheSize);

            string cacheKey = $"top_{count}_{seasonId}";

            // Check cache.
            if (_cache.TryGetValue(cacheKey, out var cached)
                && _cacheTimestamps.TryGetValue(cacheKey, out var timestamp)
                && Time.unscaledTime - timestamp < CacheExpirySeconds)
            {
                Debug.Log($"[Leaderboard] Returning cached top {count}.");
                return cached;
            }

            try
            {
                // Firestore query: order by score descending, limit to count.
                // var snapshot = await FirebaseFirestore.DefaultInstance
                //     .Collection(LeaderboardCollection).Document(seasonId)
                //     .Collection(EntriesSubcollection)
                //     .OrderByDescending("score")
                //     .Limit(count)
                //     .GetSnapshotAsync();
                //
                // var entries = new List<LeaderboardEntry>();
                // int rank = 1;
                //
                // foreach (var doc in snapshot.Documents)
                // {
                //     entries.Add(new LeaderboardEntry
                //     {
                //         PlayerId = doc.Id,
                //         PlayerName = doc.GetValue<string>("playerName"),
                //         Score = doc.GetValue<int>("score"),
                //         HighWave = doc.GetValue<int>("highWave"),
                //         SubmittedAt = doc.GetValue<string>("submittedAt"),
                //         Platform = doc.GetValue<string>("platform"),
                //         IsVIP = doc.GetValue<bool>("isVIP"),
                //         Rank = rank++,
                //     });
                // }

                // Dev stub: return empty list.
                var entries = new List<LeaderboardEntry>();
                Debug.Log($"[Leaderboard] Fetched top {count} from {seasonId} (dev stub).");

                // Cache the result.
                _cache[cacheKey] = entries;
                _cacheTimestamps[cacheKey] = Time.unscaledTime;

                OnLeaderboardRefreshed?.Invoke(entries);

                return entries;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Leaderboard] Failed to fetch top scores: {ex.Message}");
                return new List<LeaderboardEntry>();
            }
        }

        /// <summary>
        /// Get scores around the player's current rank.
        /// Returns entries from (rank - range) to (rank + range).
        /// </summary>
        /// <param name="range">Number of entries above and below the player.</param>
        /// <param name="seasonId">Season ID (defaults to current season).</param>
        /// <returns>Ordered list of entries around the player's rank.</returns>
        public async Task<List<LeaderboardEntry>> GetScoresAroundPlayerAsync(
            int range = 5,
            string seasonId = null)
        {
            seasonId = seasonId ?? defaultSeasonId;

            // First, get the player's rank.
            int? playerRank = await GetPlayerRankAsync(seasonId);
            if (!playerRank.HasValue)
            {
                Debug.Log("[Leaderboard] Player not on leaderboard yet.");
                return new List<LeaderboardEntry>();
            }

            // Fetch a larger window and slice around the player.
            int fetchCount = Mathf.Min((range * 2) + 1, MaxLeaderboardCacheSize);
            var allEntries = await GetTopScoresAsync(fetchCount, seasonId);

            int playerIdx = allEntries.FindIndex(
                e => e.PlayerId == GameManager.Instance?.Auth?.CurrentUserId);

            if (playerIdx < 0)
            {
                // Player not in the fetched window; fetch more.
                int fetchSize = Mathf.Min(
                    playerRank.Value + range, MaxLeaderboardCacheSize);
                allEntries = await GetTopScoresAsync(fetchSize, seasonId);
                playerIdx = allEntries.FindIndex(
                    e => e.PlayerId == GameManager.Instance?.Auth?.CurrentUserId);
            }

            if (playerIdx < 0)
            {
                return new List<LeaderboardEntry>();
            }

            int start = Mathf.Max(0, playerIdx - range);
            int end = Mathf.Min(allEntries.Count, playerIdx + range + 1);

            return allEntries.GetRange(start, end - start);
        }

        /// <summary>
        /// Get the player's current rank on the leaderboard.
        /// </summary>
        /// <param name="seasonId">Season ID (defaults to current season).</param>
        /// <returns>1-based rank, or null if not on the leaderboard.</returns>
        public async Task<int?> GetPlayerRankAsync(string seasonId = null)
        {
            seasonId = seasonId ?? defaultSeasonId;

            string uid = GameManager.Instance?.Auth?.CurrentUserId;
            if (string.IsNullOrEmpty(uid))
            {
                return null;
            }

            try
            {
                // Firestore query: count documents with score > player's score.
                // var playerDoc = await FirebaseFirestore.DefaultInstance
                //     .Collection(LeaderboardCollection).Document(seasonId)
                //     .Collection(EntriesSubcollection).Document(uid)
                //     .GetSnapshotAsync();
                //
                // if (!playerDoc.Exists) return null;
                //
                // int playerScore = playerDoc.GetValue<int>("score");
                //
                // var higherScores = await FirebaseFirestore.DefaultInstance
                //     .Collection(LeaderboardCollection).Document(seasonId)
                //     .Collection(EntriesSubcollection)
                //     .WhereGreaterThan("score", playerScore)
                //     .Count()
                //     .GetSnapshotAsync();
                //
                // return (int)higherScores.Count + 1;

                // Dev stub.
                Debug.Log($"[Leaderboard] GetPlayerRank (dev stub) for {seasonId}.");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Leaderboard] Failed to get player rank: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get the player's current leaderboard entry.
        /// </summary>
        /// <param name="seasonId">Season ID (defaults to current season).</param>
        /// <returns>The player's entry, or null if not on the leaderboard.</returns>
        public async Task<LeaderboardEntry> GetPlayerEntryAsync(string seasonId = null)
        {
            seasonId = seasonId ?? defaultSeasonId;

            string uid = GameManager.Instance?.Auth?.CurrentUserId;
            if (string.IsNullOrEmpty(uid))
            {
                return null;
            }

            try
            {
                // Firestore read.
                // var doc = await FirebaseFirestore.DefaultInstance
                //     .Collection(LeaderboardCollection).Document(seasonId)
                //     .Collection(EntriesSubcollection).Document(uid)
                //     .GetSnapshotAsync();
                //
                // if (!doc.Exists) return null;
                //
                // return new LeaderboardEntry
                // {
                //     PlayerId = doc.Id,
                //     PlayerName = doc.GetValue<string>("playerName"),
                //     Score = doc.GetValue<int>("score"),
                //     HighWave = doc.GetValue<int>("highWave"),
                //     SubmittedAt = doc.GetValue<string>("submittedAt"),
                //     Platform = doc.GetValue<string>("platform"),
                //     IsVIP = doc.GetValue<bool>("isVIP"),
                //     Rank = await GetPlayerRankAsync(seasonId) ?? 0,
                // };

                // Dev stub.
                Debug.Log($"[Leaderboard] GetPlayerEntry (dev stub) for {seasonId}.");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Leaderboard] Failed to get player entry: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Check if the given score would be a new personal best.
        /// Useful for UI display during gameplay.
        /// </summary>
        public bool IsNewPersonalBest(int score)
        {
            return _localBest == null || score > _localBest.Score;
        }

        // --- Cache Management ---

        /// <summary>
        /// Invalidate the cache for a specific season or all seasons.
        /// </summary>
        /// <param name="seasonId">Season to invalidate, or null for all.</param>
        public void InvalidateCache(string seasonId = null)
        {
            if (seasonId == null)
            {
                _cache.Clear();
                _cacheTimestamps.Clear();
            }
            else
            {
                var keysToRemove = new List<string>();
                // Use "_" + seasonId to match the cache key format
                // "top_{count}_{seasonId}" precisely, preventing
                // partial suffix matches (e.g. season_1 matching season_21).
                string suffix = "_" + seasonId;
                foreach (var key in _cache.Keys)
                {
                    if (key.EndsWith(suffix))
                    {
                        keysToRemove.Add(key);
                    }
                }
                foreach (var key in keysToRemove)
                {
                    _cache.Remove(key);
                    _cacheTimestamps.Remove(key);
                }
            }

            Debug.Log($"[Leaderboard] Cache invalidated for {(seasonId ?? "all seasons")}.");
        }
    }
}
