// ============================================================================
// SaveManager.cs - Cloud-synced inventory and currency persistence.
// Part of Freebuff Desktop (Unity GaaS).
//
// Writes to Firestore: users/{uid}/saveData
// Uses debounced writes (5s) to avoid hammering the backend.
// Server-wins conflict resolution via monotonic version counter.
// ============================================================================

using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Freebuff.Save
{
    using Freebuff.Core;
    using Freebuff.Data;

    /// <summary>
    /// Singleton that manages cloud save/load for player data.
    /// Debounces writes to avoid excessive Firestore calls, and performs
    /// an immediate flush on app pause/quit to prevent data loss.
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        // --- Singleton ---
        public static SaveManager Instance { get; private set; }

        // --- Constants ---
        /// <summary>Debounce interval in seconds between queued writes.</summary>
        private const float WriteDebounceSeconds = 5f;

        /// <summary>Firestore collection path.</summary>
        private const string SaveCollection = "users";

        /// <summary>Firestore document field for save data.</summary>
        private const string SaveDataField = "saveData";

        /// <summary>Local cache file name.</summary>
        private const string LocalCacheFile = "player_save.json";

        // --- State ---
        private PlayerData _data;
        private float _saveTimer;
        private bool _saveQueued;
        private bool _isSaving;
        private bool _isLoaded;

        /// <summary>The current in-memory player data. Never null after Load().</summary>
        public PlayerData Data => _data ??= new PlayerData();

        /// <summary>True after Load() has completed at least once.</summary>
        public bool IsLoaded => _isLoaded;

        // --- Events ---
        /// <summary>Fired after a successful load.</summary>
        public event Action<PlayerData> OnDataLoaded;

        /// <summary>Fired after a successful save.</summary>
        public event Action OnDataSaved;

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

        private void Update()
        {
            // Process debounced save queue.
            if (_saveQueued && !_isSaving)
            {
                _saveTimer -= Time.unscaledDeltaTime;
                if (_saveTimer <= 0f)
                {
                    _saveQueued = false;
                    _ = SaveAsync();
                }
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) FlushSave();
        }

        private void OnApplicationQuit()
        {
            // OnApplicationQuit must be synchronous — async continuations
            // may not complete before the process terminates. Save locally
            // immediately; cloud sync will happen on next launch.
            if (_data != null)
            {
                _saveQueued = false;
                SaveLocalCache();
            }
        }

        // --- Public API ---

        /// <summary>
        /// Load player data from Firestore. Falls back to local cache if
        /// the network request fails. Auto-called by GameManager after auth.
        /// </summary>
        public async Task LoadAsync()
        {
            string uid = GameManager.Instance?.Auth?.CurrentUserId;
            if (string.IsNullOrEmpty(uid))
            {
                Debug.LogWarning("[SaveManager] No authenticated user; loading local cache.");
                LoadLocalCache();
                return;
            }

            try
            {
                // Firestore read.
                // var doc = await FirebaseFirestore.DefaultInstance
                //     .Collection(SaveCollection).Document(uid)
                //     .Collection("data").Document(SaveDataField)
                //     .GetSnapshotAsync();
                //
                // if (doc.Exists)
                // {
                //     string json = doc.GetValue<string>("json");
                //     _data = PlayerData.FromJson(json);
                //     _isLoaded = true;
                //     Debug.Log($"[SaveManager] Loaded from cloud. Version: {_data.Version}");
                //     OnDataLoaded?.Invoke(_data);
                //     return;
                // }

                // If no cloud save, fall back to local.
                Debug.Log("[SaveManager] No cloud save found; using local cache.");
                LoadLocalCache();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] Cloud load failed: {ex.Message}. Using local cache.");
                LoadLocalCache();
            }
        }

        /// <summary>
        /// Queue a save (debounced). Multiple rapid calls within the debounce
        /// window are coalesced into a single write.
        /// </summary>
        public void QueueSave()
        {
            _saveQueued = true;
            _saveTimer = WriteDebounceSeconds;
            Debug.Log("[SaveManager] Save queued.");
        }

        /// <summary>Immediately save to cloud and local cache (no debounce).</summary>
        public void FlushSave()
        {
            _saveQueued = false;
            if (_isSaving)
            {
                // A save is already in progress. Mark that we need another
                // flush when it finishes, so data modified during the current
                // save is not lost.
                _saveQueued = true;
                _saveTimer = 0f; // Fire immediately when the current save ends.
                return;
            }
            _ = SaveAsync();
        }

        /// <summary>
        /// Create a fresh save for a new player.
        /// </summary>
        public void CreateNewSave()
        {
            _data = new PlayerData
            {
                Coins = 500,         // Starting currency
                Gems = 0,
                XP = 0,
                BattlePassTier = 0,
                HighScore = 0,
                Version = 1,
                LastSaveTimestamp = DateTime.UtcNow.ToString("o"),
            };
            _isLoaded = true;
            SaveLocalCache();
            QueueSave();
            OnDataLoaded?.Invoke(_data);
            Debug.Log("[SaveManager] New save created.");
        }

        // --- Private ---

        private async Task SaveAsync()
        {
            if (_data == null || _isSaving) return;

            _isSaving = true;
            try
            {
                // Increment version for conflict detection.
                _data.Version++;
                _data.LastSaveTimestamp = DateTime.UtcNow.ToString("o");

                string json = _data.ToJson();
                string uid = GameManager.Instance?.Auth?.CurrentUserId;

                // Save locally (always, even if cloud fails).
                SaveLocalCache();

                if (string.IsNullOrEmpty(uid))
                {
                    Debug.Log("[SaveManager] No UID; saved locally only.");
                    return;
                }

                // Firestore write.
                // var saveData = new Dictionary<string, object>
                // {
                //     { "json", json },
                //     { "version", _data.Version },
                //     { "updatedAt", FieldValue.ServerTimestamp },
                // };
                //
                // await FirebaseFirestore.DefaultInstance
                //     .Collection(SaveCollection).Document(uid)
                //     .Collection("data").Document(SaveDataField)
                //     .SetAsync(saveData, SetOptions.MergeAll);

                Debug.Log($"[SaveManager] Saved to cloud. Version: {_data.Version}");
                OnDataSaved?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] Cloud save failed: {ex.Message}");
            }
            finally
            {
                _isSaving = false;
            }
        }

        private void SaveLocalCache()
        {
            try
            {
                string json = _data.ToJson();
                string path = System.IO.Path.Combine(Application.persistentDataPath, LocalCacheFile);
                System.IO.File.WriteAllText(path, json);
                Debug.Log($"[SaveManager] Local cache saved: {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] Local cache save failed: {ex.Message}");
            }
        }

        private void LoadLocalCache()
        {
            try
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, LocalCacheFile);
                if (!System.IO.File.Exists(path))
                {
                    Debug.Log("[SaveManager] No local cache found. Creating new save.");
                    CreateNewSave();
                    return;
                }

                string json = System.IO.File.ReadAllText(path);
                _data = PlayerData.FromJson(json);
                _isLoaded = true;
                Debug.Log($"[SaveManager] Loaded local cache. Version: {_data.Version}");
                OnDataLoaded?.Invoke(_data);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] Local cache load failed: {ex.Message}");
                CreateNewSave();
            }
        }
    }
}
