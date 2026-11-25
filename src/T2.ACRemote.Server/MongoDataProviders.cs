using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Driver;

namespace T2.ACRemote.Server
{
    public sealed class BridgeConfiguration
    {
        public string BridgeId { get; set; }
        public string StandId { get; set; }
        public string TransportMethod { get; set; }
        public string IpAddress { get; set; }
    }

    public interface IBridgeConfigurationProvider
    {
        IList<BridgeConfiguration> GetConfigurations();
        BridgeConfiguration FindByIp(string ipAddress);
    }

    public sealed class MongoBridgeConfigurationProvider : IBridgeConfigurationProvider
    {
        private readonly IMongoCollection<BsonDocument> _collection;
        private readonly object _sync = new object();
        private IList<BridgeConfiguration> _cache = new List<BridgeConfiguration>();
        private DateTime _cacheUtc;

        public MongoBridgeConfigurationProvider(IMongoDatabase database, string collectionName)
        {
            _collection = database.GetCollection<BsonDocument>(collectionName);
        }

        public IList<BridgeConfiguration> GetConfigurations()
        {
            lock (_sync)
            {
                if (_cache.Count == 0 || (DateTime.UtcNow - _cacheUtc).TotalSeconds >= 10)
                {
                    var documents = _collection.Find(FilterDefinition<BsonDocument>.Empty).ToList();
                    _cache = documents.Select(Map).Where(x => !string.IsNullOrWhiteSpace(x.BridgeId)).OrderBy(x => x.BridgeId).ToList();
                    _cacheUtc = DateTime.UtcNow;
                }
                return _cache.Select(Clone).ToList();
            }
        }

        public BridgeConfiguration FindByIp(string ipAddress)
        {
            return GetConfigurations().FirstOrDefault(x => string.Equals(NormalizeIp(x.IpAddress), NormalizeIp(ipAddress), StringComparison.OrdinalIgnoreCase));
        }

        private static BridgeConfiguration Map(BsonDocument document)
        {
            return new BridgeConfiguration
            {
                BridgeId = TrimmedText(document, "bridge"), StandId = TrimmedText(document, "stand"),
                TransportMethod = TrimmedText(document, "transMethod"), IpAddress = TrimmedText(document, "IP")
            };
        }

        private static BridgeConfiguration Clone(BridgeConfiguration x)
        {
            return new BridgeConfiguration { BridgeId = x.BridgeId, StandId = x.StandId, TransportMethod = x.TransportMethod, IpAddress = x.IpAddress };
        }

        internal static string NormalizeIp(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            value = value.Trim();
            const string mapped = "::ffff:";
            return value.StartsWith(mapped, StringComparison.OrdinalIgnoreCase) ? value.Substring(mapped.Length) : value;
        }

        internal static string Text(BsonDocument document, string name)
        {
            BsonValue value;
            return document.TryGetValue(name, out value) && value != null && !value.IsBsonNull ? value.ToString() : null;
        }

        private static string TrimmedText(BsonDocument document, string name)
        {
            var value = Text(document, name); return value == null ? null : value.Trim();
        }
    }

    public sealed class MongoFlightDataProvider : IFlightDataProvider
    {
        private readonly IMongoCollection<BsonDocument> _collection;
        private readonly string _name;
        public string Name { get { return _name; } }

        public MongoFlightDataProvider(IMongoDatabase database, string collectionName)
        {
            _collection = database.GetCollection<BsonDocument>(collectionName);
            _name = "MongoDB: " + database.DatabaseNamespace.DatabaseName + "." + collectionName;
        }

        public IList<FlightRecord> GetFlights(DateTime utcNow)
        {
            var from = utcNow.AddHours(-12);
            var to = utcNow.AddHours(24);
            var filter = Builders<BsonDocument>.Filter.In("sign_io", new[] { "A", "D" }) &
                         Builders<BsonDocument>.Filter.Gte("st", from) &
                         Builders<BsonDocument>.Filter.Lte("st", to) &
                         (Builders<BsonDocument>.Filter.Eq("delete", BsonNull.Value) | Builders<BsonDocument>.Filter.Exists("delete", false));
            return _collection.Find(filter).Sort(Builders<BsonDocument>.Sort.Ascending("st")).ToList()
                .Select(Map).Where(x => x != null && !string.IsNullOrWhiteSpace(x.StandId)).ToList();
        }

        private FlightRecord Map(BsonDocument document)
        {
            var scheduled = Date(document, "st");
            if (!scheduled.HasValue) return null;
            var estimated = Date(document, "et") ?? scheduled.Value;
            var actual = Date(document, "at");
            var direction = (MongoBridgeConfigurationProvider.Text(document, "sign_io") ?? string.Empty).Trim().ToUpperInvariant();
            var routeText = MongoBridgeConfigurationProvider.Text(document, "fltR") ?? string.Empty;
            var route = routeText.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            var status = MongoBridgeConfigurationProvider.Text(document, "ista");
            if (actual.HasValue) status = direction == "D" ? "已起飞" : "已到达";
            else if (string.IsNullOrWhiteSpace(status)) status = direction == "D" ? "预计起飞" : "预计到达";
            return new FlightRecord
            {
                FlightId = MongoBridgeConfigurationProvider.Text(document, "_id"),
                Flid = MongoBridgeConfigurationProvider.Text(document, "FLID"), Afid = MongoBridgeConfigurationProvider.Text(document, "AFID"),
                FlightNumber = MongoBridgeConfigurationProvider.Text(document, "flt"), StandId = MongoBridgeConfigurationProvider.Text(document, "stand"),
                Origin = route.Length > 0 ? route[0] : string.Empty, Destination = route.Length > 1 ? route[route.Length - 1] : string.Empty,
                Route = routeText, Direction = direction, ScheduledArrivalUtc = scheduled.Value, EstimatedArrivalUtc = estimated,
                ScheduledDepartureUtc = scheduled.Value, EstimatedDepartureUtc = estimated,
                ActualArrivalUtc = direction == "A" ? actual : null, ActualDepartureUtc = direction == "D" ? actual : null,
                EstimatedFlightMinutes = 0, Status = status, DataSource = Name,
                UpdatedUtc = Date(document, "u_t") ?? DateTime.UtcNow,
                Airline = MongoBridgeConfigurationProvider.Text(document, "airline"), AircraftNumber = MongoBridgeConfigurationProvider.Text(document, "pnum"),
                AircraftModel = MongoBridgeConfigurationProvider.Text(document, "pmodel"), Terminal = MongoBridgeConfigurationProvider.Text(document, "terminal"),
                TrafficType = MongoBridgeConfigurationProvider.Text(document, "tatt")
            };
        }

        private static DateTime? Date(BsonDocument document, string name)
        {
            BsonValue value;
            if (!document.TryGetValue(name, out value) || value == null || value.IsBsonNull || !value.IsBsonDateTime) return null;
            return value.AsBsonDateTime.ToUniversalTime();
        }
    }

    public static class MongoConnectionFactory
    {
        public static IMongoDatabase Create(string host, int port, string databaseName, string authDatabase, string user, string password)
        {
            if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("MongoPassword 未配置。请使用 T2AC_MONGOPASSWORD 环境变量或 --MongoPassword 参数。");
            var settings = new MongoClientSettings
            {
                Server = new MongoServerAddress(host, port),
                Credential = MongoCredential.CreateCredential(authDatabase, user, password),
                ServerSelectionTimeout = TimeSpan.FromSeconds(5), ConnectTimeout = TimeSpan.FromSeconds(5)
            };
            return new MongoClient(settings).GetDatabase(databaseName);
        }
    }
}
