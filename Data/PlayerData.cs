using System;

namespace TimePlayerControl.Data
{
    [Serializable]
    public class PlayerData
    {
        public string SteamID { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public double RemainingSeconds { get; set; } = 21600.0; // 21600 segundos (6 horas)
        public double AssignedSeconds { get; set; } = 21600.0; // tiempo asignado en este bloque actual
        /// <summary>
        /// Segundos totales jugados a lo largo del tiempo (no se resetea con el bloque diario).
        /// Aumenta mientras el jugador está online y consume (o usa) su tiempo asignado.
        /// </summary>
        public double TotalPlayedSeconds { get; set; } = 0.0;
        public DateTime LastAssignedTime { get; set; } = DateTime.UtcNow;
        public DateTime NextResetTime { get; set; } = DateTime.UtcNow;
        public bool IsExempt { get; set; } = false;
        public bool Warning30MinSent { get; set; } = false;

        public bool NeedsReset(DateTime now)
        {
            return now >= NextResetTime;
        }

        /// <summary>
        /// Calcula el próximo reset basado en la hora del día (ej: 00:00 Chile = 03:00 UTC)
        /// </summary>
        private static DateTime CalculateNextResetTime(int resetHourUtc, DateTime now)
        {
            // Crear datetime de hoy a la hora del reset
            DateTime nextReset = now.Date.AddHours(resetHourUtc);
            
            // Si ya pasó esa hora hoy, ir al próximo día
            if (nextReset <= now)
            {
                nextReset = nextReset.AddDays(1);
            }
            
            return nextReset;
        }

        public void ResetSeconds(double blockSeconds, int resetHourUtc, DateTime now)
        {
            AssignedSeconds = blockSeconds;
            RemainingSeconds = blockSeconds;
            LastAssignedTime = now;
            NextResetTime = CalculateNextResetTime(resetHourUtc, now);
            Warning30MinSent = false;
        }
    }
}
