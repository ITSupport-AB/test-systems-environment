// ============================================================================
// UIManager.cs — Central panel routing and loading overlay.
// Part of Freebuff Desktop (Unity GaaS).
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Freebuff.Core
{
    /// <summary>
    /// Manages UI panel visibility. Each panel is a CanvasGroup on a child
    /// GameObject. Panels are shown/hidden via a string key for simplicity;
    /// in production, use an enum or ScriptableObject-based panel registry.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        // ── Panel Types ───────────────────────────────────────────────────
        public enum PanelType
        {
            Loading,
            MainMenu,
            Paywall,
            Gameplay,
            GameOver,
            Settings,
            Pause,
        }

        // ── Inspector ─────────────────────────────────────────────────────
        [Header("Panel References")]
        [SerializeField] private CanvasGroup loadingPanel;
        [SerializeField] private TMPro.TextMeshProUGUI loadingText;

        [SerializeField] private CanvasGroup mainMenuPanel;
        [SerializeField] private CanvasGroup paywallPanel;
        [SerializeField] private CanvasGroup gameplayPanel;
        [SerializeField] private CanvasGroup gameOverPanel;
        [SerializeField] private CanvasGroup settingsPanel;
        [SerializeField] private CanvasGroup pausePanel;

        // ── Events ────────────────────────────────────────────────────────
        /// <summary>Fires with the panel type that was just shown.</summary>
        public event Action<PanelType> OnPanelShown;

        /// <summary>Fires with the panel type that was just hidden.</summary>
        public event Action<PanelType> OnPanelHidden;

        // ── State ─────────────────────────────────────────────────────────
        private readonly Dictionary<PanelType, CanvasGroup> _panels = new();
        private PanelType? _activePanel;

        // ── Lifecycle ─────────────────────────────────────────────────────

        private void Awake()
        {
            // Map enum → CanvasGroup for O(1) lookups.
            _panels[PanelType.Loading]   = loadingPanel;
            _panels[PanelType.MainMenu]  = mainMenuPanel;
            _panels[PanelType.Paywall]   = paywallPanel;
            _panels[PanelType.Gameplay]  = gameplayPanel;
            _panels[PanelType.GameOver]  = gameOverPanel;
            _panels[PanelType.Settings]  = settingsPanel;
            _panels[PanelType.Pause]     = pausePanel;

            // Start with everything hidden.
            foreach (var kvp in _panels)
                SetPanelVisible(kvp.Value, false);
        }

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>Show a single panel, hiding all others.</summary>
        public void ShowPanel(PanelType panel)
        {
            foreach (var kvp in _panels)
                SetPanelVisible(kvp.Value, kvp.Key == panel);

            _activePanel = panel;
            OnPanelShown?.Invoke(panel);
        }

        /// <summary>Hide a specific panel.</summary>
        public void HidePanel(PanelType panel)
        {
            if (_panels.TryGetValue(panel, out var cg))
            {
                SetPanelVisible(cg, false);
                OnPanelHidden?.Invoke(panel);
            }
        }

        /// <summary>Convenience: show loading overlay with message.</summary>
        public void ShowLoading(string message)
        {
            if (loadingText != null) loadingText.text = message;
            SetPanelVisible(loadingPanel, true);
        }

        /// <summary>Convenience: hide loading overlay.</summary>
        public void HideLoading()
        {
            SetPanelVisible(loadingPanel, false);
        }

        /// <summary>Convenience: show main menu.</summary>
        public void ShowMainMenu() => ShowPanel(PanelType.MainMenu);

        /// <summary>Show an error message (reuses loading panel with red text).</summary>
        public void ShowError(string message)
        {
            if (loadingText != null)
            {
                loadingText.text = message;
                loadingText.color = Color.red;
            }
            SetPanelVisible(loadingPanel, true);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static void SetPanelVisible(CanvasGroup cg, bool visible)
        {
            if (cg == null) return;
            cg.alpha = visible ? 1f : 0f;
            cg.interactable = visible;
            cg.blocksRaycasts = visible;
        }
    }
}
