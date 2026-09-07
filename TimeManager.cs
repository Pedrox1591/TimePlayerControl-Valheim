using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using TimePlayerControl.Data;

namespace TimePlayerControl
{
    public class TimeManager : MonoBehaviour
    {
        public static TimeManager Instance { get; private set; }
        public TimeDataStore DataStore { get; private set; }

        private const string RpcTimeSync = "TimePlayerControl_TimeSync";
        private const string RpcForceMenu = "TimePlayerControl_ForceMenu";

        private float _saveTimer = 0f;
        private float _tickTimer = 0f;
        private float _configCheckTimer = 0f;
        private const float ConfigCheckIntervalSeconds = 10f;

        private ClientTimeHud _clientHud;
        private bool _isClientMode;
        private bool _isServerMode;
        private bool _clientRpcRegistered;
        private bool _lastHudEnabledState = true;
        private float _clientConfigCheckTimer = 0f;

        private void Awake()
        {
            Instance = this;
            DataStore = TimeDataStore.Load();
            Plugin.LogInfo("[TimeManager] Inicializado correctamente.");

            if (ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.Register(RpcTimeSync, new Action<long, ZPackage>(RPC_TimeSync));
                ZRoutedRpc.instance.Register(RpcForceMenu, new Action<long, ZPackage>(RPC_ForceMenu));
                Plugin.LogInfo("[Client] RPCs registrados en Awake.");
            }
            else
            {
                Plugin.LogInfo("[Client] ZRoutedRpc.instance es null en Awake, se reintentará en Update.");
            }
        }

        private void Update()
        {
            if (ZNet.instance == null)
                return;

            _isServerMode = ZNet.instance.IsServer();
            _isClientMode = !_isServerMode;

            if (_isClientMode)
            {
                EnsureClientRuntime();
                ReloadClientHudConfigFromDisk();
            }
            else if (_clientHud != null)
            {
                _clientHud.SetEnabled(false);
            }

            if (_isServerMode)
            {
                ProcessServerTick();
            }
        }

        private void EnsureClientRuntime()
        {
            if (!_clientRpcRegistered && ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.Register(RpcTimeSync, new Action<long, ZPackage>(RPC_TimeSync));
                ZRoutedRpc.instance.Register(RpcForceMenu, new Action<long, ZPackage>(RPC_ForceMenu));
                _clientRpcRegistered = true;
                Plugin.LogInfo("[Client] RPCs registrados en EnsureClientRuntime.");
            }

            if (_clientHud == null)
            {
                _clientHud = gameObject.AddComponent<ClientTimeHud>();
                Plugin.LogInfo("[Client] ClientTimeHud component creado.");
            }

            if (_clientHud != null && DataStore != null)
            {
                bool shouldEnable = DataStore.ServerConfig.EnableClientHud;
                if (shouldEnable != _lastHudEnabledState)
                {
                    _clientHud.SetEnabled(shouldEnable);
                    _lastHudEnabledState = shouldEnable;
                    Plugin.LogInfo($"[Client] HUD changed to: {shouldEnable}");
                }
            }
        }

        private void ProcessServerTick()
        {
            float dt = Time.deltaTime;
            _tickTimer += dt;
            _saveTimer += dt;
            _configCheckTimer += dt;

            if (_tickTimer >= 1.0f)
            {
                _tickTimer = 0f;
                ProcessTimeTick(1.0f);
            }

            if (_configCheckTimer >= ConfigCheckIntervalSeconds)
            {
                _configCheckTimer = 0f;
                ReloadConfigFromDisk();
            }

            float saveIntervalSeconds = (float)DataStore.ServerConfig.SaveIntervalSeconds;
            if (_saveTimer >= saveIntervalSeconds)
            {
                _saveTimer = 0f;
                DataStore.Save();
            }
        }

        private void OnApplicationQuit()
        {
            if (DataStore != null)
            {
                DataStore.Save();
            }
        }

        private void RPC_TimeSync(long sender, ZPackage pkg)
        {
            Plugin.LogDebug("[Client] RPC_TimeSync llamado.");

            if (pkg == null)
            {
                Plugin.LogDebug("[Client] RPC_TimeSync recibió pkg null!");
                return;
            }

            double remainingSeconds = pkg.ReadDouble();
            double assignedSeconds = pkg.ReadDouble();
            double usedSeconds = pkg.ReadDouble();
            bool exempt = pkg.ReadBool();
            string nextResetIso = pkg.ReadString();
            bool showHud = pkg.ReadBool();
            double totalPlayedSeconds = pkg.ReadDouble();
            var topPlayers = new List<LeaderboardEntry>();
            int topCount = pkg.ReadInt();
            for (int i = 0; i < topCount; i++)
            {
                string name = pkg.ReadString();
                double total = pkg.ReadDouble();
                topPlayers.Add(new LeaderboardEntry
                {
                    PlayerName = string.IsNullOrEmpty(name) ? "Jugador" : name,
                    TotalPlayedSeconds = Math.Max(0d, total)
                });
            }

            DateTime? nextReset = null;
            if (!string.IsNullOrEmpty(nextResetIso))
            {
                DateTime parsed;
                if (DateTime.TryParse(nextResetIso, out parsed))
                    nextReset = parsed;
            }

            ClientTimeInfo.CurrentRemainingSeconds = remainingSeconds;
            ClientTimeInfo.CurrentAssignedSeconds = assignedSeconds;
            ClientTimeInfo.CurrentMaxSeconds = assignedSeconds;
            ClientTimeInfo.CurrentUsedSeconds = usedSeconds;
            ClientTimeInfo.IsExempt = exempt;
            ClientTimeInfo.NextReset = nextReset;
            ClientTimeInfo.ShowHud = showHud;
            ClientTimeInfo.TotalPlayedSeconds = Math.Max(0d, totalPlayedSeconds);
            ClientTimeInfo.TopPlayers = topPlayers;

            Plugin.LogDebug($"[Client] Sync recibido: remaining={remainingSeconds:F0}s, assigned={assignedSeconds:F0}s, used={usedSeconds:F0}s, total={totalPlayedSeconds:F0}s, top={topPlayers.Count}, exempt={exempt}, hud={showHud}");
        }

