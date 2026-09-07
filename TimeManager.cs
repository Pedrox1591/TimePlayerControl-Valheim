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
                _clientRpcRegistered = true;
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

            try
            {
                double remainingSeconds = pkg.ReadDouble();
                double assignedSeconds = pkg.ReadDouble();
                double usedSeconds = pkg.ReadDouble();
                bool exempt = pkg.ReadBool();
                string nextResetIso = pkg.ReadString();
                bool showHud = pkg.ReadBool();
                double totalPlayedSeconds = pkg.ReadDouble();

                // Aplicar YA los datos críticos (el ranking es opcional y no debe tumbar el HUD).
                DateTime? nextReset = null;
                if (!string.IsNullOrEmpty(nextResetIso) && DateTime.TryParse(nextResetIso, out DateTime parsed))
                    nextReset = parsed;

                ClientTimeInfo.CurrentRemainingSeconds = remainingSeconds;
                ClientTimeInfo.CurrentAssignedSeconds = assignedSeconds;
                ClientTimeInfo.CurrentMaxSeconds = assignedSeconds;
                ClientTimeInfo.CurrentUsedSeconds = usedSeconds;
                ClientTimeInfo.IsExempt = exempt;
                ClientTimeInfo.NextReset = nextReset;
                ClientTimeInfo.ShowHud = showHud;
                ClientTimeInfo.TotalPlayedSeconds = Math.Max(0d, totalPlayedSeconds);
                ClientTimeInfo.HasReceivedSync = true;

                var topPlayers = new List<LeaderboardEntry>();
                try
                {
                    int topCount = pkg.ReadInt();
                    if (topCount < 0)
                        topCount = 0;
                    if (topCount > 20)
                        topCount = 20;

                    for (int i = 0; i < topCount; i++)
                    {
                        string name = pkg.ReadString();
                        double total = pkg.ReadDouble();
                        topPlayers.Add(new LeaderboardEntry
                        {
                            PlayerName = string.IsNullOrEmpty(name) ? "Player" : name,
                            TotalPlayedSeconds = Math.Max(0d, total)
                        });
                    }
                }
                catch (Exception topEx)
                {
                    Plugin.LogDebug($"[Client] Ranking omitido en sync: {topEx.Message}");
                }

                ClientTimeInfo.TopPlayers = topPlayers;
                Plugin.LogDebug($"[Client] Sync OK: remaining={remainingSeconds:F0}s, assigned={assignedSeconds:F0}s, used={usedSeconds:F0}s, total={totalPlayedSeconds:F0}s, top={topPlayers.Count}, exempt={exempt}, hud={showHud}");
            }
            catch (Exception ex)
            {
                Plugin.LogInfo($"[Client] Error en RPC_TimeSync: {ex.Message}");
            }
        }

        private void RPC_ForceMenu(long sender, ZPackage pkg)
        {
            string reason = Loc.T("out_of_time");
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
            string message = string.IsNullOrWhiteSpace(reason) ? Loc.T("out_of_time") : reason.Trim();
            ClientTimeInfo.PendingMenuMessage = message;
            ClientTimeInfo.PendingMenuMessageUntil = Time.realtimeSinceStartup + 12f;

            try
            {
                if (MessageHud.instance != null && !string.IsNullOrEmpty(message))
                {
                    // Firmas varían entre versiones de Valheim; probar las conocidas.
                    var hud = MessageHud.instance;
                    var methods = typeof(MessageHud).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    bool shown = false;
                    foreach (var method in methods)
                    {
                        if (method.Name != "ShowMessage")
                            continue;
                        var pars = method.GetParameters();
                        try
                        {
                            if (pars.Length == 2 && pars[0].ParameterType == typeof(MessageHud.MessageType) && pars[1].ParameterType == typeof(string))
                            {
                                method.Invoke(hud, new object[] { MessageHud.MessageType.Center, message });
                                shown = true;
                                break;
                            }
                            if (pars.Length == 3 && pars[0].ParameterType == typeof(MessageHud.MessageType) && pars[1].ParameterType == typeof(string))
                            {
                                object third = pars[2].ParameterType.IsValueType ? Activator.CreateInstance(pars[2].ParameterType) : null;
                                method.Invoke(hud, new object[] { MessageHud.MessageType.Center, message, third });
                                shown = true;
                                break;
                            }
                        }
                        catch
                        {
                            // probar siguiente overload
                        }
                    }
                    if (!shown)
                        Plugin.LogDebug("[Client] No se encontro overload compatible de MessageHud.ShowMessage.");
                }
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

            // No pisar la posición mientras el jugador arrastra el HUD.
            if (_clientHud != null && _clientHud.IsDragging)
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
            string playerName = string.IsNullOrEmpty(peer.m_playerName) ? Loc.T("player_fallback") : peer.m_playerName;
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
                kickReason = Loc.T("out_of_time");
                SendForceMenuToPeer(peer, kickReason);
                Plugin.LogInfo($"[TimePlayerControl] Bloqueando acceso de {playerName} ({steamId}): {kickReason}");
                return false;
            }

            if (!playerData.IsExempt && playerData.RemainingSeconds <= 0)
            {
                DateTime nextResetLocal = playerData.NextResetTime.ToLocalTime();
                kickReason = Loc.Tf("out_of_time_renewal", nextResetLocal.ToString("dd/MM/yyyy HH:mm"));
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
                    string reason = Loc.Tf("out_of_time_next", nextResetLocal.ToString("dd/MM/yyyy HH:mm"));

                    SendCenterMessageToPeer(peer, Loc.T("out_of_time"));
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
                    string warningMsg = Loc.Tf("warning_time", player.RemainingSeconds, minutesLeft);
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
            pkg.Write(reason ?? Loc.T("time_exhausted"));
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
            pkg.Write((int)topPlayers.Count);
            foreach (var entry in topPlayers)
            {
                pkg.Write(entry.PlayerName ?? "Player");
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
                string name = string.IsNullOrWhiteSpace(entry.PlayerName) ? Loc.T("player_fallback") : entry.PlayerName.Trim();
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
            string stateText = DataStore.ServerConfig.EnableClientHud ? Loc.T("hud_enabled") : Loc.T("hud_disabled");
            SendChatMessageToPeer(peer, Loc.Tf("hud_toggle", stateText));
            SendCenterMessageToPeer(peer, Loc.Tf("hud_toggle_short", stateText));

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
                SendChatMessageToPeer(peer, Loc.T("no_player_record"));
                return;
            }

            TimeSpan ts = TimeSpan.FromSeconds(Math.Max(0, player.RemainingSeconds));
            DateTime resetLocal = player.NextResetTime.ToLocalTime();
            bool isPauseActive = IsPauseRuleActive(out int online, out int registered, out float ratio);

            string statusStr = player.IsExempt
                ? Loc.T("time_unlimited_vip")
                : $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s ({player.RemainingSeconds:F0}s)";

            string pauseStr = isPauseActive
                ? Loc.Tf("pause_active", online, registered, ratio * 100f)
                : "";

            string totalStr = FormatPlayedTotal(player.TotalPlayedSeconds);
            string response = Loc.Tf("time_status", statusStr, totalStr, resetLocal.ToString("dd/MM/yyyy HH:mm"), pauseStr);
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

            string header = Loc.Tf("players_online", onlineCount, registeredCount, ratio * 100f);
            if (isPauseActive)
            {
                header += Loc.T("pause_discount");
            }
            SendChatMessageToPeer(peer, header);

            foreach (var p in peers)
            {
                string sId = GetPeerSteamID(p);
                string pName = string.IsNullOrEmpty(p.m_playerName) ? Loc.T("player_fallback") : p.m_playerName;
                if (DataStore.Players.TryGetValue(sId, out var data))
                {
                    TimeSpan ts = TimeSpan.FromSeconds(Math.Max(0, data.RemainingSeconds));
                    string timeStr = data.IsExempt ? Loc.T("unlimited") : $"{ts.Hours}h {ts.Minutes}m ({data.RemainingSeconds:F0}s)";
                    SendChatMessageToPeer(peer, $" - {pName}: {timeStr}");
                }
                else
                {
                    SendChatMessageToPeer(peer, $" - {pName}: {Loc.T("no_record")}");
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
            public static bool HasReceivedSync { get; set; }
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
            private bool _dragging;
            private Vector2 _dragOffset;
            private bool _dragMoved;
            private float _runtimePosX;
            private float _runtimePosY;
            private bool _hasRuntimePos;
            private const float FlashInterval = 0.5f;
            private const float ReferenceWidth = 1920f;
            private const float ReferenceHeight = 1080f;

            public bool IsDragging => _dragging;

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
                if (ClientTimeInfo.CurrentRemainingSeconds <= 30 && !ClientTimeInfo.IsExempt && ClientTimeInfo.HasReceivedSync)
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

                bool pauseMenuOpen = IsPauseMenuVisible();
                // Con Escape abierto dejamos el HUD visible para poder arrastrarlo.
                if (cfg.HudAutoHideOverlays && IsBlockingGameUiVisible(excludePauseMenu: true))
                    return;

                float gameHudScale = GetGameHudScaleFactor();
                float uiScale = Mathf.Clamp(
                    Mathf.Min(Screen.width / ReferenceWidth, Screen.height / ReferenceHeight) * gameHudScale,
                    0.6f,
                    3f);
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

                bool isAlert = ClientTimeInfo.HasReceivedSync
                    && ClientTimeInfo.CurrentRemainingSeconds <= 30
                    && !ClientTimeInfo.IsExempt;
                bool showAlertFlash = isAlert && _flashTimer >= FlashInterval;
                bool showProgress = cfg.HudShowProgressBar && !ClientTimeInfo.IsExempt && ClientTimeInfo.HasReceivedSync;
                string toggleKeyLabel = string.IsNullOrWhiteSpace(cfg.HudToggleKey) ? "F1" : cfg.HudToggleKey.Trim().ToUpperInvariant();
                int topCount = ClientTimeInfo.HasReceivedSync && ClientTimeInfo.TopPlayers != null
                    ? Math.Min(5, ClientTimeInfo.TopPlayers.Count)
                    : 0;

                float contentHeight;
                if (_compact)
                {
                    contentHeight = pad + titleLine + (showProgress ? gap + progressH : 0f) + pad;
                    if (pauseMenuOpen)
                        contentHeight += gap + buttonH; // "Bajo mapa"
                }
                else
                {
                    int detailLines = 5;
                    if (!ClientTimeInfo.HasReceivedSync)
                        detailLines += 2; // syncing + no_sync
                    if (ClientTimeInfo.IsExempt || isAlert)
                        detailLines++;
                    int rankingLines = topCount > 0 ? (1 + topCount) : 0;
                    contentHeight = pad + titleLine + gap + (detailLines * line) + gap + buttonH + pad;
                    if (showProgress)
                        contentHeight += gap + progressH;
                    if (rankingLines > 0)
                        contentHeight += gap + (rankingLines * line);
                    if (pauseMenuOpen)
                        contentHeight += gap + buttonH; // fila "Bajo mapa"
                }

                float height = cfg.HudHeight > 0f
                    ? Mathf.Max(cfg.HudHeight * uiScale, contentHeight)
                    : contentHeight;

                ResolveHudPosition(cfg, marginX, marginY, width, height, gameHudScale, out float posX, out float posY);

                // Zonas de botones (se calculan antes del drag para que el clic no lo robe).
                float textW = width - (pad * 2f);
                float keyBtnW = 56f * uiScale;
                Rect expandRect = new Rect(posX + width - pad - keyBtnW, posY + pad, keyBtnW, buttonH);
                float btnRowY = EstimateButtonRowY(posY, pad, titleLine, gap, line, progressH, buttonH, showProgress, isAlert, topCount, _compact);
                float compactBtnW = Mathf.Min(130f * uiScale, textW);
                Rect collapseRect = new Rect(posX + pad, btnRowY, compactBtnW, buttonH);
                Rect resetRect = new Rect(posX + pad, btnRowY + buttonH + gap, textW, buttonH);

                HandleHudDragging(pauseMenuOpen, ref posX, ref posY, width, height, cfg, expandRect, collapseRect, resetRect);

                // Recalcular rects tras posible drag
                expandRect = new Rect(posX + width - pad - keyBtnW, posY + pad, keyBtnW, buttonH);
                btnRowY = EstimateButtonRowY(posY, pad, titleLine, gap, line, progressH, buttonH, showProgress, isAlert, topCount, _compact);
                collapseRect = new Rect(posX + pad, btnRowY, compactBtnW, buttonH);
                resetRect = new Rect(posX + pad, btnRowY + buttonH + gap, textW, buttonH);

                Rect panelRect = new Rect(posX, posY, width, height);
                DrawSemiTransparentBox(panelRect, showAlertFlash ? _bgTextureAlert : _bgTexture);

                if (pauseMenuOpen)
                {
                    Color prev = GUI.color;
                    GUI.color = new Color(1f, 0.85f, 0.3f, 0.9f);
                    GUI.Box(panelRect, GUIContent.none);
                    GUI.color = prev;
                }

                string remaining = !ClientTimeInfo.HasReceivedSync
                    ? "—"
                    : (ClientTimeInfo.IsExempt ? Loc.T("unlimited") : FormatDuration(ClientTimeInfo.CurrentRemainingSeconds));
                float y = posY + pad;

                if (_compact)
                {
                    string compactText;
                    if (!ClientTimeInfo.HasReceivedSync)
                        compactText = Loc.T("syncing");
                    else if (ClientTimeInfo.IsExempt)
                        compactText = Loc.T("vip");
                    else
                        compactText = remaining;

                    Color prev = GUI.contentColor;
                    if (isAlert)
                        GUI.contentColor = Color.yellow;
                    GUI.Label(new Rect(posX + pad, y, textW - keyBtnW - gap, titleLine), compactText, _compactStyle);
                    GUI.contentColor = prev;

                    if (GUI.Button(expandRect, toggleKeyLabel, _buttonStyle))
                        _compact = false;

                    y += titleLine;
                    if (showProgress)
                    {
                        y += gap;
                        DrawProgressBar(new Rect(posX + pad, y, textW, progressH), GetRemainingRatio(), isAlert);
                    }

                    if (pauseMenuOpen)
                    {
                        if (GUI.Button(resetRect, Loc.T("below_map"), _buttonStyle))
                            ResetHudToMinimap(cfg, marginX, marginY, width, height, gameHudScale);
                    }

                    return;
                }

                GUI.Label(new Rect(posX + pad, y, textW, titleLine), Loc.T("time_available"), _titleStyle);
                y += titleLine + gap;

                if (showProgress)
                {
                    DrawProgressBar(new Rect(posX + pad, y, textW, progressH), GetRemainingRatio(), isAlert);
                    y += progressH + gap;
                }

                string assigned = !ClientTimeInfo.HasReceivedSync
                    ? "—"
                    : (ClientTimeInfo.IsExempt ? Loc.T("unlimited") : FormatDuration(ClientTimeInfo.CurrentAssignedSeconds));
                string used = !ClientTimeInfo.HasReceivedSync
                    ? "—"
                    : (ClientTimeInfo.IsExempt ? Loc.T("unlimited") : FormatDuration(ClientTimeInfo.CurrentUsedSeconds));
                string totalPlayed = !ClientTimeInfo.HasReceivedSync
                    ? "—"
                    : FormatPlayedTotal(ClientTimeInfo.TotalPlayedSeconds);
                string assignHour = GetAssignmentHourLabel();

                if (!ClientTimeInfo.HasReceivedSync)
                {
                    GUI.Label(new Rect(posX + pad, y, textW, line), Loc.T("syncing"), _labelStyle);
                    y += line;
                    GUI.Label(new Rect(posX + pad, y, textW, line), Loc.T("no_sync"), _labelStyle);
                    y += line;
                }

                GUI.Label(new Rect(posX + pad, y, textW, line), Loc.Tf("label_remaining", remaining), _labelStyle);
                y += line;
                GUI.Label(new Rect(posX + pad, y, textW, line), Loc.Tf("label_assigned", assigned), _labelStyle);
                y += line;
                GUI.Label(new Rect(posX + pad, y, textW, line), Loc.Tf("label_used", used), _labelStyle);
                y += line;
                GUI.Label(new Rect(posX + pad, y, textW, line), Loc.Tf("label_total", totalPlayed), _labelStyle);
                y += line;
                GUI.Label(new Rect(posX + pad, y, textW, line), Loc.Tf("label_renewal", assignHour), _labelStyle);
                y += line;

                if (ClientTimeInfo.IsExempt)
                {
                    GUI.Label(new Rect(posX + pad, y, textW, line), Loc.T("vip_admin"), _labelStyle);
                    y += line;
                }
                else if (isAlert)
                {
                    Color originalColor = GUI.contentColor;
                    GUI.contentColor = Color.yellow;
                    GUI.Label(new Rect(posX + pad, y, textW, line), Loc.T("low_time"), _titleStyle);
                    GUI.contentColor = originalColor;
                    y += line;
                }

                if (topCount > 0)
                {
                    y += gap;
                    GUI.Label(new Rect(posX + pad, y, textW, line), Loc.T("top_played"), _titleStyle);
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
                collapseRect = new Rect(posX + pad, y, compactBtnW, buttonH);
                if (GUI.Button(collapseRect, Loc.Tf("compact_with_key", toggleKeyLabel), _buttonStyle))
                    _compact = true;

                if (pauseMenuOpen)
                {
                    resetRect = new Rect(posX + pad, y + buttonH + gap, textW, buttonH);
                    if (GUI.Button(resetRect, Loc.T("below_map"), _buttonStyle))
                        ResetHudToMinimap(cfg, marginX, marginY, width, height, gameHudScale);
                }
            }

            private static float EstimateButtonRowY(
                float posY, float pad, float titleLine, float gap, float line, float progressH, float buttonH,
                bool showProgress, bool isAlert, int topCount, bool compact)
            {
                if (compact)
                {
                    float y = posY + pad + titleLine;
                    if (showProgress)
                        y += gap + progressH;
                    y += gap;
                    return y;
                }

                float y2 = posY + pad + titleLine + gap;
                if (showProgress)
                    y2 += progressH + gap;
                int detailLines = 5;
                if (isAlert || ClientTimeInfo.IsExempt)
                    detailLines++;
                y2 += detailLines * line;
                if (topCount > 0)
                    y2 += gap + (1 + topCount) * line;
                y2 += gap;
                return y2;
            }

            private void HandleHudDragging(
                bool pauseMenuOpen,
                ref float posX,
                ref float posY,
                float width,
                float height,
                ServerConfig cfg,
                params Rect[] ignoreRects)
            {
                if (!pauseMenuOpen)
                {
                    if (_dragging)
                        EndDrag(posX, posY, width, height, cfg);
                    return;
                }

                Event e = Event.current;
                Rect panelRect = new Rect(posX, posY, width, height);

                if (e.type == EventType.MouseDown && e.button == 0 && panelRect.Contains(e.mousePosition))
                {
                    for (int i = 0; i < ignoreRects.Length; i++)
                    {
                        if (ignoreRects[i].width > 1f && ignoreRects[i].Contains(e.mousePosition))
                            return; // dejar el clic para GUI.Button
                    }

                    _dragging = true;
                    _dragMoved = false;
                    _dragOffset = e.mousePosition - new Vector2(posX, posY);
                    e.Use();
                }

                if (_dragging && e.type == EventType.MouseDrag)
                {
                    Vector2 next = e.mousePosition - _dragOffset;
                    if (Vector2.Distance(next, new Vector2(posX, posY)) > 1f)
                        _dragMoved = true;
                    posX = next.x;
                    posY = next.y;
                    ClampHudPosition(ref posX, ref posY, width, height);
                    _runtimePosX = posX;
                    _runtimePosY = posY;
                    _hasRuntimePos = true;
                    e.Use();
                }

                if (_dragging && e.type == EventType.MouseUp)
                {
                    EndDrag(posX, posY, width, height, cfg);
                    e.Use();
                }
            }

            private void EndDrag(float posX, float posY, float width, float height, ServerConfig cfg)
            {
                _dragging = false;
                ClampHudPosition(ref posX, ref posY, width, height);

                if (!_dragMoved || cfg == null || TimeManager.Instance?.DataStore == null)
                    return;

                _runtimePosX = posX;
                _runtimePosY = posY;
                _hasRuntimePos = true;
                cfg.HudUseCustomPosition = true;
                cfg.HudCustomNormX = Screen.width > 0 ? posX / Screen.width : 0f;
                cfg.HudCustomNormY = Screen.height > 0 ? posY / Screen.height : 0f;
                TimeManager.Instance.DataStore.ServerConfig = cfg;
                TimeManager.Instance.DataStore.Save();
                Plugin.LogInfo($"[Client] HUD reposicionado: norm=({cfg.HudCustomNormX:F3},{cfg.HudCustomNormY:F3})");
            }

            private void ResetHudToMinimap(ServerConfig cfg, float marginX, float marginY, float width, float height, float gameHudScale)
            {
                if (cfg == null || TimeManager.Instance?.DataStore == null)
                    return;

                cfg.HudUseCustomPosition = false;
                cfg.HudSnapBelowMinimap = true;
                cfg.HudCustomNormX = 0f;
                cfg.HudCustomNormY = 0f;
                _dragging = false;
                _dragMoved = false;
                _hasRuntimePos = false;

                // Aplicar al instante (mismo frame / siguiente paint).
                ResolveHudPosition(cfg, marginX, marginY, width, height, gameHudScale, out float posX, out float posY);
                _runtimePosX = posX;
                _runtimePosY = posY;
                // Guardar como runtime temporal solo para este frame; sin custom flag.
                _hasRuntimePos = false;

                TimeManager.Instance.DataStore.ServerConfig = cfg;
                TimeManager.Instance.DataStore.Save();
                Plugin.LogInfo($"[Client] HUD reseteado debajo del minimapa @ ({posX:F0},{posY:F0}).");
            }

            private void ResolveHudPosition(ServerConfig cfg, float marginX, float marginY, float width, float height, float gameHudScale, out float posX, out float posY)
            {
                if (_hasRuntimePos && (_dragging || cfg.HudUseCustomPosition))
                {
                    posX = _runtimePosX;
                    posY = _runtimePosY;
                    ClampHudPosition(ref posX, ref posY, width, height);
                    return;
                }

                if (cfg.HudUseCustomPosition)
                {
                    posX = Mathf.Clamp01(cfg.HudCustomNormX) * Screen.width;
                    posY = Mathf.Clamp01(cfg.HudCustomNormY) * Screen.height;
                    ClampHudPosition(ref posX, ref posY, width, height);
                    _runtimePosX = posX;
                    _runtimePosY = posY;
                    _hasRuntimePos = true;
                    return;
                }

                if (cfg.HudSnapBelowMinimap && TryGetMinimapScreenRect(out Rect miniRect))
                {
                    float gapPx = Mathf.Max(2f, cfg.HudMinimapGap * gameHudScale * (Screen.height / ReferenceHeight));
                    posX = miniRect.xMax - width;
                    posY = miniRect.yMax + gapPx;
                    ClampHudPosition(ref posX, ref posY, width, height);
                    return;
                }

                // Fallback si no se puede leer el minimapa: estimar tamaño tipico @ HUD scale.
                if (cfg.HudSnapBelowMinimap)
                {
                    float estimatedMini = 250f * gameHudScale * (Screen.height / ReferenceHeight);
                    float edge = 16f * gameHudScale * (Screen.height / ReferenceHeight);
                    float gapPx = Mathf.Max(2f, cfg.HudMinimapGap * gameHudScale * (Screen.height / ReferenceHeight));
                    posX = Screen.width - width - edge;
                    posY = edge + estimatedMini + gapPx;
                    ClampHudPosition(ref posX, ref posY, width, height);
                    return;
                }

                ResolveAnchoredPosition(cfg.HudAnchor, marginX, marginY, width, height, out posX, out posY);
            }

            private static void ClampHudPosition(ref float posX, ref float posY, float width, float height)
            {
                posX = Mathf.Clamp(posX, 8f, Mathf.Max(8f, Screen.width - width - 8f));
                posY = Mathf.Clamp(posY, 8f, Mathf.Max(8f, Screen.height - height - 8f));
            }

            /// <summary>
            /// Factor del HUD scale del juego (1.0 = 100%).
            /// </summary>
            private static float GetGameHudScaleFactor()
            {
                try
                {
                    if (Minimap.instance != null)
                    {
                        var field = typeof(Minimap).GetField("m_guiScale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field != null)
                        {
                            float raw = Convert.ToSingle(field.GetValue(Minimap.instance));
                            if (raw > 0.01f)
                                return raw > 5f ? raw / 100f : raw;
                        }
                    }
                }
                catch { }

                try
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type t = asm.GetType("GuiScaler") ?? asm.GetType("GUIScaler");
                        if (t == null)
                            continue;

                        var prop = t.GetProperty("Scale", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                                   ?? t.GetProperty("GuiScale", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        if (prop != null)
                        {
                            float raw = Convert.ToSingle(prop.GetValue(null, null));
                            if (raw > 0.01f)
                                return raw > 5f ? raw / 100f : raw;
                        }

                        var field = t.GetField("m_largeGuiScale", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                                    ?? t.GetField("m_guiScale", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field != null)
                        {
                            float raw = Convert.ToSingle(field.GetValue(null));
                            if (raw > 0.01f)
                                return raw > 5f ? raw / 100f : raw;
                        }
                    }
                }
                catch { }

                try
                {
                    if (PlayerPrefs.HasKey("GuiScale"))
                    {
                        float raw = PlayerPrefs.GetFloat("GuiScale", 1f);
                        if (raw > 0.01f)
                            return raw > 5f ? raw / 100f : raw;
                    }
                }
                catch { }

                return 1f;
            }

            /// <summary>
            /// Rect del minimapa pequeño en coordenadas IMGUI (origen arriba-izquierda).
            /// </summary>
            private static bool TryGetMinimapScreenRect(out Rect imguiRect)
            {
                imguiRect = default;
                try
                {
                    if (Minimap.instance == null)
                        return false;

                    RectTransform rt = null;
                    var smallRootField = typeof(Minimap).GetField("m_smallRoot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (smallRootField != null)
                    {
                        object val = smallRootField.GetValue(Minimap.instance);
                        if (val is GameObject go && go != null && go.activeInHierarchy)
                            rt = go.GetComponent<RectTransform>();
                        else if (val is RectTransform directRt && directRt.gameObject.activeInHierarchy)
                            rt = directRt;
                        else if (val is Component comp && comp.gameObject.activeInHierarchy)
                            rt = comp.GetComponent<RectTransform>() ?? comp.transform as RectTransform;
                    }

                    if (rt == null)
                    {
                        var mapField = typeof(Minimap).GetField("m_mapImageSmall", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (mapField != null)
                        {
                            object val = mapField.GetValue(Minimap.instance);
                            if (val is Component comp)
                                rt = comp.GetComponent<RectTransform>() ?? comp.transform as RectTransform;
                        }
                    }

                    if (rt == null)
                        return false;

                    Vector3[] corners = new Vector3[4];
                    rt.GetWorldCorners(corners);
                    // corners: 0=BL, 1=TL, 2=TR, 3=BR (Unity screen: Y hacia arriba)
                    float xMin = corners.Min(c => c.x);
                    float xMax = corners.Max(c => c.x);
                    float yMinScreen = corners.Min(c => c.y);
                    float yMaxScreen = corners.Max(c => c.y);

                    float imguiTop = Screen.height - yMaxScreen;
                    float imguiBottom = Screen.height - yMinScreen;
                    imguiRect = new Rect(xMin, imguiTop, xMax - xMin, imguiBottom - imguiTop);
                    return imguiRect.width > 8f && imguiRect.height > 8f;
                }
                catch
                {
                    return false;
                }
            }

            private static bool IsPauseMenuVisible()
            {
                try
                {
                    return Menu.IsVisible();
                }
                catch
                {
                    return false;
                }
            }

            private static string TruncateName(string name, int maxChars)
            {
                if (string.IsNullOrEmpty(name))
                    return Loc.T("player_fallback");
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

                ClampHudPosition(ref posX, ref posY, width, height);
            }

            private static bool IsBlockingGameUiVisible(bool excludePauseMenu = false)
            {
                try
                {
                    if (InventoryGui.IsVisible())
                        return true;
                    if (!excludePauseMenu && Menu.IsVisible())
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
