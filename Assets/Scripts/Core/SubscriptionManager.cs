// ============================================================================
// SubscriptionManager.cs — Cross-platform subscription & IAP facade.
// Part of Freebuff Desktop (Unity GaaS).
//
// Uses Unity IAP (IStoreListener) as the abstraction layer. On iOS it wraps
// StoreKit 2 via RevenueCat; on Android it wraps Google Play Billing;
// on WebGL it wraps Stripe Checkout via a lightweight HTTP bridge.
// ============================================================================

using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Freebuff.Core
{
    /// <summary>
    /// Central subscription authority. Other systems read <see cref="IsVip"/>
    /// rather than calling IAP directly.
    /// </summary>
    public class SubscriptionManager : MonoBehaviour
    {
        // ── Events ────────────────────────────────────────────────────────
        /// <summary>Fires with (true/false) whenever VIP status changes.</summary>
        public event Action<bool> OnVipStatusChanged;

        // ── Product IDs (must match store dashboards) ─────────────────────
        public const string MonthlySubId = "com.freebuff.premium.monthly";
        public const string BattlePassId  = "com.freebuff.battlepass.current";

        // ── State ─────────────────────────────────────────────────────────
        private bool _isVip;
        private DateTime? _vipExpiryUtc;

        /// <summary>True when the user has an active, unexpired VIP subscription.</summary>
        public bool IsVip => _isVip && (!_vipExpiryUtc.HasValue || _vipExpiryUtc.Value > DateTime.UtcNow);

        /// <summary>UTC expiry of the current VIP window, or null if lifetime.</summary>
        public DateTime? VipExpiryUtc => _vipExpiryUtc;

        // ── Initialization ────────────────────────────────────────────────

        /// <summary>
        /// Called by GameManager at startup. Restores previous purchases from
        /// the store without prompting the user. Server-side validation is done
        /// via the ReceiptValidator Cloud Function.
        /// </summary>
        public async Task RestorePurchasesAsync()
        {
            try
            {
                // Unity IAP: initiate a restore flow (no-op on WebGL).
                // In production, Unity IAP's IStoreListener.RestoreTransactions()
                // will trigger OnPurchaseFailed / OnPurchaseConfirmed callbacks.
                Debug.Log("[SubscriptionManager] Restoring purchases...");

                // Simulated: in a real build, the IAP listener handles this.
                // For now, we check Firestore for existing VIP status.
                await CheckServerVipStatusAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SubscriptionManager] Restore failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Server-side VIP check: reads the Firestore document to see if the
        /// user has a validated, non-expired subscription.
        /// </summary>
        private async Task CheckServerVipStatusAsync()
        {
            string uid = GameManager.Instance?.Auth?.CurrentUserId;
            if (string.IsNullOrEmpty(uid)) return;

            // Firestore read — in production, use Firebase SDK directly.
            // var doc = await FirebaseFirestore.DefaultInstance
            //     .Collection("users").Document(uid).GetSnapshotAsync();
            //
            // if (doc.Exists && doc.TryGetValue("isVIP", out bool vip) && vip)
            // {
            //     doc.TryGetValue("vipExpiry", out string expiry);
            //     _isVip = true;
            //     _vipExpiryUtc = DateTime.Parse(expiry);
            // }

            Debug.Log($"[SubscriptionManager] VIP status: {IsVip}");
            OnVipStatusChanged?.Invoke(IsVip);
        }

        // ── Purchase Flow ─────────────────────────────────────────────────

        /// <summary>
        /// Called by the UI paywall when the user taps "Subscribe".
        /// Initiates a purchase through Unity IAP.
        /// </summary>
        public void RequestPurchase(string productId)
        {
            Debug.Log($"[SubscriptionManager] Requesting purchase: {productId}");
            // Unity IAP: m_StoreController.InitiatePurchase(productId);
            // The IStoreListener callback will call OnPurchaseConfirmed or
            // OnPurchaseFailed, which are wired up in the IAP initialization code.
        }

        /// <summary>
        /// Called by the IAP listener (or ReceiptValidator) when a purchase
        /// is confirmed server-side. Updates local VIP state.
        /// </summary>
        public void SetVipStatus(bool active, DateTime? expiryUtc = null)
        {
            _isVip = active;
            _vipExpiryUtc = expiryUtc;
            OnVipStatusChanged?.Invoke(IsVip);
            Debug.Log($"[SubscriptionManager] VIP set to {active}, expiry: {expiryUtc}");
        }
    }
}
