using System;
using System.Collections.Generic;
using System.Linq;

namespace T2.ACRemote.Server
{
    // Database integration point: replace MockFlightDataProvider with MongoFlightDataProvider.
    // MongoFlightDocument deliberately mirrors CollectingSystem.coll_flight field names.
    public interface IFlightDataProvider
    {
        string Name { get; }
        IList<FlightRecord> GetFlights(DateTime utcNow);
    }

    public sealed class MockFlightDataProvider : IFlightDataProvider
    {
        private readonly object _sync = new object();
        private readonly Random _random = new Random(275);
        private readonly List<MongoFlightDocument> _documents = new List<MongoFlightDocument>();
        private DateTime _lastMutationUtc;
        private int _sequence = 100;

        public string Name { get { return "MOCK-MONGODB: CollectingSystem.coll_flight"; } }

        public MockFlightDataProvider(DateTime utcNow) { Seed(utcNow); }

        public IList<FlightRecord> GetFlights(DateTime utcNow)
        {
            lock (_sync)
            {
                if ((utcNow - _lastMutationUtc).TotalSeconds >= 10) Mutate(utcNow);
                return _documents
                    .Where(x => x.sign_io == "A" && x.delete == null && !string.IsNullOrWhiteSpace(x.stand))
                    .Select(MapArrival).OrderBy(x => x.EstimatedArrivalUtc).ToList();
            }
        }

        private void Seed(DateTime now)
        {
            var stands = new[] { "275", "276", "277", "278", "301", "302" };
            var cities = new[] { "北京首都", "上海虹桥", "成都天府", "重庆", "武汉", "西安", "杭州", "昆明" };
            for (var i = 0; i < 18; i++)
            {
                var arrival = now.AddMinutes(3 + i * 4 + _random.Next(0, 3));
                var duration = _random.Next(80, 210);
                _documents.Add(NewDocument("A", stands[i % stands.Length], cities[i % cities.Length], arrival, duration, now));
                if (i % 2 == 0)
                    _documents.Add(NewDocument("D", stands[(i + 2) % stands.Length], cities[(i + 3) % cities.Length], arrival.AddMinutes(18), duration, now));
            }
            _lastMutationUtc = now;
        }

        private MongoFlightDocument NewDocument(string direction, string stand, string city, DateTime operationTime, int duration, DateTime now)
        {
            var scheduled = operationTime.AddMinutes(_random.Next(-2, 3));
            var airlines = new[] { "CZ", "MU", "CA", "HU", "3U", "ZH" };
            var airline = airlines[_sequence % airlines.Length];
            var number = airline + _sequence++;
            var localDate = operationTime.ToLocalTime().Date;
            return new MongoFlightDocument
            {
                _id = number + "-" + operationTime.ToString("yyyyMMddHHmmss") + "-" + direction,
                FLID = (50190000 + _sequence).ToString(), AFID = (50191000 + _sequence).ToString(),
                fdt = localDate.ToUniversalTime(), fimd = localDate.AddDays(-1).ToUniversalTime(),
                flt = number, airline = airline, sign_io = direction, stand = stand,
                area = stand.StartsWith("2", StringComparison.Ordinal) ? "西边" : "东边",
                pnum = "B" + _random.Next(1000, 9999), pmodel = new[] { "32A", "32B", "73N" }[_random.Next(3)],
                rway = new[] { "01L", "01R", "19L", "19R" }[_random.Next(4)],
                tatt = "国内", fltk = "正班", fltR = direction == "A" ? city + "-广州" : "广州-" + city,
                fltRType = "国内", ista = direction == "A" ? "预计到达" : "计划起飞", mist = null,
                terminal = "T2", st = scheduled, et = direction == "A" ? operationTime : (DateTime?)null,
                at = null, t = operationTime, aomip_u_t = now, u_t = now, delete = null,
                isSendDockingOrRemovingTime = false, EstimatedFlightMinutes = duration
            };
        }

        private void Mutate(DateTime now)
        {
            foreach (var flight in _documents.Where(x => x.sign_io == "A"))
            {
                var estimated = flight.et ?? flight.st;
                if (estimated > now.AddMinutes(-20) && estimated < now.AddHours(2))
                {
                    flight.et = estimated.AddSeconds(_random.Next(-35, 36));
                    flight.t = flight.et.Value;
                    flight.ista = flight.et <= now ? "已到达" : "预计到达";
                    if (flight.et <= now && !flight.at.HasValue) flight.at = flight.et;
                    flight.u_t = now; flight.aomip_u_t = now;
                }
            }

            var expired = _documents.Where(x => x.sign_io == "A" && (x.et ?? x.st) < now.AddMinutes(-25)).ToList();
            foreach (var flight in expired)
            {
                _documents.Remove(flight);
                var arrivals = _documents.Where(x => x.sign_io == "A").ToList();
                var latest = arrivals.Count == 0 ? now : arrivals.Max(x => x.et ?? x.st);
                var stands = new[] { "275", "276", "277", "278", "301", "302" };
                var cities = new[] { "北京首都", "上海虹桥", "成都天府", "重庆", "武汉", "西安", "杭州", "昆明" };
                _documents.Add(NewDocument("A", stands[_random.Next(stands.Length)], cities[_random.Next(cities.Length)], latest.AddMinutes(_random.Next(3, 7)), _random.Next(80, 210), now));
            }
            _lastMutationUtc = now;
        }

        private FlightRecord MapArrival(MongoFlightDocument x)
        {
            var route = (x.fltR ?? string.Empty).Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            var estimated = x.et ?? x.st;
            return new FlightRecord
            {
                FlightId = x._id, Flid = x.FLID, Afid = x.AFID, FlightNumber = x.flt,
                Origin = route.Length > 0 ? route[0] : string.Empty,
                Destination = route.Length > 1 ? route[route.Length - 1] : "广州",
                StandId = x.stand, ScheduledDepartureUtc = x.st.AddMinutes(-x.EstimatedFlightMinutes),
                EstimatedDepartureUtc = estimated.AddMinutes(-x.EstimatedFlightMinutes),
                ScheduledArrivalUtc = x.st, EstimatedArrivalUtc = estimated,
                EstimatedFlightMinutes = x.EstimatedFlightMinutes, Status = x.ista, DataSource = Name,
                UpdatedUtc = x.u_t, Direction = x.sign_io, Route = x.fltR, ActualArrivalUtc = x.at,
                Airline = x.airline, AircraftNumber = x.pnum, AircraftModel = x.pmodel,
                Terminal = x.terminal, TrafficType = x.tatt
            };
        }
    }

    // Field names intentionally match the production MongoDB collection document.
    internal sealed class MongoFlightDocument
    {
        public string _id, FLID, AFID, flt, airline, sign_io, stand, area, pnum, pmodel, rway;
        public string tatt, fltk, fltR, fltRType, ista, mist, terminal, delete;
        public DateTime fdt, fimd, aomip_u_t, st, u_t, t;
        public DateTime? et, at;
        public bool isSendDockingOrRemovingTime;
        // Simulation-only helper: the supplied collection has no reliable duration field.
        public int EstimatedFlightMinutes;
    }
}
