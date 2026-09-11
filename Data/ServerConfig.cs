using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TimePlayerControl.Data
{
    /// <summary>
    /// Overrides opcionales por día. Si un campo falta, se usa el valor global de ServerConfig.
    /// </summary>
    [Serializable]
    public class DailyPlayRule
    {
        public double? DefaultBlockSeconds { get; set; }
        public float? PauseTimeMinOnlineRatio { get; set; }
    }

    [Serializable]
    public class ServerConfig
    {
        public double DefaultBlockSeconds { get; set; } = 21600.0;   // 6 horas = 21600 segundos
        public int ResetHourUtc { get; set; } = 3;                   // 03:00 UTC = 00:00 Chile (UTC-3)
        public double WarningThresholdSeconds { get; set; } = 1800.0; // 30 minutos = 1800 segundos
        public float PauseTimeMinOnlineRatio { get; set; } = 0.75f;

        /// <summary>
        /// Reglas por día (Monday..Sunday o Lunes..Domingo). Los días omitidos usan los valores globales.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, DailyPlayRule> DailyRules { get; set; }

        public double SaveIntervalSeconds { get; set; } = 300.0;      // 5 minutos = 300 segundos

        public bool EnableInfoLogs { get; set; } = true;
        public bool EnableDebugLogs { get; set; } = false;
        public bool EnableClientHud { get; set; } = true;

        /// <summary>
        /// Idioma de textos locales: ES (español) o EN (inglés).
        /// Cliente = HUD; servidor = mensajes de kick/chat.
        /// </summary>
        public string Language { get; set; } = "ES";

        /// <summary>
        /// Ancla del HUD: TopLeft, TopRight, BottomLeft, BottomRight.
        /// HudPositionX/Y se interpretan como margen desde esa esquina.
        /// </summary>
        public string HudAnchor { get; set; } = "TopRight";

        /// <summary>Margen horizontal desde el ancla (px @ 1920x1080).</summary>
        public float HudPositionX { get; set; } = 16f;

        /// <summary>Margen vertical desde el ancla (px @ 1920x1080). En TopRight ~220 deja el panel bajo el minimapa.</summary>
        public float HudPositionY { get; set; } = 220f;

        public float HudWidth { get; set; } = 240f;

        /// <summary>0 = altura automática según contenido.</summary>
        public float HudHeight { get; set; } = 0f;

        public float HudOpacity { get; set; } = 0.75f;

        /// <summary>Oculta el HUD al abrir inventario, menú, mapa, tienda o chat enfocado.</summary>
        public bool HudAutoHideOverlays { get; set; } = true;

        /// <summary>Inicia en modo compacto (una línea); clic para expandir detalles.</summary>
        public bool HudStartCompact { get; set; } = true;

        /// <summary>En modo compacto muestra una barra de progreso del tiempo restante.</summary>
        public bool HudShowProgressBar { get; set; } = true;

        /// <summary>
        /// Tecla para alternar compacto/detalle (nombre KeyCode de Unity, ej: F1, F2, T, Alpha1).
        /// </summary>
        public string HudToggleKey { get; set; } = "F1";

        /// <summary>
        /// Si es true y no hay posición personalizada, coloca el HUD justo debajo del minimapa
        /// respetando el HUD scale del juego.
        /// </summary>
        public bool HudSnapBelowMinimap { get; set; } = true;

        /// <summary>Separación en px (a 100% HUD scale / 1080p) entre el borde inferior del minimapa y el panel.</summary>
        public float HudMinimapGap { get; set; } = 8f;

        /// <summary>Si es true, usa HudCustomNormX/Y (posición arrastrada con Escape) en lugar del ancla automática.</summary>
        public bool HudUseCustomPosition { get; set; } = false;

        /// <summary>Posición X normalizada (0–1) respecto al ancho de pantalla. Usada si HudUseCustomPosition.</summary>
        public float HudCustomNormX { get; set; } = 0f;

        /// <summary>Posición Y normalizada (0–1) respecto al alto de pantalla. Usada si HudUseCustomPosition.</summary>
        public float HudCustomNormY { get; set; } = 0f;

        public bool ShouldSerializeDailyRules()
        {
            return DailyRules != null && DailyRules.Count > 0;
        }

        /// <summary>
        /// Día de juego según la medianoche local definida por ResetHourUtc (ej. 3 = UTC-3 Chile).
        /// </summary>
        public DayOfWeek GetPlayDay(DateTime utcNow)
        {
            DateTime local = utcNow.AddHours(-ResetHourUtc);
            return local.DayOfWeek;
        }

        public double GetBlockSecondsForNow(DateTime utcNow)
        {
            if (TryGetDailyRule(GetPlayDay(utcNow), out DailyPlayRule rule)
                && rule.DefaultBlockSeconds.HasValue
                && rule.DefaultBlockSeconds.Value > 0d)
            {
                return rule.DefaultBlockSeconds.Value;
            }

            return DefaultBlockSeconds > 0d ? DefaultBlockSeconds : 21600d;
        }

        public float GetPauseRatioForNow(DateTime utcNow)
        {
            float ratio = PauseTimeMinOnlineRatio;
            if (TryGetDailyRule(GetPlayDay(utcNow), out DailyPlayRule rule)
                && rule.PauseTimeMinOnlineRatio.HasValue)
            {
                ratio = rule.PauseTimeMinOnlineRatio.Value;
            }

            return Math.Max(0f, Math.Min(1f, ratio));
        }

        private bool TryGetDailyRule(DayOfWeek day, out DailyPlayRule rule)
        {
            rule = null;
            if (DailyRules == null || DailyRules.Count == 0)
                return false;

            foreach (string alias in GetDayAliases(day))
            {
                foreach (var kvp in DailyRules)
                {
                    if (kvp.Value == null || string.IsNullOrWhiteSpace(kvp.Key))
                        continue;
                    if (string.Equals(kvp.Key.Trim(), alias, StringComparison.OrdinalIgnoreCase))
                    {
                        rule = kvp.Value;
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<string> GetDayAliases(DayOfWeek day)
        {
            switch (day)
            {
                case DayOfWeek.Monday:
                    return new[] { "Monday", "Mon", "Lunes" };
                case DayOfWeek.Tuesday:
                    return new[] { "Tuesday", "Tue", "Martes" };
                case DayOfWeek.Wednesday:
                    return new[] { "Wednesday", "Wed", "Miercoles", "Miércoles" };
                case DayOfWeek.Thursday:
                    return new[] { "Thursday", "Thu", "Jueves" };
                case DayOfWeek.Friday:
                    return new[] { "Friday", "Fri", "Viernes" };
                case DayOfWeek.Saturday:
                    return new[] { "Saturday", "Sat", "Sabado", "Sábado" };
                default:
                    return new[] { "Sunday", "Sun", "Domingo" };
            }
        }
    }
}
