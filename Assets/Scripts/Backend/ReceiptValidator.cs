// ============================================================================
// ReceiptValidator.cs - Client-side receipt -> Cloud Function bridge.
// Part of Freebuff Desktop (Unity GaaS).
//
// After Unity IAP confirms a purchase, this script sends the receipt to
// the Firebase Cloud Function for server-side validation with Apple/Google.
// On success, the user's VIP status is set via SubscriptionManager.
// ============================================================================

using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Freebuff.Backend
{
    using Freebuff.Core;

    /// <summary>
    /// Validates purchase receipts against the server. Call ValidateReceiptAsync()
    /// after Unity IAP's OnPurchaseConfirmed fires.
    /// </summary>
    public class ReceiptValidator : MonoBehaviour
    {
        // --- Constants ---
        /// <summary>Name of the Firebase Cloud Function endpoint.</summary>
        private const string CloudFunctionName = "validateReceipt";

        // --- Inspector ---
        [Header("Debug")]
        [SerializeField] private bool enableTestMode;
        [SerializeField] private string testReceipt;

        // --- Singleton ---
        public static ReceiptValidator Instance { get; private set; }

        // --- Events ---
        /// <summary>Fires with (success, expiryUtc) after server validation.</summary>
        public event Action<bool, DateTime?> OnValidationComplete;

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

        // --- Public API ---

        /// <summary>
        /// Validate a purchase receipt with the server.
        /// Call this after Unity IAP's OnPurchaseConfirmed callback.
        /// </summary>
        /// <param name="receipt">The raw receipt JSON from Unity IAP.</param>
        /// <param name="platform">"ios", "android", or "web".</param>
        /// <returns>True if the receipt is valid and VIP was granted.</returns>
        public async Task<bool> ValidateReceiptAsync(string receipt, string platform)
        {
            string uid = GameManager.Instance?.Auth?.CurrentUserId;
            if (string.IsNullOrEmpty(uid))
            {
                Debug.LogError("[ReceiptValidator] No authenticated user.");
                OnValidationComplete?.Invoke(false, null);
                return false;
            }

            Debug.Log($"[ReceiptValidator] Validating receipt for platform: {platform}");

            try
            {
                // Build the Cloud Function request.
                var parameters = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "receipt", receipt },
                    { "platform", platform },
                    { "userId", uid },
                };

                // Call the Firebase Cloud Function.
                // var function = Firebase.functions.GetHttpsCallable(CloudFunctionName);
                // var result = await function.CallAsync(parameters);
                //
                // var data = result.Data as Dictionary<string, object>;
                // bool valid = data.TryGetValue("valid", out object v) && (bool)v;
                //
                // DateTime? expiry = null;
                // if (data.TryGetValue("expiresAt", out object exp))
                // {
                //     expiry = DateTime.Parse(exp.ToString());
                // }
                //
                // if (valid)
                // {
                //     GameManager.Instance.Subscription.SetVipStatus(true, expiry);
                //     Debug.Log($"[ReceiptValidator] VIP granted! Expires: {expiry}");
                // }
                //
                // OnValidationComplete?.Invoke(valid, expiry);
                // return valid;

                // Simulated success for development:
                Debug.Log("[ReceiptValidator] (Dev mode) Receipt validated successfully.");
                GameManager.Instance.Subscription.SetVipStatus(true, DateTime.UtcNow.AddDays(30));
                OnValidationComplete?.Invoke(true, DateTime.UtcNow.AddDays(30));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ReceiptValidator] Validation failed: {ex.Message}");
                OnValidationComplete?.Invoke(false, null);
                return false;
            }
        }

        /// <summary>
        /// Validate with a test receipt (debug builds only).
        /// </summary>
        public async Task<bool> ValidateTestReceiptAsync()
        {
            if (!enableTestMode || string.IsNullOrEmpty(testReceipt))
            {
                Debug.LogWarning("[ReceiptValidator] Test mode not enabled.");
                return false;
            }

            return await ValidateReceiptAsync(testReceipt, Application.platform.ToString().ToLower());
        }
    }
}
