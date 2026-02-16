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

        var partnerName = message.Sender.TextValue.Trim();

        // Check if tab already exists
        var existingTab = config.Tabs.FirstOrDefault(t =>
            t.TargetSender != null && partnerName.Contains(t.TargetSender.Split('@')[0], StringComparison.OrdinalIgnoreCase));

        if (existingTab != null) return;

        // ---------------------------------------------------------
        // FIND THE WORLD ID (CRITICAL FOR CROSS-WORLD TELLS)
        // ---------------------------------------------------------
        ushort worldId = 0;
        string cleanName = partnerName;

        // Strategy A: Check Payloads (Best)
        var senderPayload = message.Sender.Payloads.FirstOrDefault(p => p is PlayerPayload) as PlayerPayload;
        if (senderPayload != null)
        {
            cleanName = senderPayload.PlayerName;
            worldId = (ushort)senderPayload.World.RowId;
        }
        // Strategy B: Check "Name@World" String
        else if (partnerName.Contains('@'))
        {
            var parts = partnerName.Split('@');
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
        // Strategy C: Check Nearby Players
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
        // Strategy D: Fallback to Your World
        if (worldId == 0 && Plugin.ClientState.LocalPlayer != null)
        {
            worldId = (ushort)Plugin.ClientState.LocalPlayer.CurrentWorld.RowId;
        }

        // ---------------------------------------------------------
        // CONSTRUCT THE COMMAND TARGET
        // ---------------------------------------------------------
        // We save "Name@World" into TargetSender. This is what we will use for the /tell command.
        string finalTarget = cleanName;
        if (worldId > 0)
        {
            var worldName = Sheets.WorldSheet.GetRow(worldId).Name.ToString();
            finalTarget = $"{cleanName}@{worldName}";
        }

        // Create the Tab
        var newTab = new Tab
        {
            Name = cleanName,       // Tab Title (Short Name)
            TargetSender = finalTarget, // Command Target (Full Name@World)
            ChatCodes = new Dictionary<ChatType, ChatSource>
            {
                { ChatType.TellIncoming, (ChatSource)0xFFFF },
                { ChatType.TellOutgoing, (ChatSource)0xFFFF }
            },
            UnreadMode = UnreadMode.Unseen,
            DisplayTimestamp = true,
            Channel = InputChannel.Tell,
            CanResize = true,
            CanMove = true
        };

        // Create the initial target object (Visuals only)
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