using System;
using HarmonyLib;

namespace TimePlayerControl.Patches
{
    [HarmonyPatch(typeof(ZNet))]
    public static class ZNetPatch
    {
        [HarmonyPatch(nameof(ZNet.RPC_PeerInfo))]
        [HarmonyPostfix]
        public static void RPC_PeerInfo_Postfix(ZNet __instance, ZRpc rpc)
        {
            if (__instance == null || !__instance.IsServer()) return;

            ZNetPeer peer = __instance.GetPeer(rpc);
            if (peer == null) return;

            if (TimeManager.Instance != null)
            {
                if (!TimeManager.Instance.OnPlayerConnecting(peer, out string kickReason))
                {
                    Plugin.Logger.LogInfo($"[ZNetPatch] Rechazando conexión de {peer.m_playerName}: {kickReason}");
                    TimeManager.SendCenterMessageToPeer(peer, kickReason);
                    TimeManager.SendChatMessageToPeer(peer, kickReason);
                    __instance.Disconnect(peer);
                }
            }
        }

        [HarmonyPatch(nameof(ZNet.Disconnect))]
        [HarmonyPrefix]
        public static void Disconnect_Prefix(ZNetPeer peer)
        {
            if (TimeManager.Instance != null && TimeManager.Instance.DataStore != null)
            {
                TimeManager.Instance.DataStore.Save();
            }
        }

        [HarmonyPatch(nameof(ZNet.Disconnect))]
        [HarmonyPostfix]
        public static void Disconnect_Postfix(ZNet __instance, ZNetPeer peer)
        {
            if (__instance == null || !__instance.IsServer())
                return;
            if (TimeManager.Instance == null)
                return;

            // El peer ya salió: refrescar pausa/top en los que siguen conectados.
            TimeManager.Instance.BroadcastTimeSyncToReadyPeers(excludePeerId: peer != null ? peer.m_uid : 0);
        }
    }
}
