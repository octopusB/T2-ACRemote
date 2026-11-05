using System;
using System.Collections.Generic;
using System.Linq;

namespace T2.ACRemote.Server
{
    // Database integration point: replace MockFlightDataProvider with an implementation
    // that reads the airport flight database and maps rows to FlightRecord.
    public interface IFlightDataProvider
    {
        string Name { get; }
        IList<FlightRecord> GetFlights(DateTime utcNow);
    }

    public sealed class MockFlightDataProvider : IFlightDataProvider
    {
        private readonly object _sync = new object();
        private readonly Random _random = new Random(275);
        private readonly List<FlightRecord> _flights = new List<FlightRecord>();
        private DateTime _lastMutationUtc;
        private int _sequence = 100;

        public string Name { get { return "MOCK-FLIGHTS (replace with database provider)"; } }

        public MockFlightDataProvider(DateTime utcNow)
        {
            Seed(utcNow);
        }

        public IList<FlightRecord> GetFlights(DateTime utcNow)
        {
            lock (_sync)
            {
                if ((utcNow - _lastMutationUtc).TotalSeconds >= 10) Mutate(utcNow);
                return _flights.OrderBy(x => x.EstimatedArrivalUtc).Select(Clone).ToList();
            }
        }

        private void Seed(DateTime now)
        {
            var stands = new[] { "275", "276", "277", "278", "301", "302" };
            var origins = new[] { "CAN", "PEK", "SHA", "CTU", "CKG", "WUH", "XIY", "HGH" };
            for (var i = 0; i < 18; i++)
            {
                var arrival = now.AddMinutes(3 + i * 4 + _random.Next(0, 3));
                var flightMinutes = _random.Next(80, 210);
                _flights.Add(NewFlight(stands[i % stands.Length], origins[i % origins.Length], arrival, flightMinutes, now));
            }
            _lastMutationUtc = now;
        }

        private FlightRecord NewFlight(string stand, string origin, DateTime arrival, int flightMinutes, DateTime now)
        {
            var scheduledArrival = arrival.AddMinutes(_random.Next(-2, 3));
            var scheduledDeparture = scheduledArrival.AddMinutes(-flightMinutes);
            var number = "T2" + _sequence++;
            return new FlightRecord
            {
                FlightId = Guid.NewGuid().ToString("N"),
                FlightNumber = number,
                Origin = origin,
                Destination = "T2",
                StandId = stand,
                ScheduledDepartureUtc = scheduledDeparture,
                EstimatedDepartureUtc = scheduledDeparture.AddMinutes(_random.Next(-3, 4)),
                ScheduledArrivalUtc = scheduledArrival,
                EstimatedArrivalUtc = arrival,
                EstimatedFlightMinutes = flightMinutes,
                Status = "预计到达",
                DataSource = Name,
                UpdatedUtc = now
            };
        }

        private void Mutate(DateTime now)
        {
            foreach (var flight in _flights)
            {
                if (flight.EstimatedArrivalUtc > now.AddMinutes(-20) && flight.EstimatedArrivalUtc < now.AddHours(2))
                {
                    flight.EstimatedArrivalUtc = flight.EstimatedArrivalUtc.AddSeconds(_random.Next(-35, 36));
                    flight.EstimatedDepartureUtc = flight.EstimatedArrivalUtc.AddMinutes(-flight.EstimatedFlightMinutes);
                    flight.Status = flight.EstimatedArrivalUtc <= now ? "已到达" : "预计到达";
                    flight.UpdatedUtc = now;
                }
            }

            var expired = _flights.Where(x => x.EstimatedArrivalUtc < now.AddMinutes(-25)).ToList();
            foreach (var flight in expired)
            {
                _flights.Remove(flight);
                var latest = _flights.Count == 0 ? now : _flights.Max(x => x.EstimatedArrivalUtc);
                var stands = new[] { "275", "276", "277", "278", "301", "302" };
                var origins = new[] { "CAN", "PEK", "SHA", "CTU", "CKG", "WUH", "XIY", "HGH" };
                _flights.Add(NewFlight(stands[_random.Next(stands.Length)], origins[_random.Next(origins.Length)], latest.AddMinutes(_random.Next(3, 7)), _random.Next(80, 210), now));
            }
            _lastMutationUtc = now;
        }

        private static FlightRecord Clone(FlightRecord x)
        {
            return new FlightRecord
            {
                FlightId = x.FlightId,
                FlightNumber = x.FlightNumber,
                Origin = x.Origin,
                Destination = x.Destination,
                StandId = x.StandId,
                ScheduledDepartureUtc = x.ScheduledDepartureUtc,
                EstimatedDepartureUtc = x.EstimatedDepartureUtc,
                ScheduledArrivalUtc = x.ScheduledArrivalUtc,
                EstimatedArrivalUtc = x.EstimatedArrivalUtc,
                EstimatedFlightMinutes = x.EstimatedFlightMinutes,
                Status = x.Status,
                DataSource = x.DataSource,
                UpdatedUtc = x.UpdatedUtc
            };
        }
    }
}
