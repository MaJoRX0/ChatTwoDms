using ChatTwo.GameFunctions.Types;
using ChatTwo.Code;
using System.Linq;
using System.Collections.Generic;
using Dalamud.Game.Text;
using System;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Game.ClientState.Objects.SubKinds;

namespace ChatTwo;

internal static class DmTabManager
{
    private const int HISTORY_SCAN_LIMIT = 20000;

    public static void HandleMessage(MessageManager.PendingMessage message, Configuration config, MessageStore store)
    {
        if (message.Type != XivChatType.TellIncoming && message.Type != XivChatType.TellOutgoing)
            return;

        // ---------------------------------------------------------
        // 1. EXTRACT CLEAN NAME & WORLD ID (Moved to Top)
        // ---------------------------------------------------------
        // We do this FIRST so we can match against existing tabs accurately.

        var rawName = message.Sender.TextValue.Trim();
        string cleanName = rawName;
        ushort worldId = 0;

        // Strategy A: Check Payloads (Most Accurate)
        var senderPayload = message.Sender.Payloads.FirstOrDefault(p => p is PlayerPayload) as PlayerPayload;
        if (senderPayload != null)
        {
            cleanName = senderPayload.PlayerName;
            worldId = (ushort)senderPayload.World.RowId;
        }
        // Strategy B: Check "Name@World" String
        else if (rawName.Contains('@'))
        {
            var parts = rawName.Split('@');
            cleanName = parts[0];
            var worldName = parts[1];
            foreach (var w in Sheets.WorldSheet)
            {
                if (w.Name.ToString().Equals(worldName, StringComparison.OrdinalIgnoreCase))
                {
                    worldId = (ushort)w.RowId;
                    break;
                }
            }
        }
        // Strategy C: Check Object Table
        if (worldId == 0)
        {
            foreach (var obj in Plugin.ObjectTable)
            {
                if (obj is IPlayerCharacter pc && pc.Name.ToString().Equals(cleanName, StringComparison.OrdinalIgnoreCase))
                {
                    worldId = (ushort)pc.CurrentWorld.RowId;
                    break;
                }
            }
        }
        // Strategy D: Fallback to Local Player's World
        if (worldId == 0 && Plugin.ClientState.LocalPlayer != null)
        {
            worldId = (ushort)Plugin.ClientState.LocalPlayer.CurrentWorld.RowId;
        }

        // ---------------------------------------------------------
        // 2. CHECK FOR EXISTING TABS (Strict Match)
        // ---------------------------------------------------------
        var existingTab = config.Tabs.FirstOrDefault(t =>
        {
            if (string.IsNullOrEmpty(t.TargetSender)) return false;

            // Get the name part of the tab's target (e.g., "Bob" from "Bob@Lich")
            var tabTargetName = t.TargetSender.Split('@')[0].Trim();

            // [FIX] USE .EQUALS INSTEAD OF .CONTAINS
            // This prevents "Alex" from matching into "Al" tab.
            return cleanName.Equals(tabTargetName, StringComparison.OrdinalIgnoreCase);
        });

        // If found, we stop here (the message manager adds it to that tab automatically later)
        if (existingTab != null) return;

        // ---------------------------------------------------------
        // 3. CREATE NEW TAB
        // ---------------------------------------------------------
        string finalTarget = cleanName;
        if (worldId > 0)
        {
            var worldName = Sheets.WorldSheet.GetRow(worldId).Name.ToString();
            finalTarget = $"{cleanName}@{worldName}";
        }

        var newTab = new Tab
        {
            Name = cleanName,
            TargetSender = finalTarget,
            ChatCodes = new Dictionary<ChatType, ChatSource>
            {
                { ChatType.TellIncoming, (ChatSource)0xFFFF },
                { ChatType.TellOutgoing, (ChatSource)0xFFFF }
            },
            UnreadMode = UnreadMode.Unseen,
            DisplayTimestamp = true,
            Channel = InputChannel.Tell,
            CanResize = true,
            CanMove = true,
            IsManuallyHidden = false // [FIX] Explicitly force it to be visible
        };

        if (worldId > 0)
        {
            var target = new TellTarget(cleanName, worldId, 0, TellReason.Direct);
            newTab.CurrentChannel.TellTarget = target;
            newTab.CurrentChannel.Channel = InputChannel.Tell;
        }

        BackfillHistory(newTab, store);
        config.Tabs.Add(newTab);


    }

    private static void BackfillHistory(Tab tab, MessageStore store)
    {
        using var enumerator = store.GetRecentTells(count: -1);
        var historyToAdd = new List<Message>();
        foreach (var msg in enumerator)
        {
            if (tab.Matches(msg)) historyToAdd.Add(msg);
        }
        if (historyToAdd.Count > 0)
        {
            tab.Messages.AddSortPrune(historyToAdd, int.MaxValue);
        }
    }
}