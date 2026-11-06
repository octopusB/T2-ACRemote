using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace T2.ACRemote.Server
{
    [DataContract]
    public sealed class FlightRecord
    {
        [DataMember(Order = 1)] public string FlightId { get; set; }
        [DataMember(Order = 2)] public string FlightNumber { get; set; }
        [DataMember(Order = 3)] public string Origin { get; set; }
        [DataMember(Order = 4)] public string Destination { get; set; }
        [DataMember(Order = 5)] public string StandId { get; set; }
        [DataMember(Order = 6)] public DateTime ScheduledDepartureUtc { get; set; }
        [DataMember(Order = 7)] public DateTime EstimatedDepartureUtc { get; set; }
        [DataMember(Order = 8)] public DateTime ScheduledArrivalUtc { get; set; }
        [DataMember(Order = 9)] public DateTime EstimatedArrivalUtc { get; set; }
        [DataMember(Order = 10)] public int EstimatedFlightMinutes { get; set; }
        [DataMember(Order = 11)] public string Status { get; set; }
        [DataMember(Order = 12)] public string DataSource { get; set; }
        [DataMember(Order = 13)] public DateTime UpdatedUtc { get; set; }
        [DataMember(Order = 14)] public string Flid { get; set; }
        [DataMember(Order = 15)] public string Afid { get; set; }
        [DataMember(Order = 16)] public string Direction { get; set; }
        [DataMember(Order = 17)] public string Route { get; set; }
        [DataMember(Order = 18)] public DateTime? ActualArrivalUtc { get; set; }
        [DataMember(Order = 19)] public string Airline { get; set; }
        [DataMember(Order = 20)] public string AircraftNumber { get; set; }
        [DataMember(Order = 21)] public string AircraftModel { get; set; }
        [DataMember(Order = 22)] public string Terminal { get; set; }
        [DataMember(Order = 23)] public string TrafficType { get; set; }
    }

    [DataContract]
    public sealed class FlightLinkageSettings
    {
        [DataMember(Order = 1)] public int StartBeforeMinutes { get; set; }
        [DataMember(Order = 2)] public int StopAfterMinutes { get; set; }
        [DataMember(Order = 3)] public bool AllEnabled { get; set; }
        [DataMember(Order = 4)] public DateTime UpdatedUtc { get; set; }
    }

    [DataContract]
    public sealed class FlightLinkageSettingsUpdate
    {
        [DataMember(Order = 1)] public int StartBeforeMinutes { get; set; }
        [DataMember(Order = 2)] public int StopAfterMinutes { get; set; }
    }

    [DataContract]
    public sealed class BridgeFlightLinkageState
    {
        [DataMember(Order = 1)] public string BridgeId { get; set; }
        [DataMember(Order = 2)] public string StandId { get; set; }
        [DataMember(Order = 3)] public bool Enabled { get; set; }
        [DataMember(Order = 4)] public bool ShouldRun { get; set; }
        [DataMember(Order = 5)] public string AutomationStatus { get; set; }
        [DataMember(Order = 6)] public string LastAction { get; set; }
        [DataMember(Order = 7)] public DateTime? LastActionUtc { get; set; }
        [DataMember(Order = 8)] public string LastError { get; set; }
        [DataMember(Order = 9)] public IList<FlightRecord> RelevantFlights { get; set; }
    }

    [DataContract]
    public sealed class FlightLinkageSnapshot
    {
        [DataMember(Order = 1)] public FlightLinkageSettings Settings { get; set; }
        [DataMember(Order = 2)] public IList<BridgeFlightLinkageState> Bridges { get; set; }
        [DataMember(Order = 3)] public int FlightCount { get; set; }
        [DataMember(Order = 4)] public DateTime FlightsUpdatedUtc { get; set; }
        [DataMember(Order = 5)] public string DataSource { get; set; }
    }
}
