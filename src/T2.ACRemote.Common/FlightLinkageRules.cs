using System;
using System.Collections.Generic;
using System.Linq;

namespace T2.ACRemote.Common
{
    public static class FlightLinkageRules
    {
        public static string NormalizeStandId(string bridgeId)
        {
            if (string.IsNullOrWhiteSpace(bridgeId)) return string.Empty;
            var value = bridgeId.Trim();
            var end = value.Length;
            while (end > 0 && char.IsLetter(value[end - 1])) end--;
            return end == 0 ? value : value.Substring(0, end);
        }

        public static bool ShouldRun(DateTime utcNow, IEnumerable<DateTime> estimatedArrivalsUtc, int startBeforeMinutes, int stopAfterMinutes)
        {
            return estimatedArrivalsUtc.Any(arrival => utcNow >= arrival.AddMinutes(-startBeforeMinutes) && utcNow <= arrival.AddMinutes(stopAfterMinutes));
        }
    }
}
