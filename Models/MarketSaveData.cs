
using System;
using System.Collections.Generic;

namespace BawsaqOffline.Core.Models
{
    public sealed class MarketSaveData
    {
        // Legacy field: оставлен для чтения старых сохранений.
        // В новой экономике деньги хранятся непосредственно в GTA V.
        public decimal Cash { get; set; }

        public DateTime SavedAt { get; set; }

        public DateTime LastMarketUpdate { get; set; }

        public List<StockSaveData> Stocks { get; set; }

        public List<HoldingSaveData> Holdings { get; set; }

        public List<MarketEventSaveData> Events { get; set; }
    }

    public sealed class StockSaveData
    {
        public string Symbol { get; set; }

        public decimal Price { get; set; }

        public decimal PreviousPrice { get; set; }

        public List<PricePoint> History { get; set; }
    }

    public sealed class HoldingSaveData
    {
        public string Symbol { get; set; }

        public long Quantity { get; set; }
    }

    public sealed class MarketEventSaveData
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public List<string> TargetSymbols { get; set; }

        public decimal PriceImpactPercent { get; set; }

        public decimal DriftModifier { get; set; }

        public int DurationTicks { get; set; }

        public int RemainingTicks { get; set; }

        public DateTime StartedAt { get; set; }
    }
}
