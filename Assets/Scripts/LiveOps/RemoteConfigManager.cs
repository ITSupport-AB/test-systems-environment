// ============================================================================
// RemoteConfigManager.cs - Firebase Remote Config wrapper for LiveOps.
// Part of Freebuff Desktop (Unity GaaS).
//
// Allows the team to change subscription price points, seasonal event
// durations, XP multipliers, ad frequencies, and any other tunable without
// shipping a new app build.
//
// Usage:
//   await RemoteConfigManager.Instance.InitializeAsync();
//   float price = RemoteConfigManager.Instance.GetFloat("subscription_price_usd");
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Freebuff.LiveOps
{
    /// <summary>
    /// Singleton that fetches, caches, and exposes Firebase Remote Config values.
    /// Includes a local JSON defaults file so the app always has a baseline even
    /// before the first network fetch.
    /// </summary>
    public class RemoteConfigManager : MonoBehaviour
    {
        // --- Singleton ---
        public static RemoteConfigManager Instance { get; private set; }

        // --- Events ---
        /// <summary>Fired after a successful fetch + activate.</summary>
        public event Action OnConfigUpdated;

        // --- Constants ---
        private static readonly TimeSpan MinFetchInterval = TimeSpan.FromMinutes(30);

        // --- State ---
        private DateTime _lastFetchUtc = DateTime.MinValue;
        private bool _initialized;

        // --- Default Values ---
        private static readonly Dictionary<string, object> Defaults = new()
        {
            ["subscription_price_usd"]          = 4.99,
            ["battlepass_price_usd"]            = 9.99,
            ["ad_frequency_seconds"]            = 30,
            ["battlepass_xp_multiplier"]        = 1.0,
            ["vip_xp_multiplier"]               = 1.5,
            ["xp_per_wave_base"]                = 100,
            ["seasonal_event_enabled"]          = false,
            ["seasonal_event_duration_hours"]   = 72,
            ["seasonal_event_id"]               = "none",
            ["seasonal_event_reward_id"]        = "none",
            ["enemy_spawn_rate_base"]           = 2.0f,
            ["enemy_health_multiplier"]         = 1.0f,
            ["max_enemies_per_wave"]            = 20,
            ["feature_new_ui_enabled"]          = false,
            ["feature_battle_royale_enabled"]   = false,
        };

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

        public async Task InitializeAsync()
        {
            if (_initialized) return;
            try
            {
                var rc = Firebase.RemoteConfig.FirebaseRemoteConfig.Instance;
                var settings = new Firebase.RemoteConfig.ConfigSettings
                {
                    FetchTimeoutInMilliseconds = 60_000,
                    MinimumFetchIntervalInMilliseconds = (ulong)MinFetchInterval.TotalMilliseconds,
                };
                await rc.SetConfigSettingsAsync(settings);
                await rc.SetDefaultsAsync(Defaults);
                LoadJsonDefaults(rc);
                var info = await rc.FetchAndActivateAsync();
                Debug.Log($"[RemoteConfig] Fetch status: {info}");
                _lastFetchUtc = DateTime.UtcNow;
                _initialized = true;
                OnConfigUpdated?.Invoke();
                Debug.Log("[RemoteConfig] Initialized and activated.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RemoteConfig] Init failed: {ex.Message}");
                _initialized = true;
            }
        }

        public async Task ForceFetchAsync()
        {
            try
            {
                var rc = Firebase.RemoteConfig.FirebaseRemoteConfig.Instance;
                await rc.FetchAndActivateAsync();
                _lastFetchUtc = DateTime.UtcNow;
                OnConfigUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RemoteConfig] Force fetch failed: {ex.Message}");
            }
        }

        // --- Typed Getters ---

        public float GetFloat(string key)
        {
            var val = GetValue(key);
            if (val.DoubleValue != 0.0) return (float)val.DoubleValue;
            return val.LongValue;
        }

        public int GetInt(string key) => (int)GetValue(key).LongValue;
        public long GetLong(string key) => GetValue(key).LongValue;
        public string GetString(string key) => GetValue(key).StringValue ?? string.Empty;
        public bool GetBool(string key) => GetValue(key).BooleanValue;

        public T Get<T>(string key, T fallback)
        {
            try
            {
                var val = GetValue(key);
                object raw = val.ValueType == Firebase.RemoteConfig.ConfigValueType.Value
                    ? val.StringValue : val.DoubleValue.ToString();
                return (T)Convert.ChangeType(raw, typeof(T));
            }
            catch { return fallback; }
        }

        // --- Convenience Getters ---
        public float SubscriptionPrice => GetFloat("subscription_price_usd");
        public float VipXpMultiplier => GetFloat("vip_xp_multiplier");
        public bool IsSeasonalEventActive => GetBool("seasonal_event_enabled");
        public float SeasonalEventDurationHours => GetFloat("seasonal_event_duration_hours");
        public float AdFrequencySeconds => GetFloat("ad_frequency_seconds");

        // --- Private Helpers ---

        private Firebase.RemoteConfig.ConfigValue GetValue(string key)
        {
            try { return Firebase.RemoteConfig.FirebaseRemoteConfig.Instance.GetValue(key); }
            catch (KeyNotFoundException)
            {
                Debug.LogWarning($"[RemoteConfig] Key '{key}' not found.");
                return default;
            }
        }

        private void LoadJsonDefaults(Firebase.RemoteConfig.FirebaseRemoteConfig rc)
        {
            var textAsset = Resources.Load<TextAsset>("RemoteConfigDefaults");
            if (textAsset == null) return;
            try
            {
                var dict = Newtonsoft.Json.JsonConvert.DeserializeObject
                    <Dictionary<string, object>>(textAsset.text);
                if (dict == null) return;
                var typedDefaults = new Dictionary<string, object>();
                foreach (var kvp in dict)
                    typedDefaults[kvp.Key] = kvp.Value?.ToString() ?? "";
                rc.SetDefaultsAsync(typedDefaults);
                Debug.Log($"[RemoteConfig] Loaded {typedDefaults.Count} defaults from JSON.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteConfig] JSON defaults parse error: {ex.Message}");
            }
        }
    }
}
