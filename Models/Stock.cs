
using System;
using System.Collections.Generic;
using System.Linq;

namespace BawsaqOffline.Core.Models
{
    public sealed class Stock
    {
        public string Symbol { get; private set; }
        public string Name { get; private set; }
        public StockExchange Exchange { get; private set; }

        public decimal Price { get; internal set; }
        public decimal PreviousPrice { get; internal set; }

        public decimal Volatility { get; private set; }
        public decimal Drift { get; private set; }

        public decimal ChangePercent
        {
            get
            {
                if (PreviousPrice <= 0)
                    return 0;

                return (Price / PreviousPrice - 1) * 100;
            }
        }

        private readonly List<PricePoint> priceHistory =
            new List<PricePoint>();

        public IReadOnlyList<PricePoint> PriceHistory
        {
            get { return priceHistory.AsReadOnly(); }
        }

        // Старый конструктор оставляем для совместимости.
        // Если биржа не указана, акция считается LCN.
        public Stock(
            string symbol,
            string name,
            decimal price,
            decimal volatility,
            decimal drift)
            : this(
                symbol,
                name,
                price,
                volatility,
                drift,
                StockExchange.LCN)
        {
        }

        public Stock(
            string symbol,
            string name,
            decimal price,
            decimal volatility,
            decimal drift,
            StockExchange exchange)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Symbol is required.");

            if (price <= 0)
                throw new ArgumentOutOfRangeException(nameof(price));

            Symbol = symbol;
            Name = name;
            Price = price;
            PreviousPrice = price;
            Volatility = volatility;
            Drift = drift;
            Exchange = exchange;

            RecordPrice(DateTime.Now);
        }

        public void RecordPrice(DateTime timestamp)
        {
            priceHistory.Add(new PricePoint(timestamp, Price));

            if (priceHistory.Count > 5000)
                priceHistory.RemoveAt(0);
        }

        public decimal GetAveragePrice(int periods)
        {
            if (periods <= 0 || priceHistory.Count == 0)
                return Price;

            return priceHistory
                .Skip(Math.Max(0, priceHistory.Count - periods))
                .Average(x => x.Price);
        }

        public void RestoreHistory(IEnumerable<PricePoint> history)
        {
            priceHistory.Clear();

            if (history == null)
            {
                RecordPrice(DateTime.Now);
                return;
            }

            foreach (PricePoint point in history)
            {
                if (point == null || point.Price <= 0)
                    continue;

                priceHistory.Add(point);
            }

            if (priceHistory.Count > 5000)
            {
                priceHistory.RemoveRange(
                    0,
                    priceHistory.Count - 5000);
            }

            if (priceHistory.Count == 0)
                RecordPrice(DateTime.Now);
        }
    }
}
