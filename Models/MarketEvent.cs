
using System;
using System.Collections.Generic;

namespace BawsaqOffline.Core.Models
{
    public sealed class MarketEvent
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        // Какие компании затрагивает событие.
        public List<string> TargetSymbols { get; set; }

        // Мгновенное изменение цены в процентах.
        // Например, -12 означает падение на 12%.
        public decimal PriceImpactPercent { get; set; }

        // Дополнительная поправка к изменению цены
        // на каждом рыночном тике.
        // Например, -0.001 означает -0.1%.
        public decimal DriftModifier { get; set; }

        // Полная длительность события.
        public int DurationTicks { get; set; }

        // Сколько тиков осталось.
        public int RemainingTicks { get; set; }

        public DateTime StartedAt { get; set; }

        public MarketEvent()
        {
            TargetSymbols = new List<string>();
        }

        public MarketEvent(
            string id,
            string name,
            string description,
            List<string> targetSymbols,
            decimal priceImpactPercent,
            decimal driftModifier,
            int durationTicks)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Event ID is required.");

            if (durationTicks <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(durationTicks));

            Id = id;
            Name = name;
            Description = description;

            TargetSymbols = targetSymbols
                ?? new List<string>();

            PriceImpactPercent = priceImpactPercent;
            DriftModifier = driftModifier;

            DurationTicks = durationTicks;
            RemainingTicks = durationTicks;

            StartedAt = DateTime.Now;
        }
    }
}
