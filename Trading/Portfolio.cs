
using System;
using System.Collections.Generic;

using BawsaqOffline.Core.Models;
using BawsaqOffline.Core.Engine;

namespace BawsaqOffline.Core.Trading
{
    public sealed class Portfolio
    {
        private readonly Dictionary<string, long> holdings =
            new Dictionary<string, long>();

        public Portfolio()
        {
        }

        public long GetShares(string symbol)
        {
            long quantity;

            return holdings.TryGetValue(symbol, out quantity)
                ? quantity
                : 0;
        }

        public bool Buy(Stock stock, long quantity)
        {
            if (stock == null || quantity <= 0)
                return false;

            long current = GetShares(stock.Symbol);

            if (current > long.MaxValue - quantity)
                return false;

            holdings[stock.Symbol] = current + quantity;

            return true;
        }

        public bool Sell(Stock stock, long quantity)
        {
            if (stock == null || quantity <= 0)
                return false;

            long current = GetShares(stock.Symbol);

            if (quantity > current)
                return false;

            long remaining = current - quantity;

            if (remaining == 0)
                holdings.Remove(stock.Symbol);
            else
                holdings[stock.Symbol] = remaining;

            return true;
        }

        public decimal GetTotalValue(MarketEngine market)
        {
            if (market == null)
                throw new ArgumentNullException(nameof(market));

            decimal total = 0m;

            foreach (var holding in holdings)
            {
                Stock stock = market.GetStock(holding.Key);

                if (stock != null)
                    total += stock.Price * holding.Value;
            }

            return total;
        }

        public void RestoreHolding(string symbol, long quantity)
        {
            if (string.IsNullOrWhiteSpace(symbol) || quantity <= 0)
                return;

            holdings[symbol] = quantity;
        }

        public void ClearHoldings()
        {
            holdings.Clear();
        }
    }
}
