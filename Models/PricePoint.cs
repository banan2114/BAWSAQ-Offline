
using System;

namespace BawsaqOffline.Core.Models
{
    public sealed class PricePoint
    {
        public DateTime Timestamp { get; set; }
        public decimal Price { get; set; }

        public PricePoint(DateTime timestamp, decimal price)
        {
            Timestamp = timestamp;
            Price = price;
        }
    }
}
