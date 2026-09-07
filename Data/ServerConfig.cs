using System;

namespace TimePlayerControl.Data
{
    [Serializable]
    public class ServerConfig
    {
        public double DefaultBlockSeconds { get; set; } = 21600.0;   // 6 horas = 21600 segundos
        public int ResetHourUtc { get; set; } = 3;                   // 03:00 UTC = 00:00 Chile (UTC-3)
        public double WarningThresholdSeconds { get; set; } = 1800.0; // 30 minutos = 1800 segundos
        public float PauseTimeMinOnlineRatio { get; set; } = 0.75f;
        public double SaveIntervalSeconds { get; set; } = 300.0;      // 5 minutos = 300 segundos

        public bool EnableInfoLogs { get; set; } = true;
        public bool EnableDebugLogs { get; set; } = false;
        public bool EnableClientHud { get; set; } = true;

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
    }
}
