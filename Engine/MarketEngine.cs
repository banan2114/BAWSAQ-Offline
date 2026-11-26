
using System;
using System.Collections.Generic;
using System.Linq;

using BawsaqOffline.Core.Catalog;
using BawsaqOffline.Core.Models;
using BawsaqOffline.Core.Time;

namespace BawsaqOffline.Core.Engine
{
    public sealed class MarketEngine
    {
        private readonly Dictionary<string, Stock> stocks;
        private readonly Random random;
        private readonly MarketClock clock;
        private readonly List<MarketEvent> activeEvents;

        public DateTime LastMarketUpdate
        {
            get { return clock.LastUpdate; }
        }

        public DateTime LastUpdate { get; private set; }

        public MarketEngine(int seed = 0)
        {
            stocks = new Dictionary<string, Stock>();
            activeEvents = new List<MarketEvent>();

            clock = new MarketClock(DateTime.Now);

            random = seed == 0
                ? new Random()
                : new Random(seed);

            // Теперь все акции создаются из единого каталога.
            foreach (Stock stock in CompanyCatalog.CreateStocks())
            {
                AddStock(stock);
            }

            LastUpdate = clock.LastUpdate;
        }

        private void AddStock(Stock stock)
        {
            if (stock == null)
                throw new ArgumentNullException(nameof(stock));

            if (stocks.ContainsKey(stock.Symbol))
            {
                throw new InvalidOperationException(
                    "Duplicate stock symbol: " + stock.Symbol);
            }

            stocks.Add(stock.Symbol, stock);
        }

        public Stock GetStock(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return null;

            Stock stock;

            return stocks.TryGetValue(symbol, out stock)
                ? stock
                : null;
        }

        public IEnumerable<Stock> GetAllStocks()
        {
            return stocks.Values.ToList();
        }

        public IEnumerable<Stock> GetStocks(StockExchange exchange)
        {
            return stocks.Values
                .Where(stock => stock.Exchange == exchange)
                .ToList();
        }

        public int GetStockCount()
        {
            return stocks.Count;
        }

        public IEnumerable<MarketEvent> GetActiveEvents()
        {
            return activeEvents.ToList();
        }

        public void StartEvent(MarketEvent marketEvent)
        {
            if (marketEvent == null)
                throw new ArgumentNullException(nameof(marketEvent));

            if (string.IsNullOrWhiteSpace(marketEvent.Id))
                throw new ArgumentException("Event ID is required.");

            if (marketEvent.DurationTicks <= 0)
                throw new ArgumentException(
                    "Event duration must be greater than zero.");

            if (activeEvents.Any(e => e.Id == marketEvent.Id))
            {
                throw new InvalidOperationException(
                    "Event with this ID is already active.");
            }

            if (marketEvent.TargetSymbols == null ||
                marketEvent.TargetSymbols.Count == 0)
            {
                throw new ArgumentException(
                    "Event must have at least one target stock.");
            }

            // Сначала проверяем все акции.
            // Это не позволит частично применить событие,
            // если один из тикеров окажется неизвестным.
            foreach (string symbol in marketEvent.TargetSymbols)
            {
                if (GetStock(symbol) == null)
                {
                    throw new ArgumentException(
                        "Unknown stock symbol: " + symbol);
                }
            }

            DateTime now = DateTime.Now;

            marketEvent.StartedAt = now;
            marketEvent.RemainingTicks = marketEvent.DurationTicks;

            activeEvents.Add(marketEvent);

            // Применяем мгновенное изменение цены.
            foreach (string symbol in marketEvent.TargetSymbols)
            {
                Stock stock = GetStock(symbol);

                stock.PreviousPrice = stock.Price;

                stock.Price *=
                    1 + marketEvent.PriceImpactPercent / 100m;

                if (stock.Price < 0.01m)
                    stock.Price = 0.01m;

                stock.Price = Math.Round(
                    stock.Price,
                    2,
                    MidpointRounding.AwayFromZero);

                stock.RecordPrice(now);
            }

            LastUpdate = now;
            clock.Synchronize(now);
        }

        public void Tick()
        {
            Tick(DateTime.Now);
        }

