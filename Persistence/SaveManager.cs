
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

using BawsaqOffline.Core.Models;
using BawsaqOffline.Core.Engine;
using BawsaqOffline.Core.Trading;

namespace BawsaqOffline.Core.Persistence
{
    public sealed class SaveManager
    {
        private readonly string savePath;

        public SaveManager(string savePath)
        {
            if (string.IsNullOrWhiteSpace(savePath))
                throw new ArgumentException("Save path cannot be empty.");

            this.savePath = savePath;
        }

        public void Save(
            MarketEngine market,
            Portfolio portfolio)
        {
            if (market == null)
                throw new ArgumentNullException(nameof(market));

            if (portfolio == null)
                throw new ArgumentNullException(nameof(portfolio));

            var data = new MarketSaveData
            {
                SavedAt = DateTime.Now,
                LastMarketUpdate = market.LastMarketUpdate,

                Stocks = market.GetAllStocks()
                    .Select(stock => new StockSaveData
                    {
                        Symbol = stock.Symbol,
                        Price = stock.Price,
                        PreviousPrice = stock.PreviousPrice,
                        History = stock.PriceHistory.ToList()
                    })
                    .ToList(),

                Holdings = market.GetAllStocks()
                    .Select(stock => new HoldingSaveData
                    {
                        Symbol = stock.Symbol,
                        Quantity = portfolio.GetShares(stock.Symbol)
                    })
                    .Where(x => x.Quantity > 0)
                    .ToList(),

                Events = market.GetActiveEvents()
                    .Select(e => new MarketEventSaveData
                    {
                        Id = e.Id,
                        Name = e.Name,
                        Description = e.Description,
                        TargetSymbols = e.TargetSymbols.ToList(),
                        PriceImpactPercent = e.PriceImpactPercent,
                        DriftModifier = e.DriftModifier,
                        DurationTicks = e.DurationTicks,
                        RemainingTicks = e.RemainingTicks,
                        StartedAt = e.StartedAt
                    })
                    .ToList()
            };

            string json = JsonConvert.SerializeObject(
                data,
                Formatting.Indented
            );

            string directory = Path.GetDirectoryName(savePath);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempPath = savePath + ".tmp";

            File.WriteAllText(tempPath, json);

            if (!File.Exists(savePath))
            {
                File.Move(tempPath, savePath);
                return;
            }

            string backupPath = savePath + ".bak";

            File.Replace(
                tempPath,
                savePath,
                backupPath
            );
        }

        public bool Load(
            MarketEngine market,
            Portfolio portfolio)
        {
            if (!File.Exists(savePath))
                return false;

            string json = File.ReadAllText(savePath);

            var data =
                JsonConvert.DeserializeObject<MarketSaveData>(json);

            if (data == null)
                return false;

            portfolio.ClearHoldings();

            if (data.Stocks != null)
            {
                foreach (var stock in data.Stocks)
                {
                    market.RestorePrice(
                        stock.Symbol,
                        stock.Price,
                        stock.PreviousPrice
                    );

                    market.RestoreHistory(
                        stock.Symbol,
                        stock.History
                    );
                }
            }

            if (data.Holdings != null)
            {
                foreach (var holding in data.Holdings)
                {
                    portfolio.RestoreHolding(
                        holding.Symbol,
                        holding.Quantity
                    );
                }
            }

            DateTime lastUpdate = data.LastMarketUpdate;

            if (lastUpdate == DateTime.MinValue)
                lastUpdate = data.SavedAt;

            market.RestoreClock(lastUpdate);

            market.ClearActiveEvents();

            if (data.Events != null)
            {
                foreach (var savedEvent in data.Events)
                {
                    var marketEvent = new MarketEvent
                    {
                        Id = savedEvent.Id,
                        Name = savedEvent.Name,
                        Description = savedEvent.Description,

                        TargetSymbols = savedEvent.TargetSymbols
                            ?? new System.Collections.Generic.List<string>(),

                        PriceImpactPercent =
                            savedEvent.PriceImpactPercent,

                        DriftModifier = savedEvent.DriftModifier,

                        DurationTicks = savedEvent.DurationTicks,

                        RemainingTicks = savedEvent.RemainingTicks,

                        StartedAt = savedEvent.StartedAt
                    };

                    market.RestoreEvent(marketEvent);
                }
            }

            return true;
        }
    }
}
