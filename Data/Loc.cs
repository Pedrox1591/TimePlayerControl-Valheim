using System;
using System.Collections.Generic;

namespace TimePlayerControl
{
    /// <summary>
    /// Textos ES/EN. El idioma se toma del JSON local (cliente = HUD, servidor = mensajes).
    /// </summary>
    public static class Loc
    {
        private static readonly Dictionary<string, string> Es = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["player_fallback"] = "Jugador",
            ["unlimited"] = "Ilimitado",
            ["vip"] = "VIP",
            ["vip_admin"] = "VIP/Admin",
            ["time_available"] = "Tiempo disponible",
            ["remaining"] = "Restante",
            ["assigned"] = "Asignado",
            ["used"] = "Usado",
            ["total_played"] = "Total jugado",
            ["renewal"] = "Renovacion",
            ["low_time"] = "POCO TIEMPO",
            ["top_played"] = "Top tiempo jugado",
            ["compact"] = "Compactar",
            ["below_map"] = "Bajo mapa",
            ["out_of_time"] = "Te has quedado sin tiempo",
            ["out_of_time_renewal"] = "Te has quedado sin tiempo. Renovacion: {0}.",
            ["out_of_time_next"] = "Te has quedado sin tiempo. Proxima renovacion: {0}.",
            ["time_exhausted"] = "Tiempo agotado",
            ["warning_time"] = "ATENCION: Te quedan {0:F0} segundos ({1} min) de tiempo de juego.",
            ["hud_enabled"] = "activado",
            ["hud_disabled"] = "desactivado",
            ["hud_toggle"] = "HUD de tiempo {0}.",
            ["hud_toggle_short"] = "HUD {0}.",
            ["no_player_record"] = "No se encontro registro de tu usuario.",
            ["time_unlimited_vip"] = "Tiempo Ilimitado (VIP/Admin)",
            ["pause_active"] = " ( Pausa colectiva ACTIVADA: {0}/{1} jugadores - {2:F0}%)",
            ["time_status"] = " Tiempo restante: {0}. Total jugado: {1}. Renovacion: {2}.{3}",
            ["players_online"] = "Jugadores Conectados ({0}/{1} - {2:F0}%):",
            ["pause_discount"] = "  [DESCUENTO PAUSADO]",
            ["no_record"] = "Sin registro",
            ["label_remaining"] = "Restante: {0}",
            ["label_assigned"] = "Asignado: {0}",
            ["label_used"] = "Usado: {0}",
            ["label_total"] = "Total jugado: {0}",
            ["label_renewal"] = "Renovacion: {0}",
            ["compact_with_key"] = "Compactar ({0})",
            ["syncing"] = "Sincronizando...",
            ["no_sync"] = "Sin datos del servidor",
            ["pause_hud"] = "Pausa colectiva",
            ["pause_hud_short"] = "PAUSA",
            ["label_players"] = "Jugadores: {0}/{1}",
            ["label_players_short"] = "{0}/{1}",
            ["pause_at"] = "Pausa a {0}/{1}",
            ["players_pause"] = "{0}/{1}  ·  PAUSA",
        };

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["player_fallback"] = "Player",
            ["unlimited"] = "Unlimited",
            ["vip"] = "VIP",
            ["vip_admin"] = "VIP/Admin",
            ["time_available"] = "Available time",
            ["remaining"] = "Remaining",
            ["assigned"] = "Assigned",
            ["used"] = "Used",
            ["total_played"] = "Total played",
            ["renewal"] = "Renewal",
            ["low_time"] = "LOW TIME",
            ["top_played"] = "Top playtime",
            ["compact"] = "Compact",
            ["below_map"] = "Below map",
            ["out_of_time"] = "You have run out of time",
            ["out_of_time_renewal"] = "You have run out of time. Renewal: {0}.",
            ["out_of_time_next"] = "You have run out of time. Next renewal: {0}.",
            ["time_exhausted"] = "Time exhausted",
            ["warning_time"] = "WARNING: You have {0:F0} seconds ({1} min) of play time left.",
            ["hud_enabled"] = "enabled",
            ["hud_disabled"] = "disabled",
            ["hud_toggle"] = "Time HUD {0}.",
            ["hud_toggle_short"] = "HUD {0}.",
            ["no_player_record"] = "No record found for your user.",
            ["time_unlimited_vip"] = "Unlimited time (VIP/Admin)",
            ["pause_active"] = " ( Collective pause ON: {0}/{1} players - {2:F0}%)",
            ["time_status"] = " Time remaining: {0}. Total played: {1}. Renewal: {2}.{3}",
            ["players_online"] = "Online players ({0}/{1} - {2:F0}%):",
            ["pause_discount"] = "  [TIME DEDUCTION PAUSED]",
            ["no_record"] = "No record",
            ["label_remaining"] = "Remaining: {0}",
            ["label_assigned"] = "Assigned: {0}",
            ["label_used"] = "Used: {0}",
            ["label_total"] = "Total played: {0}",
            ["label_renewal"] = "Renewal: {0}",
            ["compact_with_key"] = "Compact ({0})",
            ["syncing"] = "Syncing...",
            ["no_sync"] = "No server data",
            ["pause_hud"] = "Collective pause",
            ["pause_hud_short"] = "PAUSE",
            ["label_players"] = "Players: {0}/{1}",
            ["label_players_short"] = "{0}/{1}",
            ["pause_at"] = "Pause at {0}/{1}",
            ["players_pause"] = "{0}/{1}  ·  PAUSE",
        };

        public static string Normalize(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
                return "ES";

            string trimmed = language.Trim().ToUpperInvariant();
            if (trimmed == "EN" || trimmed == "ENG" || trimmed == "ENGLISH")
                return "EN";
            return "ES";
        }

        public static string CurrentCode()
        {
            string lang = TimeManager.Instance != null && TimeManager.Instance.DataStore != null
                ? TimeManager.Instance.DataStore.ServerConfig.Language
                : "ES";
            return Normalize(lang);
        }

        public static bool IsEnglish()
        {
            return CurrentCode() == "EN";
        }

        public static string T(string key)
        {
            var table = IsEnglish() ? En : Es;
            if (table.TryGetValue(key, out string value))
                return value;
            if (Es.TryGetValue(key, out value))
                return value;
            return key;
        }

        public static string Tf(string key, params object[] args)
        {
            try
            {
                return string.Format(T(key), args);
            }
            catch
            {
                return T(key);
            }
        }
    }
}