        public void Tick(DateTime timestamp)
        {
            UpdateMarket(timestamp);
            clock.Synchronize(timestamp);
        }

        private void UpdateMarket(DateTime timestamp)
        {
            foreach (Stock stock in stocks.Values)
            {
                stock.PreviousPrice = stock.Price;

                double randomValue =
                    random.NextDouble() * 2.0 - 1.0;

                decimal eventDrift =
                    GetEventDrift(stock.Symbol);

                decimal change =
                    stock.Drift +
                    eventDrift +
                    stock.Volatility * (decimal)randomValue;

                if (change > 0.15m)
                    change = 0.15m;

                if (change < -0.15m)
                    change = -0.15m;

                stock.Price *= 1 + change;

                if (stock.Price < 0.01m)
                    stock.Price = 0.01m;

                stock.Price = Math.Round(
                    stock.Price,
                    2,
                    MidpointRounding.AwayFromZero);

                stock.RecordPrice(timestamp);
            }

            // Уменьшаем оставшуюся длительность событий.
            foreach (MarketEvent marketEvent in activeEvents)
            {
                marketEvent.RemainingTicks--;
            }

            // Удаляем завершившиеся события.
            activeEvents.RemoveAll(
                e => e.RemainingTicks <= 0);

            LastUpdate = timestamp;
        }

        private decimal GetEventDrift(string symbol)
        {
            decimal modifier = 0m;

            foreach (MarketEvent marketEvent in activeEvents)
            {
                if (marketEvent.TargetSymbols.Contains(symbol))
                {
                    modifier += marketEvent.DriftModifier;
                }
            }

            return modifier;
        }

        public void ApplyMarketShock(
            string symbol,
            decimal percentage)
        {
            Stock stock = GetStock(symbol);

            if (stock == null)
                return;

            DateTime now = DateTime.Now;

            stock.PreviousPrice = stock.Price;

            stock.Price *= 1 + percentage / 100m;

            if (stock.Price < 0.01m)
                stock.Price = 0.01m;

            stock.Price = Math.Round(
                stock.Price,
                2,
                MidpointRounding.AwayFromZero);

            stock.RecordPrice(now);

            LastUpdate = now;
            clock.Synchronize(now);
        }

        public void RestorePrice(
            string symbol,
            decimal price,
            decimal previousPrice)
        {
            Stock stock = GetStock(symbol);

            if (stock == null || price <= 0)
                return;

            stock.Price = price;

            stock.PreviousPrice = previousPrice > 0
                ? previousPrice
                : price;
        }

        public void RestoreHistory(
            string symbol,
            IEnumerable<PricePoint> history)
        {
            Stock stock = GetStock(symbol);

            if (stock == null)
                return;

            stock.RestoreHistory(history);
        }

        public void RestoreClock(DateTime lastUpdate)
        {
            clock.Synchronize(lastUpdate);
            LastUpdate = lastUpdate;
        }

        public int ProcessElapsedTime(DateTime now)
        {
            int elapsedTicks = clock.GetElapsedTicks(now);

            for (int i = 0; i < elapsedTicks; i++)
            {
                DateTime tickTime = clock.GetTickTime(i + 1);

                UpdateMarket(tickTime);
            }

            clock.Advance(elapsedTicks);

            LastUpdate = clock.LastUpdate;

            return elapsedTicks;
        }

        public void ClearActiveEvents()
        {
            activeEvents.Clear();
        }

        public void RestoreEvent(MarketEvent marketEvent)
        {
            if (marketEvent == null)
                return;

            if (string.IsNullOrWhiteSpace(marketEvent.Id))
                return;

            if (marketEvent.RemainingTicks <= 0)
                return;

            if (marketEvent.TargetSymbols == null ||
                marketEvent.TargetSymbols.Count == 0)
                return;

            foreach (string symbol in marketEvent.TargetSymbols)
            {
                if (GetStock(symbol) == null)
                    return;
            }

            if (activeEvents.Any(e => e.Id == marketEvent.Id))
                return;

            // При восстановлении не применяем мгновенный эффект повторно.
            activeEvents.Add(marketEvent);
        }
    }
}
