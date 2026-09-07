using System;
using HarmonyLib;

namespace TimePlayerControl.Patches
{
    [HarmonyPatch(typeof(Chat))]
    public static class ChatPatch
    {
        [HarmonyPatch(nameof(Chat.RPC_ChatMessage))]
        [HarmonyPrefix]
        public static bool RPC_ChatMessage_Prefix(long sender, string text)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return true;

            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            if (peer == null) return true;

            if (TimeManager.Instance != null && TimeManager.Instance.HandleChatCommand(peer, text))
            {
                // Si el comando fue procesado por TimeManager (/tiempo, /tiempos), cancelamos la emisión pública del chat.
                return false;
            }

            return true;
        }
    }
}
