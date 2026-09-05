// ============================================================================
// PlayerData.cs — Serializable player data model for cloud sync & local cache.
// Part of Freebuff Desktop (Unity GaaS).
// ============================================================================

using System;
using System.Collections.Generic;

namespace Freebuff.Data
{
    /// <summary>
    /// Root data object persisted to Firestore and cached locally.
    /// All fields use JSON-serializable types (System.Text.Json / Newtonsoft).
    /// </summary>
    [Serializable]
    public class PlayerData
    {
        // ── Currency ──────────────────────────────────────────────────────
        /// <summary>Soft currency earned through gameplay.</summary>
        public int Coins;

        /// <summary>Premium currency (purchasable or earned via Battle Pass).</summary>
        public int Gems;

        // ── Inventory ─────────────────────────────────────────────────────
        /// <summary>Map of item-ID → quantity owned.</summary>
        public Dictionary<string, int> Inventory = new Dictionary<string, int>();

        /// <summary>List of owned cosmetic IDs (skins, emotes, etc.).</summary>
        public List<string> OwnedCosmetics = new List<string>();

        /// <summary>ID of the currently equipped cosmetic per slot.</summary>
        public Dictionary<string, string> EquippedCosmetics = new Dictionary<string, string>();

        // ── Progression ───────────────────────────────────────────────────
        /// <summary>Current experience points toward next Battle Pass tier.</summary>
        public int XP;

        /// <summary>Current Battle Pass tier (0 = free track only).</summary>
        public int BattlePassTier;

        /// <summary>Highest wave survived (used for leaderboards).</summary>
        public int HighScore;

        // ── Metadata ──────────────────────────────────────────────────────
        /// <summary>ISO-8601 timestamp of the last successful save.</summary>
        public string LastSaveTimestamp = string.Empty;

        /// <summary>
        /// Monotonic version counter. Incremented on every save to detect
        /// client-side write conflicts without reading the server first.
        /// </summary>
        public long Version;

        // ── Helpers ───────────────────────────────────────────────────────

        /// <summary>Add coins with a floor of zero.</summary>
        public void AddCoins(int amount)
        {
            Coins = Math.Max(0, Coins + amount);
        }

        /// <summary>Add gems with a floor of zero.</summary>
        public void AddGems(int amount)
        {
            Gems = Math.Max(0, Gems + amount);
        }

        /// <summary>Spend coins; returns false if insufficient funds.</summary>
        public bool SpendCoins(int cost)
        {
            if (Coins < cost) return false;
            Coins -= cost;
            return true;
        }

        /// <summary>Spend gems; returns false if insufficient funds.</summary>
        public bool SpendGems(int cost)
        {
            if (Gems < cost) return false;
            Gems -= cost;
            return true;
        }

        /// <summary>Add an item to inventory. Creates the entry if missing.</summary>
        public void AddItem(string itemId, int quantity = 1)
        {
            if (Inventory.TryGetValue(itemId, out int existing))
                Inventory[itemId] = existing + quantity;
            else
                Inventory[itemId] = quantity;
        }

        /// <summary>Remove items; returns false if insufficient quantity.</summary>
        public bool RemoveItem(string itemId, int quantity = 1)
        {
            if (!Inventory.TryGetValue(itemId, out int current) || current < quantity)
                return false;

            Inventory[itemId] = current - quantity;
            if (Inventory[itemId] <= 0)
                Inventory.Remove(itemId);

            return true;
        }

        /// <summary>Create a deep-ish clone for diffing before save.</summary>
        public PlayerData Clone()
        {
            return new PlayerData
            {
                Coins = Coins,
                Gems = Gems,
                Inventory = new Dictionary<string, int>(Inventory),
                OwnedCosmetics = new List<string>(OwnedCosmetics),
                EquippedCosmetics = new Dictionary<string, string>(EquippedCosmetics),
                XP = XP,
                BattlePassTier = BattlePassTier,
                HighScore = HighScore,
                LastSaveTimestamp = LastSaveTimestamp,
                Version = Version,
            };
        }

        /// <summary>Serialize to JSON for Firestore.</summary>
        public string ToJson()
        {
            // Uses Newtonsoft.Json (JsonConvert) which is the Unity/PlayFab default.
            return Newtonsoft.Json.JsonConvert.SerializeObject(this);
        }

        /// <summary>Deserialize from JSON.</summary>
        public static PlayerData FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return new PlayerData();

            return Newtonsoft.Json.JsonConvert.DeserializeObject<PlayerData>(json)
                   ?? new PlayerData();
        }
    }
}