        private void RPC_ForceMenu(long sender, ZPackage pkg)
        {
            string reason = "Te has quedado sin tiempo";
            if (pkg != null)
            {
                string payload = pkg.ReadString();
                if (!string.IsNullOrEmpty(payload))
                    reason = payload;
            }

            Plugin.LogInfo($"[Client] RPC_ForceMenu recibido: {reason}");
            StartCoroutine(ForceReturnToMainMenuRoutine(reason));
        }

        private IEnumerator ForceReturnToMainMenuRoutine(string reason)
        {
            string message = string.IsNullOrWhiteSpace(reason) ? "Te has quedado sin tiempo" : reason.Trim();
            ClientTimeInfo.PendingMenuMessage = message;
            ClientTimeInfo.PendingMenuMessageUntil = Time.realtimeSinceStartup + 12f;

            try
            {
                if (MessageHud.instance != null)
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, message);
            }
            catch (Exception ex)
            {
                Plugin.LogDebug($"[Client] No se pudo mostrar MessageHud: {ex.Message}");
            }

            // Dar tiempo a ver el mensaje antes de salir al menú (evita el icono rojo de desconexión brusca).
            yield return new WaitForSecondsRealtime(1.4f);

            try
            {
                if (Game.instance != null)
                {
                    var logout = typeof(Game).GetMethod("Logout", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (logout != null)
                    {
                        var parameters = logout.GetParameters();
                        if (parameters.Length == 0)
                            logout.Invoke(Game.instance, null);
                        else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                            logout.Invoke(Game.instance, new object[] { false });
                        else
                            logout.Invoke(Game.instance, parameters.Select(p => p.HasDefaultValue ? p.DefaultValue : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)).ToArray());
                    }
                }
                else if (ZNet.instance != null)
                {
                    ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
                    if (serverPeer != null)
                        ZNet.instance.Disconnect(serverPeer);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogDebug($"[Client] Logout/Disconnect local: {ex.Message}");
            }

            yield return new WaitForSecondsRealtime(0.25f);

            try
            {
                if (SceneManager.GetActiveScene().name != "start")
                {
                    Plugin.LogInfo($"[Client] Volviendo al menú principal. Motivo: {message}");
                    SceneManager.LoadScene("start");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogInfo($"[Client] No se pudo volver al menú principal: {ex.Message}");
            }
        }

        private void ReloadConfigFromDisk()
        {
            if (DataStore == null)
            {
                DataStore = TimeDataStore.Load();
                return;
            }

            TimeDataStore latest = TimeDataStore.Load();
            if (latest == null)
                return;

            DataStore.ServerConfig = latest.ServerConfig ?? new ServerConfig();

            var mergedPlayers = new Dictionary<string, PlayerData>(DataStore.Players ?? new Dictionary<string, PlayerData>());
            if (latest.Players != null)
            {
                foreach (var kvp in latest.Players)
                {
                    if (!mergedPlayers.TryGetValue(kvp.Key, out var current))
                    {
                        mergedPlayers[kvp.Key] = kvp.Value;
                        continue;
                    }

                    if (string.IsNullOrEmpty(current.PlayerName) && !string.IsNullOrEmpty(kvp.Value.PlayerName))
                        current.PlayerName = kvp.Value.PlayerName;

                    if (string.IsNullOrEmpty(current.SteamID))
                        current.SteamID = kvp.Value.SteamID;

                    if (!current.IsExempt && kvp.Value.IsExempt)
                        current.IsExempt = true;
                }
            }

            DataStore.Players = mergedPlayers;

            Plugin.LogDebug($"[TimePlayerControl] [TRACE] Revisión JSON cada {ConfigCheckIntervalSeconds:F0}s: {DataStore.Players.Count} jugadores. save={DataStore.ServerConfig.SaveIntervalSeconds:F0}s, default={DataStore.ServerConfig.DefaultBlockSeconds:F0}s, warning={DataStore.ServerConfig.WarningThresholdSeconds:F0}s");

            foreach (var kvp in DataStore.Players.OrderBy(x => x.Value.PlayerName))
            {
                var player = kvp.Value;
                Plugin.LogDebug($"[TimePlayerControl] [TRACE] {player.PlayerName} ({player.SteamID}) -> restante={player.RemainingSeconds:F0}s, siguiente_reset={player.NextResetTime:O}, exento={player.IsExempt}");
            }
        }

        /// <summary>
        /// El layout del HUD se lee del JSON local del cliente. Recarga periódica para aplicar cambios sin reiniciar.
        /// </summary>
        private void ReloadClientHudConfigFromDisk()
        {
            _clientConfigCheckTimer += Time.deltaTime;
            if (_clientConfigCheckTimer < ConfigCheckIntervalSeconds)
                return;

            _clientConfigCheckTimer = 0f;
            if (DataStore == null)
                return;

            TimeDataStore latest = TimeDataStore.Load();
            if (latest?.ServerConfig == null)
                return;

            DataStore.ServerConfig = latest.ServerConfig;
            if (_clientHud != null)
                _clientHud.InvalidateStyles();
        }

        public static string GetPeerSteamID(ZNetPeer peer)
        {
            if (peer == null) return string.Empty;

            string rawHostName = peer.m_socket?.GetHostName() ?? string.Empty;
            if (rawHostName.StartsWith("Steam_"))
            {
                rawHostName = rawHostName.Substring(6);
            }
            return string.IsNullOrEmpty(rawHostName) ? peer.m_uid.ToString() : rawHostName;
        }

        public bool OnPlayerConnecting(ZNetPeer peer, out string kickReason)
        {
            kickReason = string.Empty;
            string steamId = GetPeerSteamID(peer);
            string playerName = string.IsNullOrEmpty(peer.m_playerName) ? "Jugador" : peer.m_playerName;
            DateTime now = DateTime.UtcNow;
            bool pauseActive = IsPauseRuleActive(out _, out _, out _);

            if (!DataStore.Players.TryGetValue(steamId, out var playerData))
            {
                playerData = new PlayerData
                {
                    SteamID = steamId,
                    PlayerName = playerName,
                    RemainingSeconds = 0,
                    AssignedSeconds = 0,
                    TotalPlayedSeconds = 0,
                    LastAssignedTime = now
                };

                DateTime nextReset = now.Date.AddHours(DataStore.ServerConfig.ResetHourUtc);
                if (nextReset <= now)
                    nextReset = nextReset.AddDays(1);

                double remainingUntilReset = Math.Max(0d, (nextReset - now).TotalSeconds);
                double assignedSeconds = Math.Min(DataStore.ServerConfig.DefaultBlockSeconds, remainingUntilReset);

                playerData.AssignedSeconds = assignedSeconds;
                playerData.RemainingSeconds = assignedSeconds;
                playerData.NextResetTime = nextReset;
                playerData.Warning30MinSent = false;

                DataStore.Players[steamId] = playerData;
                DataStore.Save();
                Plugin.LogInfo($"[TimePlayerControl] Nuevo registro para: {playerName} ({steamId}) con {playerData.RemainingSeconds:F0}s restantes del bloque actual.");
            }
            else
            {
                playerData.PlayerName = playerName;
                if (playerData.NeedsReset(now))
                {
                    playerData.ResetSeconds(DataStore.ServerConfig.DefaultBlockSeconds, DataStore.ServerConfig.ResetHourUtc, now);
                    DataStore.Save();
                    Plugin.LogInfo($"[TimePlayerControl] Tiempo renovado para: {playerName} ({steamId}).");
                }
            }

            if (!playerData.IsExempt && !pauseActive && playerData.RemainingSeconds <= 0)
            {
                kickReason = "Te has quedado sin tiempo";
                SendForceMenuToPeer(peer, kickReason);
                Plugin.LogInfo($"[TimePlayerControl] Bloqueando acceso de {playerName} ({steamId}): {kickReason}");
                return false;
            }

            if (!playerData.IsExempt && playerData.RemainingSeconds <= 0)
            {
                DateTime nextResetLocal = playerData.NextResetTime.ToLocalTime();
                kickReason = $"Te has quedado sin tiempo. Renovacion: {nextResetLocal:dd/MM/yyyy HH:mm}.";
                SendForceMenuToPeer(peer, kickReason);
                return false;
            }

            SendPlayerTimeSyncToPeer(peer, playerData);
            return true;
        }

        public bool IsPauseRuleActive(out int onlineCount, out int registeredCount, out float currentRatio)
        {
            registeredCount = DataStore.Players.Count;
            List<ZNetPeer> peers = ZNet.instance != null ? ZNet.instance.GetPeers() : new List<ZNetPeer>();
            onlineCount = peers != null ? peers.Count : 0;

            if (registeredCount <= 1)
            {
                currentRatio = 0f;
                return false;
            }

            currentRatio = (float)onlineCount / registeredCount;
            return currentRatio >= DataStore.ServerConfig.PauseTimeMinOnlineRatio;
        }

        private void ProcessTimeTick(float deltaSeconds)
        {
            if (ZNet.instance == null) return;
            List<ZNetPeer> peers = ZNet.instance.GetPeers();
            if (peers == null || peers.Count == 0) return;

            bool pauseActive = IsPauseRuleActive(out int onlineCount, out int registeredCount, out float ratio);
            Plugin.LogDebug($"[TimePlayerControl] [TRACE] Tick de control: {peers.Count} conectados, {registeredCount} registrados, ratio={ratio:P0}, pausa_activa={pauseActive}");

            if (pauseActive)
                return;

            double warningThresholdSec = DataStore.ServerConfig.WarningThresholdSeconds;

            foreach (var peer in peers.ToList())
            {
                if (peer == null || !peer.IsReady()) continue;

                string steamId = GetPeerSteamID(peer);
                if (!DataStore.Players.TryGetValue(steamId, out var player)) continue;

                if (player.IsExempt)
                {
                    player.TotalPlayedSeconds += deltaSeconds;
                    SendPlayerTimeSyncToPeer(peer, player);
                    continue;
                }

                player.RemainingSeconds -= deltaSeconds;
                player.TotalPlayedSeconds += deltaSeconds;
                Plugin.LogDebug($"[TimePlayerControl] [TRACE] Revisando {player.PlayerName} ({steamId}): restante={player.RemainingSeconds:F0}s, total={player.TotalPlayedSeconds:F0}s, threshold={warningThresholdSec:F0}s, nextReset={player.NextResetTime:O}");

                SendPlayerTimeSyncToPeer(peer, player);

                if (player.RemainingSeconds <= 0)
                {
                    player.RemainingSeconds = 0;
                    DateTime nextResetLocal = player.NextResetTime.ToLocalTime();
                    string reason = $"Te has quedado sin tiempo. Proxima renovacion: {nextResetLocal:dd/MM/yyyy HH:mm}.";

                    SendCenterMessageToPeer(peer, "Te has quedado sin tiempo");
                    SendForceMenuToPeer(peer, reason);
                    Plugin.LogInfo($"[TimePlayerControl] Jugador {player.PlayerName} ({steamId}) enviado al menu por limite de tiempo.");

                    // Desconexion suave diferida: el cliente primero vuelve al menu con el mensaje.
                    StartCoroutine(DelayedSoftDisconnect(peer, 2.0f, reason));
                    DataStore.Save();
                    continue;
                }
                else if (player.RemainingSeconds <= warningThresholdSec && !player.Warning30MinSent)
                {
                    player.Warning30MinSent = true;
                    int minutesLeft = Mathf.CeilToInt((float)(player.RemainingSeconds / 60.0));
                    string warningMsg = $"ATENCION: Te quedan {player.RemainingSeconds:F0} segundos ({minutesLeft} min) de tiempo de juego.";
                    SendCenterMessageToPeer(peer, warningMsg);
                    SendChatMessageToPeer(peer, warningMsg);
                    Plugin.LogInfo($"[TimePlayerControl] Advertencia enviada a {player.PlayerName} ({player.RemainingSeconds:F0}s restantes).");
                }
            }
        }

        private IEnumerator DelayedSoftDisconnect(ZNetPeer peer, float delaySeconds, string reason)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, delaySeconds));
            SoftDisconnectPeer(peer, reason);
        }

        /// <summary>
        /// Cierra la sesion del peer sin invocar Error/Kick (evita el icono rojo de desconexion).
        /// </summary>
        private static void SoftDisconnectPeer(ZNetPeer peer, string reason)
        {
            if (peer == null) return;

            try
            {
                if (ZNet.instance != null)
                    ZNet.instance.Disconnect(peer);
            }
            catch (Exception ex)
            {
                Plugin.LogInfo($"[TimePlayerControl] SoftDisconnect fallo: {ex.Message}");
            }

            Plugin.LogDebug($"[TimePlayerControl] SoftDisconnect aplicado. Motivo: {reason}");
        }

        private static void DisconnectPeerWithReason(ZNetPeer peer, string reason)
        {
            SoftDisconnectPeer(peer, reason);
        }

        public static void SendForceMenuToPeer(ZNetPeer peer, string reason)
        {
            if (peer == null || ZRoutedRpc.instance == null)
                return;

            ZPackage pkg = new ZPackage();
            pkg.Write(reason ?? "Tiempo agotado");
            ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RpcForceMenu, pkg);
            Plugin.LogInfo($"[Server] Enviado RPC ForceMenu a {peer.m_playerName} ({peer.m_uid}): {reason}");
        }

        private static void SendPlayerTimeSyncToPeer(ZNetPeer peer, PlayerData player)
        {
            if (peer == null || ZRoutedRpc.instance == null || player == null)
                return;

            double blockSeconds = TimeManager.Instance?.DataStore?.ServerConfig?.DefaultBlockSeconds ?? 21600;
            bool showHud = TimeManager.Instance != null && TimeManager.Instance.DataStore != null
                ? TimeManager.Instance.DataStore.ServerConfig.EnableClientHud
                : true;

            double assignedSeconds = player.AssignedSeconds > 0 ? player.AssignedSeconds : blockSeconds;
            double remainingSeconds = Math.Max(0d, player.RemainingSeconds);
            double usedSeconds = Math.Max(0d, assignedSeconds - remainingSeconds);
            if (usedSeconds > assignedSeconds)
                usedSeconds = assignedSeconds;
            double totalPlayedSeconds = Math.Max(0d, player.TotalPlayedSeconds);
            List<LeaderboardEntry> topPlayers = BuildTopPlayedLeaderboard(5);

            ZPackage pkg = new ZPackage();
            pkg.Write(remainingSeconds);
            pkg.Write(assignedSeconds);
            pkg.Write(usedSeconds);
            pkg.Write(player.IsExempt);
            pkg.Write(player.NextResetTime.ToString("O"));
            pkg.Write(showHud);
            pkg.Write(totalPlayedSeconds);
            pkg.Write(topPlayers.Count);
            foreach (var entry in topPlayers)
            {
                pkg.Write(entry.PlayerName ?? "Jugador");
                pkg.Write(entry.TotalPlayedSeconds);
            }

            Plugin.LogDebug($"[Server] Enviando sync a {player.PlayerName}: remaining={remainingSeconds:F0}s, assigned={assignedSeconds:F0}s, used={usedSeconds:F0}s, total={totalPlayedSeconds:F0}s, top={topPlayers.Count}, exempt={player.IsExempt}, hud={showHud}");
            ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RpcTimeSync, pkg);
        }

        private static List<LeaderboardEntry> BuildTopPlayedLeaderboard(int maxEntries)
        {
            var result = new List<LeaderboardEntry>();
            var players = TimeManager.Instance?.DataStore?.Players;
            if (players == null || players.Count == 0)
                return result;

            foreach (var entry in players.Values
                .Where(p => p != null)
                .OrderByDescending(p => p.TotalPlayedSeconds)
                .ThenBy(p => p.PlayerName)
                .Take(Math.Max(1, maxEntries)))
            {
                string name = string.IsNullOrWhiteSpace(entry.PlayerName) ? "Jugador" : entry.PlayerName.Trim();
                result.Add(new LeaderboardEntry
                {
                    PlayerName = name,
                    TotalPlayedSeconds = Math.Max(0d, entry.TotalPlayedSeconds)
                });
            }

            return result;
        }

        public sealed class LeaderboardEntry
        {
            public string PlayerName;
            public double TotalPlayedSeconds;
        }

        public static void SendCenterMessageToPeer(ZNetPeer peer, string message)
        {
            if (string.IsNullOrEmpty(message) || peer == null)
                return;

            TryInvokeMessageHud(peer, "ShowMessage", MessageHud.MessageType.Center, message);
        }

        public static void SendChatMessageToPeer(ZNetPeer peer, string message)
        {
            if (string.IsNullOrEmpty(message) || peer == null)
                return;

            if (TryInvokeChatMethod(peer, "SendTextToPlayer", message))
                return;

            if (TryInvokeChatMethod(peer, "SendTextToClient", message))
                return;

            Plugin.LogDebug($"[TimePlayerControl] No se pudo enviar mensaje directo a {peer.m_playerName}. Mensaje omitido: {message}");
        }

        private static bool TryInvokeChatMethod(ZNetPeer peer, string methodName, string message)
        {
            try
            {
                Type chatType = typeof(Chat);
                var chatInstance = chatType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (chatInstance == null)
                    return false;

                MethodInfo[] methods = chatType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var method in methods.Where(m => m.Name == methodName))
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 2 && parameters[0].ParameterType == typeof(ZNetPeer) && parameters[1].ParameterType == typeof(string))
                    {
                        method.Invoke(chatInstance, new object[] { peer, message });
                        return true;
                    }

                    if (parameters.Length == 2 && parameters[0].ParameterType == typeof(long) && parameters[1].ParameterType == typeof(string))
                    {
                        method.Invoke(chatInstance, new object[] { peer.m_uid, message });
                        return true;
                    }

                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                    {
                        method.Invoke(chatInstance, new object[] { message });
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogInfo($"[TimePlayerControl] No se pudo invocar Chat.{methodName}: {ex.Message}");
            }

            return false;
        }

        private static bool TryInvokeMessageHud(ZNetPeer peer, string methodName, MessageHud.MessageType type, string message)
        {
            try
            {
                Type hudType = typeof(MessageHud);
                var hudInstance = hudType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hudInstance == null)
                    return false;

                MethodInfo method = hudType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(MessageHud.MessageType), typeof(string) }, null);
                if (method == null)
                    return false;

                method.Invoke(hudInstance, new object[] { type, message });
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogInfo($"[TimePlayerControl] No se pudo invocar MessageHud.{methodName}: {ex.Message}");
            }

            return false;
        }

        public bool HandleChatCommand(ZNetPeer peer, string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            string trimmed = text.Trim();
            if (trimmed.Equals("/tiempo", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("!tiempo", StringComparison.OrdinalIgnoreCase))
            {
                RespondPlayerTime(peer);
                return true;
            }
            if (trimmed.Equals("/tiempos", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("!tiempos", StringComparison.OrdinalIgnoreCase))
            {
                RespondAllPlayerTimes(peer);
                return true;
            }
            if (trimmed.Equals("/hud", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("!hud", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("/tiempohud", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("!tiempohud", StringComparison.OrdinalIgnoreCase))
            {
                ToggleHudForPeer(peer);
                return true;
            }

            return false;
        }

        private void ToggleHudForPeer(ZNetPeer peer)
        {
            if (peer == null)
                return;

            DataStore.ServerConfig.EnableClientHud = !DataStore.ServerConfig.EnableClientHud;
            DataStore.Save();

            string steamId = GetPeerSteamID(peer);
            string stateText = DataStore.ServerConfig.EnableClientHud ? "activado" : "desactivado";
            SendChatMessageToPeer(peer, $"HUD de tiempo {stateText}.");
            SendCenterMessageToPeer(peer, $"HUD {stateText}.");

            if (DataStore.Players.TryGetValue(steamId, out var player))
            {
                SendPlayerTimeSyncToPeer(peer, player);
            }
        }

        private void RespondPlayerTime(ZNetPeer peer)
        {
            string steamId = GetPeerSteamID(peer);
            if (!DataStore.Players.TryGetValue(steamId, out var player))
            {
                SendChatMessageToPeer(peer, "No se encontró registro de tu usuario.");
                return;
            }

            TimeSpan ts = TimeSpan.FromSeconds(Math.Max(0, player.RemainingSeconds));
            DateTime resetLocal = player.NextResetTime.ToLocalTime();
            bool isPauseActive = IsPauseRuleActive(out int online, out int registered, out float ratio);

            string statusStr = player.IsExempt
                ? "Tiempo Ilimitado (VIP/Admin)"
                : $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s ({player.RemainingSeconds:F0}s)";

            string pauseStr = isPauseActive
                ? $" ( Pausa colectiva ACTIVADA: {online}/{registered} jugadores - {(ratio * 100):F0}%)"
                : "";

            string totalStr = FormatPlayedTotal(player.TotalPlayedSeconds);
            string response = $" Tiempo restante: {statusStr}. Total jugado: {totalStr}. Renovación: {resetLocal:dd/MM/yyyy HH:mm}.{pauseStr}";
            SendChatMessageToPeer(peer, response);
            SendCenterMessageToPeer(peer, response);
        }

        private static string FormatPlayedTotal(double totalSeconds)
        {
            TimeSpan total = TimeSpan.FromSeconds(Math.Max(0d, totalSeconds));
            if (total.TotalDays >= 1d)
                return $"{(int)total.TotalDays}d {total.Hours}h {total.Minutes}m";
            return $"{(int)total.TotalHours}h {total.Minutes}m {total.Seconds}s";
        }

        private void RespondAllPlayerTimes(ZNetPeer peer)
        {
            List<ZNetPeer> peers = ZNet.instance.GetPeers();
            bool isPauseActive = IsPauseRuleActive(out int onlineCount, out int registeredCount, out float ratio);

            string header = $"Jugadores Conectados ({onlineCount}/{registeredCount} - {(ratio * 100):F0}%):";
            if (isPauseActive)
            {
                header += "  [DESCUENTO PAUSADO AL >= 75%]";
            }
            SendChatMessageToPeer(peer, header);

            foreach (var p in peers)
            {
                string sId = GetPeerSteamID(p);
                string pName = string.IsNullOrEmpty(p.m_playerName) ? "Jugador" : p.m_playerName;
                if (DataStore.Players.TryGetValue(sId, out var data))
                {
                    TimeSpan ts = TimeSpan.FromSeconds(Math.Max(0, data.RemainingSeconds));
                    string timeStr = data.IsExempt ? "Ilimitado" : $"{ts.Hours}h {ts.Minutes}m ({data.RemainingSeconds:F0}s)";
                    SendChatMessageToPeer(peer, $" - {pName}: {timeStr}");
                }
                else
                {
                    SendChatMessageToPeer(peer, $" - {pName}: Sin registro");
                }
            }
        }

        public static class ClientTimeInfo
        {
            public static double CurrentRemainingSeconds { get; set; }
            public static double CurrentMaxSeconds { get; set; }
            public static double CurrentAssignedSeconds { get; set; }
            public static double CurrentUsedSeconds { get; set; }
            public static double TotalPlayedSeconds { get; set; }
            public static bool IsExempt { get; set; }
            public static DateTime? NextReset { get; set; }
            public static bool ShowHud { get; set; } = true;
            public static List<LeaderboardEntry> TopPlayers { get; set; } = new List<LeaderboardEntry>();
            public static string PendingMenuMessage { get; set; }
            public static float PendingMenuMessageUntil { get; set; }
        }

        public class ClientTimeHud : MonoBehaviour
        {
            private bool _enabled = true;
            private bool _compact = true;
            private bool _compactInitialized;
            private GUIStyle _boxStyle;
            private GUIStyle _labelStyle;
            private GUIStyle _titleStyle;
            private GUIStyle _buttonStyle;
            private GUIStyle _compactStyle;
            private Texture2D _bgTexture;
            private Texture2D _bgTextureAlert;
            private Texture2D _progressBgTexture;
            private Texture2D _progressFgTexture;
            private Texture2D _progressAlertTexture;
            private float _flashTimer = 0f;
            private float _lastAppliedOpacity = -1f;
            private float _lastUiScale = -1f;
            private const float FlashInterval = 0.5f;
            private const float ReferenceWidth = 1920f;
            private const float ReferenceHeight = 1080f;

            public void SetEnabled(bool enabled)
            {
                _enabled = enabled;
            }

            public void InvalidateStyles()
            {
                DestroyTexture(ref _bgTexture);
                DestroyTexture(ref _bgTextureAlert);
                DestroyTexture(ref _progressBgTexture);
                DestroyTexture(ref _progressFgTexture);
                DestroyTexture(ref _progressAlertTexture);
                _boxStyle = null;
                _labelStyle = null;
                _titleStyle = null;
                _buttonStyle = null;
                _compactStyle = null;
                _lastAppliedOpacity = -1f;
                _lastUiScale = -1f;
            }

            private void Awake()
            {
                InvalidateStyles();
                _flashTimer = 0f;
                _compactInitialized = false;
            }

            private void OnDestroy()
            {
                InvalidateStyles();
            }

            private void Update()
            {
                if (ClientTimeInfo.CurrentRemainingSeconds <= 30 && !ClientTimeInfo.IsExempt)
                {
                    _flashTimer += Time.deltaTime;
                    if (_flashTimer >= FlashInterval * 2f)
                        _flashTimer = 0f;
                }

                if (!_enabled || !ClientTimeInfo.ShowHud || ZNet.instance == null || ZNet.instance.IsServer())
                    return;

                if (Player.m_localPlayer == null)
                    return;

                // No alternar mientras el jugador escribe en chat
                try
                {
                    if (Chat.instance != null && Chat.instance.HasFocus())
                        return;
                }
                catch { }

                ServerConfig cfg = TimeManager.Instance != null && TimeManager.Instance.DataStore != null
                    ? TimeManager.Instance.DataStore.ServerConfig
                    : null;

                if (!_compactInitialized)
                {
                    _compact = cfg == null || cfg.HudStartCompact;
                    _compactInitialized = true;
                }

                string keyName = cfg != null && !string.IsNullOrWhiteSpace(cfg.HudToggleKey)
                    ? cfg.HudToggleKey.Trim()
                    : "F1";

                if (!TryGetToggleKey(keyName, out KeyCode toggleKey))
                    toggleKey = KeyCode.F1;

                if (Input.GetKeyDown(toggleKey))
                    _compact = !_compact;
            }

            private void OnGUI()
            {
                DrawPendingMenuMessageIfAny();

                if (!_enabled || !ClientTimeInfo.ShowHud || ZNet.instance == null || ZNet.instance.IsServer())
                    return;

                if (Player.m_localPlayer == null)
                    return;

                ServerConfig cfg = TimeManager.Instance != null && TimeManager.Instance.DataStore != null
                    ? TimeManager.Instance.DataStore.ServerConfig
                    : new ServerConfig();

                if (!_compactInitialized)
                {
                    _compact = cfg.HudStartCompact;
                    _compactInitialized = true;
                }

                if (cfg.HudAutoHideOverlays && IsBlockingGameUiVisible())
                    return;

                float uiScale = Mathf.Clamp(Mathf.Min(Screen.width / ReferenceWidth, Screen.height / ReferenceHeight), 0.75f, 2.5f);
                float opacity = Mathf.Clamp01(cfg.HudOpacity <= 0f ? 0.75f : cfg.HudOpacity);
                InitializeStyles(uiScale, opacity);

                float marginX = Mathf.Max(8f, cfg.HudPositionX * uiScale);
                float marginY = Mathf.Max(8f, cfg.HudPositionY * uiScale);
                float width = Mathf.Max(160f * uiScale, cfg.HudWidth * uiScale);

                float pad = 10f * uiScale;
                float line = 18f * uiScale;
                float titleLine = 22f * uiScale;
                float buttonH = 22f * uiScale;
                float progressH = 8f * uiScale;
                float gap = 4f * uiScale;

                bool isAlert = ClientTimeInfo.CurrentRemainingSeconds <= 30 && !ClientTimeInfo.IsExempt;
                bool showAlertFlash = isAlert && _flashTimer >= FlashInterval;
                bool showProgress = cfg.HudShowProgressBar && !ClientTimeInfo.IsExempt;
                string toggleKeyLabel = string.IsNullOrWhiteSpace(cfg.HudToggleKey) ? "F1" : cfg.HudToggleKey.Trim().ToUpperInvariant();
                int topCount = ClientTimeInfo.TopPlayers != null ? Math.Min(5, ClientTimeInfo.TopPlayers.Count) : 0;

                float contentHeight;
                if (_compact)
                {
                    // Solo cronometro + boton; en alerta el color basta (sin texto extra que se desborde).
                    contentHeight = pad + titleLine + (showProgress ? gap + progressH : 0f) + pad;
                }
                else
                {
                    int detailLines = 5; // restante, asignado, usado, total, renovacion
                    if (ClientTimeInfo.IsExempt || isAlert)
                        detailLines++;
                    int rankingLines = topCount > 0 ? (1 + topCount) : 0; // titulo + entradas
                    contentHeight = pad + titleLine + gap + (detailLines * line) + gap + buttonH + pad;
                    if (showProgress)
                        contentHeight += gap + progressH;
                    if (rankingLines > 0)
                        contentHeight += gap + (rankingLines * line);
                }

                float height = cfg.HudHeight > 0f
                    ? Mathf.Max(cfg.HudHeight * uiScale, contentHeight)
                    : contentHeight;

                ResolveAnchoredPosition(cfg.HudAnchor, marginX, marginY, width, height, out float posX, out float posY);

                Rect panelRect = new Rect(posX, posY, width, height);
                DrawSemiTransparentBox(panelRect, showAlertFlash ? _bgTextureAlert : _bgTexture);

                string remaining = ClientTimeInfo.IsExempt ? "Ilimitado" : FormatDuration(ClientTimeInfo.CurrentRemainingSeconds);
                float y = posY + pad;
                float textW = width - (pad * 2f);
                float keyBtnW = 56f * uiScale;

                if (_compact)
                {
                    string compactText = ClientTimeInfo.IsExempt ? "VIP" : remaining;

                    Color prev = GUI.contentColor;
                    if (isAlert)
                        GUI.contentColor = Color.yellow;
                    GUI.Label(new Rect(posX + pad, y, textW - keyBtnW - gap, titleLine), compactText, _compactStyle);
                    GUI.contentColor = prev;

                    Rect expandRect = new Rect(posX + width - pad - keyBtnW, y, keyBtnW, buttonH);
                    if (GUI.Button(expandRect, toggleKeyLabel, _buttonStyle))
                        _compact = false;

                    y += titleLine;
                    if (showProgress)
                    {
                        y += gap;
                        DrawProgressBar(new Rect(posX + pad, y, textW, progressH), GetRemainingRatio(), isAlert);
                    }

                    return;
                }

                GUI.Label(new Rect(posX + pad, y, textW, titleLine), "Tiempo disponible", _titleStyle);
                y += titleLine + gap;

                if (showProgress)
                {
                    DrawProgressBar(new Rect(posX + pad, y, textW, progressH), GetRemainingRatio(), isAlert);
                    y += progressH + gap;
                }

                string assigned = ClientTimeInfo.IsExempt ? "Ilimitado" : FormatDuration(ClientTimeInfo.CurrentAssignedSeconds);
                string used = ClientTimeInfo.IsExempt ? "Ilimitado" : FormatDuration(ClientTimeInfo.CurrentUsedSeconds);
                string totalPlayed = FormatPlayedTotal(ClientTimeInfo.TotalPlayedSeconds);
                string assignHour = GetAssignmentHourLabel();

                GUI.Label(new Rect(posX + pad, y, textW, line), "Restante: " + remaining, _labelStyle);
                y += line;
                GUI.Label(new Rect(posX + pad, y, textW, line), "Asignado: " + assigned, _labelStyle);
                y += line;
                GUI.Label(new Rect(posX + pad, y, textW, line), "Usado: " + used, _labelStyle);
                y += line;
                GUI.Label(new Rect(posX + pad, y, textW, line), "Total jugado: " + totalPlayed, _labelStyle);
                y += line;
                GUI.Label(new Rect(posX + pad, y, textW, line), "Renovacion: " + assignHour, _labelStyle);
                y += line;

                if (ClientTimeInfo.IsExempt)
                {
                    GUI.Label(new Rect(posX + pad, y, textW, line), "VIP/Admin", _labelStyle);
                    y += line;
                }
                else if (isAlert)
                {
                    Color originalColor = GUI.contentColor;
                    GUI.contentColor = Color.yellow;
                    GUI.Label(new Rect(posX + pad, y, textW, line), "POCO TIEMPO", _titleStyle);
                    GUI.contentColor = originalColor;
                    y += line;
                }

                if (topCount > 0)
                {
                    y += gap;
                    GUI.Label(new Rect(posX + pad, y, textW, line), "Top tiempo jugado", _titleStyle);
                    y += line;
                    for (int i = 0; i < topCount; i++)
                    {
                        var entry = ClientTimeInfo.TopPlayers[i];
                        string name = TruncateName(entry.PlayerName, 14);
                        string total = FormatPlayedTotal(entry.TotalPlayedSeconds);
                        GUI.Label(new Rect(posX + pad, y, textW, line), $"{i + 1}. {name}  {total}", _labelStyle);
                        y += line;
                    }
                }

                y += gap;
                Rect collapseRect = new Rect(posX + pad, y, Mathf.Min(130f * uiScale, textW), buttonH);
                if (GUI.Button(collapseRect, "Compactar (" + toggleKeyLabel + ")", _buttonStyle))
                    _compact = true;
            }

            private void DrawPendingMenuMessageIfAny()
            {
                if (string.IsNullOrEmpty(ClientTimeInfo.PendingMenuMessage))
                    return;

                if (Time.realtimeSinceStartup > ClientTimeInfo.PendingMenuMessageUntil)
                {
                    ClientTimeInfo.PendingMenuMessage = null;
                    return;
                }

                float uiScale = Mathf.Clamp(Mathf.Min(Screen.width / ReferenceWidth, Screen.height / ReferenceHeight), 0.75f, 2.5f);
                float boxW = Mathf.Min(520f * uiScale, Screen.width - 40f);
                float boxH = 70f * uiScale;
                float boxX = (Screen.width - boxW) * 0.5f;
                float boxY = Screen.height * 0.18f;

                if (_bgTexture == null)
                    _bgTexture = CreateColorTexture(new Color(0.05f, 0.06f, 0.08f, 0.85f));
                if (_titleStyle == null)
                {
                    _titleStyle = new GUIStyle(GUI.skin.label);
                    _titleStyle.normal.textColor = new Color(1f, 0.82f, 0.28f);
                    _titleStyle.fontSize = Mathf.Max(14, Mathf.RoundToInt(16f * uiScale));
                    _titleStyle.fontStyle = FontStyle.Bold;
                    _titleStyle.alignment = TextAnchor.MiddleCenter;
                    _titleStyle.wordWrap = true;
                }

                Rect box = new Rect(boxX, boxY, boxW, boxH);
                DrawSemiTransparentBox(box, _bgTexture);
                GUI.Label(new Rect(boxX + 12f, boxY + 8f, boxW - 24f, boxH - 16f), ClientTimeInfo.PendingMenuMessage, _titleStyle);
            }

            private static string TruncateName(string name, int maxChars)
            {
                if (string.IsNullOrEmpty(name))
                    return "Jugador";
                if (name.Length <= maxChars)
                    return name;
                return name.Substring(0, Math.Max(1, maxChars - 1)) + "…";
            }

            private static bool TryGetToggleKey(string keyName, out KeyCode keyCode)
            {
                if (Enum.TryParse(keyName, true, out keyCode))
                    return true;

                keyCode = KeyCode.F1;
                return string.Equals(keyName, "F1", StringComparison.OrdinalIgnoreCase);
            }

            private static void ResolveAnchoredPosition(string anchor, float marginX, float marginY, float width, float height, out float posX, out float posY)
            {
                string normalized = string.IsNullOrEmpty(anchor) ? "TopRight" : anchor.Trim();
                switch (normalized.ToLowerInvariant())
                {
                    case "topleft":
                        posX = marginX;
                        posY = marginY;
                        break;
                    case "bottomleft":
                        posX = marginX;
                        posY = Screen.height - height - marginY;
                        break;
                    case "bottomright":
                        posX = Screen.width - width - marginX;
                        posY = Screen.height - height - marginY;
                        break;
                    case "topright":
                    default:
                        posX = Screen.width - width - marginX;
                        posY = marginY;
                        break;
                }

                posX = Mathf.Clamp(posX, 8f, Mathf.Max(8f, Screen.width - width - 8f));
                posY = Mathf.Clamp(posY, 8f, Mathf.Max(8f, Screen.height - height - 8f));
            }

            private static bool IsBlockingGameUiVisible()
            {
                try
                {
                    if (InventoryGui.IsVisible())
                        return true;
                    if (Menu.IsVisible())
                        return true;
                    if (TextInput.IsVisible())
                        return true;
                    if (StoreGui.IsVisible())
                        return true;
                    if (Minimap.IsOpen())
                        return true;
                    if (Chat.instance != null && Chat.instance.HasFocus())
                        return true;
                }
                catch
                {
                    // API de Valheim puede variar entre versiones; no romper el HUD.
                }

                return false;
            }

            private static float GetRemainingRatio()
            {
                if (ClientTimeInfo.IsExempt)
                    return 1f;

                double assigned = ClientTimeInfo.CurrentAssignedSeconds;
                if (assigned <= 0d)
                    return 0f;

                return Mathf.Clamp01((float)(ClientTimeInfo.CurrentRemainingSeconds / assigned));
            }

            private string GetAssignmentHourLabel()
            {
                int resetHourUtc = TimeManager.Instance != null && TimeManager.Instance.DataStore != null
                    ? TimeManager.Instance.DataStore.ServerConfig.ResetHourUtc
                    : 3;

                int localHour = (resetHourUtc + 24 - 3) % 24;
                return localHour.ToString("D2") + ":00";
            }

            private void InitializeStyles(float uiScale, float opacity)
            {
                if (!Mathf.Approximately(_lastAppliedOpacity, opacity) || !Mathf.Approximately(_lastUiScale, uiScale))
                {
                    InvalidateStyles();
                    _lastAppliedOpacity = opacity;
                    _lastUiScale = uiScale;
                }

                if (_bgTexture == null)
                {
                    _bgTexture = CreateColorTexture(new Color(0.05f, 0.06f, 0.08f, opacity));
                }

                if (_bgTextureAlert == null)
                {
                    _bgTextureAlert = CreateColorTexture(new Color(0.55f, 0.12f, 0.12f, Mathf.Min(1f, opacity + 0.1f)));
                }

                if (_progressBgTexture == null)
                {
                    _progressBgTexture = CreateColorTexture(new Color(0.15f, 0.15f, 0.18f, Mathf.Min(1f, opacity + 0.15f)));
                }

                if (_progressFgTexture == null)
                {
                    _progressFgTexture = CreateColorTexture(new Color(0.85f, 0.7f, 0.2f, 0.95f));
                }

                if (_progressAlertTexture == null)
                {
                    _progressAlertTexture = CreateColorTexture(new Color(0.95f, 0.35f, 0.2f, 0.95f));
                }

                int fontSize = Mathf.Max(11, Mathf.RoundToInt(12f * uiScale));
                int titleSize = Mathf.Max(12, Mathf.RoundToInt(13f * uiScale));
                int compactSize = Mathf.Max(12, Mathf.RoundToInt(14f * uiScale));

                if (_boxStyle == null)
                {
                    _boxStyle = new GUIStyle(GUI.skin.box);
                    _boxStyle.normal.background = _bgTexture;
                    _boxStyle.normal.textColor = Color.white;
                    _boxStyle.border = new RectOffset(4, 4, 4, 4);
                }

                if (_titleStyle == null)
                {
                    _titleStyle = new GUIStyle(GUI.skin.label);
                    _titleStyle.normal.textColor = new Color(1f, 0.82f, 0.28f);
                    _titleStyle.fontSize = titleSize;
                    _titleStyle.fontStyle = FontStyle.Bold;
                    _titleStyle.alignment = TextAnchor.MiddleLeft;
                }

                if (_labelStyle == null)
                {
                    _labelStyle = new GUIStyle(GUI.skin.label);
                    _labelStyle.normal.textColor = Color.white;
                    _labelStyle.fontSize = fontSize;
                    _labelStyle.alignment = TextAnchor.MiddleLeft;
                }

                if (_compactStyle == null)
                {
                    _compactStyle = new GUIStyle(GUI.skin.label);
                    _compactStyle.normal.textColor = new Color(1f, 0.9f, 0.55f);
                    _compactStyle.fontSize = compactSize;
                    _compactStyle.fontStyle = FontStyle.Bold;
                    _compactStyle.alignment = TextAnchor.MiddleLeft;
                }

                if (_buttonStyle == null)
                {
                    _buttonStyle = new GUIStyle(GUI.skin.button);
                    _buttonStyle.fontSize = fontSize;
                    _buttonStyle.fontStyle = FontStyle.Bold;
                    _buttonStyle.alignment = TextAnchor.MiddleCenter;
                }
            }

            private void DrawProgressBar(Rect rect, float ratio, bool alert)
            {
                GUI.DrawTexture(rect, _progressBgTexture);
                float fill = Mathf.Clamp01(ratio) * rect.width;
                if (fill > 0.5f)
                {
                    Rect fillRect = new Rect(rect.x, rect.y, fill, rect.height);
                    GUI.DrawTexture(fillRect, alert ? _progressAlertTexture : _progressFgTexture);
                }
            }

            private void DrawSemiTransparentBox(Rect rect, Texture2D bgTexture = null)
            {
                if (bgTexture == null)
                    bgTexture = _bgTexture;

                GUIStyle style = new GUIStyle(_boxStyle);
                style.normal.background = bgTexture;
                GUI.Box(rect, "", style);
            }

            private static Texture2D CreateColorTexture(Color color)
            {
                Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.SetPixel(0, 0, color);
                tex.Apply();
                return tex;
            }

            private static void DestroyTexture(ref Texture2D texture)
            {
                if (texture == null)
                    return;

                UnityEngine.Object.Destroy(texture);
                texture = null;
            }

            private static string FormatDuration(double totalSeconds)
            {
                if (double.IsNaN(totalSeconds) || double.IsInfinity(totalSeconds) || totalSeconds <= 0d)
                    return "00:00:00";

                TimeSpan ts = TimeSpan.FromSeconds(Math.Max(0d, totalSeconds));
                return string.Format("{0:D2}:{1:D2}:{2:D2}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
            }

            private static string FormatPlayedTotal(double totalSeconds)
            {
                TimeSpan total = TimeSpan.FromSeconds(Math.Max(0d, totalSeconds));
                if (total.TotalDays >= 1d)
                    return string.Format("{0}d {1:D2}h {2:D2}m", (int)total.TotalDays, total.Hours, total.Minutes);
                return string.Format("{0:D2}:{1:D2}:{2:D2}", (int)total.TotalHours, total.Minutes, total.Seconds);
            }
        }
    }
}
