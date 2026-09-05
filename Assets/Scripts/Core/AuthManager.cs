// ============================================================================
// AuthManager.cs — Firebase Authentication wrapper.
// Part of Freebuff Desktop (Unity GaaS).
// ============================================================================

using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Freebuff.Core
{
    /// <summary>
    /// Handles anonymous sign-in, account linking (email/social), and session
    /// persistence via Firebase Auth. The UID is the canonical key for all
    /// Firestore documents and cross-platform entitlement lookups.
    /// </summary>
    public class AuthManager : MonoBehaviour
    {
        // ── Events ────────────────────────────────────────────────────────
        /// <summary>Fires when the authenticated user changes (login/logout/link).</summary>
        public event Action<string> OnAuthStateChanged;

        // ── State ─────────────────────────────────────────────────────────
        private string _currentUserId = string.Empty;

        /// <summary>The Firebase UID of the current user, or empty if signed out.</summary>
        public string CurrentUserId => _currentUserId;

        /// <summary>True if a user is currently authenticated.</summary>
        public bool IsAuthenticated => !string.IsNullOrEmpty(_currentUserId);

        // ── Initialization ────────────────────────────────────────────────

        /// <summary>
        /// Initialize Firebase Auth. Restores a cached session if available,
        /// otherwise signs in anonymously.
        /// </summary>
        /// <returns>True if authentication succeeded.</returns>
        public async Task<bool> InitializeAsync()
        {
            try
            {
                // Firebase Auth is already initialized by FirebaseApp.DefaultInstance.
                // Check if there's a cached user.
                var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;

                if (user != null)
                {
                    // Refresh the token to ensure it's still valid.
                    await user.ReloadAsync();
                    _currentUserId = user.UserId;
                    Debug.Log($"[AuthManager] Restored session for UID: {_currentUserId}");
                }
                else
                {
                    // Anonymous sign-in for first-time users.
                    var result = await Firebase.Auth.FirebaseAuth.DefaultInstance
                        .SignInAnonymouslyAsync();

                    _currentUserId = result.User.UserId;
                    Debug.Log($"[AuthManager] Anonymous sign-in complete. UID: {_currentUserId}");
                }

                OnAuthStateChanged?.Invoke(_currentUserId);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthManager] Auth failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Link an anonymous account with an email/password credential.
        /// Call this when the user signs up with email after playing anonymously.
        /// </summary>
        public async Task<bool> LinkWithEmailAsync(string email, string password)
        {
            try
            {
                var credential = Firebase.Auth.EmailAuthProvider
                    .GetCredential(email, password);

                var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
                if (user == null)
                {
                    Debug.LogError("[AuthManager] No current user to link.");
                    return false;
                }

                var result = await user.LinkWithCredentialAsync(credential);
                Debug.Log($"[AuthManager] Account linked for UID: {result.User.UserId}");

                OnAuthStateChanged?.Invoke(_currentUserId);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthManager] Link failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Sign out the current user.</summary>
        public void SignOut()
        {
            Firebase.Auth.FirebaseAuth.DefaultInstance.SignOut();
            _currentUserId = string.Empty;
            OnAuthStateChanged?.Invoke(_currentUserId);
            Debug.Log("[AuthManager] Signed out.");
        }
    }
}
