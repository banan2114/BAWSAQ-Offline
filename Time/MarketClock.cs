
using System;

namespace BawsaqOffline.Core.Time
{
    public sealed class MarketClock
    {
        // Один игровой день GTA V — примерно 48 минут.
        private static readonly TimeSpan RealGameDay =
            TimeSpan.FromMinutes(48);

        // Один рыночный тик — 15 игровых минут.
        private const int GameMinutesPerTick = 15;

        // Максимальное количество тиков за одну загрузку.
        private const int MaxCatchUpTicks = 96 * 7;

        public DateTime LastUpdate { get; private set; }

        public MarketClock()
        {
            LastUpdate = DateTime.Now;
        }

        public MarketClock(DateTime lastUpdate)
        {
            LastUpdate = lastUpdate;
        }

        public int GetElapsedTicks(DateTime now)
        {
            if (now <= LastUpdate)
                return 0;

            double realMinutes = (now - LastUpdate).TotalMinutes;

            // Переводим реальные минуты в игровые.
            double gameMinutes =
                realMinutes * (1440.0 / RealGameDay.TotalMinutes);

            int ticks = (int)(gameMinutes / GameMinutesPerTick);

            return Math.Min(ticks, MaxCatchUpTicks);
        }

        public void Advance(int ticks)
        {
            if (ticks <= 0)
                return;

            double realMinutesPerTick =
                RealGameDay.TotalMinutes *
                GameMinutesPerTick / 1440.0;

            LastUpdate = LastUpdate.AddMinutes(
                ticks * realMinutesPerTick);
        }

        public void Synchronize(DateTime now)
        {
            LastUpdate = now;
        }

        public DateTime GetTickTime(int tickNumber)
        {
            if (tickNumber < 1)
                throw new ArgumentOutOfRangeException(nameof(tickNumber));

            long ticksPerMarketTick =
                RealGameDay.Ticks * GameMinutesPerTick / 1440;

            return LastUpdate.AddTicks(
                ticksPerMarketTick * tickNumber);
        }
    }
}
