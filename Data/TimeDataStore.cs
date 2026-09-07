using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Newtonsoft.Json;

namespace TimePlayerControl.Data
{
    public class TimeDataStore
    {
        public ServerConfig ServerConfig { get; set; } = new ServerConfig();
        public Dictionary<string, PlayerData> Players { get; set; } = new Dictionary<string, PlayerData>();

        private static readonly string FilePath = Path.Combine(Paths.ConfigPath, "TimePlayerControl.json");
        private static readonly object FileLock = new object();

        public static TimeDataStore Load()
        {
            lock (FileLock)
            {
                try
                {
                    if (File.Exists(FilePath))
                    {
                        string json = File.ReadAllText(FilePath);
                        var store = JsonConvert.DeserializeObject<TimeDataStore>(json);
                        if (store != null)
                        {
                            if (store.ServerConfig == null) store.ServerConfig = new ServerConfig();
                            if (store.Players == null) store.Players = new Dictionary<string, PlayerData>();
                            Plugin.LogDebug($"[TimePlayerControl] JSON recargado correctamente ({store.Players.Count} jugadores). ");
                            return store;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger?.LogError($"[TimePlayerControl] Error al cargar JSON: {ex.Message}");
                }

                var defaultStore = new TimeDataStore();
                defaultStore.Save();
                return defaultStore;
            }
        }

        public void Save()
        {
            lock (FileLock)
            {
                try
                {
                    string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                    File.WriteAllText(FilePath, json);
                }
                catch (Exception ex)
                {
                    Plugin.Logger?.LogError($"[TimePlayerControl] Error al guardar JSON: {ex.Message}");
                }
            }
        }
    }
}
