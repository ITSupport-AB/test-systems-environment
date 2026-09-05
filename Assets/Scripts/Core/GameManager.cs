// ============================================================================
// GameManager.cs — Top-level orchestrator for initialization sequence.
// Part of Freebuff Desktop (Unity GaaS).
//
// Initialization order:
//   1. Firebase Remote Config (tunables needed by all other systems)
//   2. Auth (anonymous or restore cached session)
//   3. Subscription check (receipt restore from store + server VIP lookup)
//   4. Load player save data from cloud (with local cache fallback)
//   5. Show Main Menu
// ============================================================================

using System;
using UnityEngine;

namespace Freebuff.Core
{
    /// <summary>
    /// Singleton that bootstraps the app in order.
    /// Attach to a persistent DontDestroyOnLoad GameObject.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        // --- Singleton ---
        public static GameManager Instance { get; private set; }

        // --- Inspector References ---
        [Header("Manager References")]
        [SerializeField] private AuthManager authManager;
        [SerializeField] private SubscriptionManager subscriptionManager;
        [SerializeField] private UIManager uiManager;
        [SerializeField] private PlayerController playerController;

        // --- Public Accessors ---
        public AuthManager Auth => authManager;
        public SubscriptionManager Subscription => subscriptionManager;
        public UIManager UI => uiManager;
        public PlayerController Player => playerController;

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

        private async void Start()
        {
            try
            {
                // 1. Show loading screen.
                uiManager.ShowLoading("Initializing...");

                // 2. Fetch Remote Config values (price points, event durations, etc.).
                //    This must come first because other systems read these values
                //    during their own initialization.
                if (LiveOps.RemoteConfigManager.Instance != null)
                {
                    uiManager.ShowLoading("Fetching configuration...");
                    await LiveOps.RemoteConfigManager.Instance.InitializeAsync();
                }
                else
                {
                    Debug.LogWarning("[GameManager] RemoteConfigManager not found in scene.");
                }

                // 3. Authenticate (anonymous or restore cached session).
                uiManager.ShowLoading("Authenticating...");
                bool authOk = await authManager.InitializeAsync();
                if (!authOk)
                {
                    uiManager.ShowError("Authentication failed. Please restart.");
                    return;
                }

                // 4. Validate / restore VIP subscription.
                //    After auth, we can check the server for existing VIP status
                //    and restore any pending purchases from the store.
                uiManager.ShowLoading("Checking subscription...");
                await subscriptionManager.RestorePurchasesAsync();

                // 5. Load player save data from cloud.
                //    Falls back to local JSON cache if the network request fails.
                if (Save.SaveManager.Instance != null)
                {
                    uiManager.ShowLoading("Loading your data...");
                    await Save.SaveManager.Instance.LoadAsync();
                }
                else
                {
                    Debug.LogWarning("[GameManager] SaveManager not found in scene.");
                }

                // 6. Ready — hide loading, show main menu.
                uiManager.HideLoading();
                uiManager.ShowMainMenu();

                Debug.Log("[GameManager] Initialization complete.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameManager] Initialization failed: {ex.Message}\n{ex.StackTrace}");
                if (uiManager != null)
                    uiManager.ShowError("Initialization failed. Please restart.");
            }
        }
    }
}
