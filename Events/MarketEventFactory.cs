
using System.Collections.Generic;
using BawsaqOffline.Core.Models;

namespace BawsaqOffline.Core.Events
{
    public static class MarketEventFactory
    {
        // Hotel Assassination
        // Betta Pharmaceuticals растёт после устранения цели.
        public static MarketEvent CreateHotelBettaEvent()
        {
            return Create(
                "lester.hotel.betta",
                "Hotel Assassination: Betta",
                "Положительное влияние миссии на Betta Pharmaceuticals.",
                "BET",
                0.20m,
                0.012m,
                24);
        }

        // Bilkinton падает, затем постепенно восстанавливается.
        public static MarketEvent CreateHotelBilkintonEvent()
        {
            return Create(
                "lester.hotel.bilkinton",
                "Hotel Assassination: Bilkinton",
                "Падение Bilkinton с последующим восстановлением.",
                "BIL",
                -0.25m,
                0.006m,
                48);
        }

        // Multi Target Assassination
        public static MarketEvent CreateMultiTargetDebonaireEvent()
        {
            return Create(
                "lester.multi.debonaire",
                "Multi Target Assassination: Debonaire",
                "Положительное влияние миссии на Debonaire.",
                "DEB",
                0.25m,
                0.012m,
                24);
        }

        public static MarketEvent CreateMultiTargetRedwoodEvent()
        {
            return Create(
                "lester.multi.redwood",
                "Multi Target Assassination: Redwood",
                "Падение Redwood с последующим восстановлением.",
                "RWC",
                -0.40m,
                0.008m,
                48);
        }

        // Vice Assassination
        public static MarketEvent CreateViceFruitEvent()
        {
            return Create(
                "lester.vice.fruit",
                "Vice Assassination: Fruit",
                "Положительное влияние миссии на Fruit.",
                "FRT",
                0.20m,
                0.010m,
                24);
        }

        public static MarketEvent CreateViceFacadeEvent()
        {
            return Create(
                "lester.vice.facade",
                "Vice Assassination: Facade",
                "Падение Facade с последующим восстановлением.",
                "FAC",
                -0.25m,
                0.006m,
                48);
        }

        // Bus Assassination
        public static MarketEvent CreateBusVapidEvent()
        {
            return Create(
                "lester.bus.vapid",
                "Bus Assassination: Vapid",
                "Падение Vapid с последующим восстановлением.",
                "VAP",
                -0.35m,
                0.010m,
                48);
        }

        // Construction Assassination
        public static MarketEvent CreateConstructionGoldCoastEvent()
        {
            return Create(
                "lester.construction.goldcoast",
                "Construction Assassination: GoldCoast",
                "Положительное влияние миссии на GoldCoast.",
                "GCD",
                0.25m,
                0.012m,
                24);
        }

        private static MarketEvent Create(
            string id,
            string name,
            string description,
            string symbol,
            decimal priceImpact,
            decimal driftModifier,
            int durationTicks)
        {
            return new MarketEvent(
                id,
                name,
                description,
                new List<string> { symbol },
                priceImpact,
                driftModifier,
                durationTicks);
        }
    }
}
