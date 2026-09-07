using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TimePlayerControl
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;
        private Harmony _harmony;

        public static bool IsInfoLoggingEnabled()
        {
            if (TimeManager.Instance != null && TimeManager.Instance.DataStore != null)
                return TimeManager.Instance.DataStore.ServerConfig.EnableInfoLogs;

            return true;
        }

        public static bool IsDebugLoggingEnabled()
        {
            if (TimeManager.Instance != null && TimeManager.Instance.DataStore != null)
                return TimeManager.Instance.DataStore.ServerConfig.EnableDebugLogs;

            return false;
        }

        public static void LogInfo(string message)
        {
            if (Logger == null || !IsInfoLoggingEnabled()) return;
            Logger.LogInfo(message);
        }

        public static void LogDebug(string message)
        {
            if (Logger == null || !IsDebugLoggingEnabled()) return;
            Logger.LogInfo($"[DEBUG] {message}");
        }

        private void Awake()
        {
            Logger = base.Logger;
            LogInfo($"[TimePlayerControl] Cargando plugin v{MyPluginInfo.PLUGIN_VERSION}...");

            // Instanciar GameObject con TimeManager
            GameObject managerObj = new GameObject("TimePlayerControlManager");
            DontDestroyOnLoad(managerObj);
            managerObj.AddComponent<TimeManager>();

            // Aplicar parches Harmony
            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();

            LogInfo("[TimePlayerControl] Plugin cargado e iniciado exitosamente!");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
