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
        /// Aumenta mientras el jugador está online, también durante la pausa colectiva.
        /// </summary>
        public double TotalPlayedSeconds { get; set; } = 0.0;
        public DateTime LastAssignedTime { get; set; } = DateTime.UtcNow;
        public DateTime NextResetTime { get; set; } = DateTime.UtcNow;
        public bool IsExempt { get; set; } = false;
        public bool Warning30MinSent { get; set; } = false;

        public bool NeedsReset(DateTime now, ServerConfig config = null)
        {
            DateTime utcNow = ToUtc(now);
            DateTime nextResetUtc = ToUtc(NextResetTime);

            if (nextResetUtc == default || nextResetUtc == DateTime.MinValue)
                return true;

            if (utcNow >= nextResetUtc)
                return true;

            // Si se pasó la medianoche de juego (ResetHourUtc) con tiempo de sobra,
            // renovar aunque NextResetTime en JSON haya quedado mal.
            if (config != null && LastAssignedTime != default)
            {
                DayOfWeek assignedDay = config.GetPlayDay(ToUtc(LastAssignedTime));
                DayOfWeek currentDay = config.GetPlayDay(utcNow);
                if (assignedDay != currentDay)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Próximo reset en UTC (ej: 00:00 Chile = 03:00 UTC).
        /// </summary>
        public static DateTime CalculateNextResetTime(int resetHourUtc, DateTime now)
        {
            DateTime utcNow = ToUtc(now);
            DateTime todayReset = DateTime.SpecifyKind(utcNow.Date.AddHours(resetHourUtc), DateTimeKind.Utc);
            if (todayReset <= utcNow)
                todayReset = todayReset.AddDays(1);
            return todayReset;
        }

        public void ResetSeconds(double blockSeconds, int resetHourUtc, DateTime now)
        {
            DateTime utcNow = ToUtc(now);
            AssignedSeconds = blockSeconds;
            RemainingSeconds = blockSeconds;
            LastAssignedTime = utcNow;
            NextResetTime = CalculateNextResetTime(resetHourUtc, utcNow);
            Warning30MinSent = false;
        }

        private static DateTime ToUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
                return value;
            if (value.Kind == DateTimeKind.Local)
                return value.ToUniversalTime();
            // JSON sin zona: se guardó como reloj UTC (ResetHourUtc).
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
    }
}
