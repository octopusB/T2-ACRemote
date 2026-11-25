using System;
using System.Collections.Generic;
using T2.ACRemote.Common;
using T2.ACRemote.Server;

namespace T2.ACRemote.Tests
{
    internal static class Program
    {
        private static int _tests;
        private static void Main()
        {
            StartWhenStopped(); RejectStartWhenRunning(); StopWhenRunning(); StopWhenStopped(); ReleaseRestoresLinkage(); BridgeStartReleasesRemoteStart(); BridgeStopReleasesRemoteStop();
            CommandLineSettingsOverrideDefaults(); EnvironmentSettingsOverrideDefaults();
            MultiBridgeStandNormalization(); FlightWindowStartsEarly(); FlightWindowClosesAfterIdle();
            TurnaroundKeepsRunningUntilDeparture(); DepartureWindowStartsEarly(); CompletedTurnaroundClosesAfterIdle();
            RegisteredConfigurationRoundTrip(); OfflineConfiguredBridgeIsListed();
            Console.WriteLine("PASS: " + _tests + " safety-state tests");
        }
        private static void StartWhenStopped() { var f = new Fake(); var r = Controller(f).Apply(C(ControlMode.RemoteStart)); Yes(r.Success && f.Do1 && !f.Do2, "remote start"); }
        private static void RejectStartWhenRunning() { var f = new Fake { Di1 = true }; var r = Controller(f).Apply(C(ControlMode.RemoteStart)); Yes(!r.Success && !f.Do1 && !f.Do2, "reject remote start while bridge runs"); }
        private static void StopWhenRunning() { var f = new Fake { Di1 = true, Do1 = true }; var r = Controller(f).Apply(C(ControlMode.RemoteStop)); Yes(r.Success && !f.Do1 && f.Do2, "cut original circuit"); }
        private static void StopWhenStopped() { var f = new Fake(); Controller(f).Apply(C(ControlMode.RemoteStop)); Yes(!f.Do1 && !f.Do2, "do not hold cut relay unnecessarily"); }
        private static void ReleaseRestoresLinkage() { var f = new Fake { Do1 = true, Do2 = true }; Controller(f).Apply(C(ControlMode.Release)); Yes(!f.Do1 && !f.Do2, "release linkage"); }
        private static void BridgeStartReleasesRemoteStart() { var f = new Fake(); var c = Controller(f); c.Apply(C(ControlMode.RemoteStart)); f.Di1 = true; c.Poll(); Yes(!f.Do1 && !f.Do2, "bridge start safety release"); }
        private static void BridgeStopReleasesRemoteStop() { var f = new Fake { Di1 = true }; var c = Controller(f); c.Apply(C(ControlMode.RemoteStop)); f.Di1 = false; c.Poll(); Yes(!f.Do1 && !f.Do2, "bridge stop restores original circuit"); }
        private static void CommandLineSettingsOverrideDefaults() { var s = new RuntimeSettings(new[] { "--ServerPort=20001" }, new Dictionary<string, string> { { "ServerPort", "19001" } }); Yes(s.GetInt("ServerPort") == 20001, "command-line settings"); }
        private static void EnvironmentSettingsOverrideDefaults() { Environment.SetEnvironmentVariable("T2AC_TESTVALUE", "from-environment"); try { var s = new RuntimeSettings(new string[0], new Dictionary<string, string> { { "TestValue", "default" } }); Yes(s.Get("TestValue") == "from-environment", "environment settings"); } finally { Environment.SetEnvironmentVariable("T2AC_TESTVALUE", null); } }
        private static void MultiBridgeStandNormalization() { Yes(FlightLinkageRules.NormalizeStandId("275A") == "275" && FlightLinkageRules.NormalizeStandId("275B") == "275", "multi-bridge stand normalization"); }
        private static void FlightWindowStartsEarly() { var now = DateTime.UtcNow; Yes(FlightLinkageRules.ShouldRun(now, new[] { now.AddMinutes(10) }, 15, 20), "flight window starts early"); }
        private static void FlightWindowClosesAfterIdle() { var now = DateTime.UtcNow; Yes(!FlightLinkageRules.ShouldRun(now, new[] { now.AddMinutes(-21) }, 15, 20), "flight window closes after idle"); }
        private static void TurnaroundKeepsRunningUntilDeparture()
        {
            var now = DateTime.UtcNow;
            var arrival = Flight("A", now.AddMinutes(-40), "A1", "D1", "B6683");
            var departure = Flight("D", now.AddMinutes(50), "D1", "A1", "B6683");
            Yes(FlightLinkageService.ShouldRun(now, new[] { arrival, departure }, 15, 20), "turnaround remains active until departure");
        }
        private static void DepartureWindowStartsEarly()
        {
            var now = DateTime.UtcNow;
            Yes(FlightLinkageService.ShouldRun(now, new[] { Flight("D", now.AddMinutes(10), "D2", null, "B7788") }, 15, 20), "departure window starts early");
        }
        private static void CompletedTurnaroundClosesAfterIdle()
        {
            var now = DateTime.UtcNow;
            var arrival = Flight("A", now.AddHours(-2), "A3", "D3", "B8899");
            var departure = Flight("D", now.AddMinutes(-21), "D3", "A3", "B8899");
            Yes(!FlightLinkageService.ShouldRun(now, new[] { arrival, departure }, 15, 20), "completed turnaround closes after idle");
        }
        private static FlightRecord Flight(string direction, DateTime operationTime, string flid, string afid, string aircraft)
        {
            return new FlightRecord
            {
                Direction = direction, Flid = flid, Afid = afid, AircraftNumber = aircraft,
                EstimatedArrivalUtc = operationTime, EstimatedDepartureUtc = operationTime
            };
        }
        private static void RegisteredConfigurationRoundTrip() { var x = new RegisteredMessage { BridgeId = "277A", StandId = "277", TransportMethod = "serial port", SerialPortName = "COM4" }; var y = Json.Deserialize<RegisteredMessage>(Json.Serialize(x)); Yes(y.BridgeId == "277A" && y.TransportMethod == "serial port" && y.SerialPortName == "COM4", "registered bridge configuration"); }
        private static void OfflineConfiguredBridgeIsListed() { var registry = new BridgeRegistry(new Configs()); var status = registry.Statuses()[0]; Yes(status.BridgeId == "277A" && status.StandId == "277" && !status.Online && status.TransportMethod == "serial port", "offline configured bridge listing"); }
        private static AirConditionerController Controller(Fake f) { return new AirConditionerController(f, "B1"); }
        private static CommandMessage C(ControlMode mode) { return new CommandMessage { RequestId = Guid.NewGuid().ToString(), Mode = mode, LeaseSeconds = 60 }; }
        private static void Yes(bool condition, string name) { _tests++; if (!condition) throw new Exception("FAILED: " + name); }
        private sealed class Fake : IIoController { public bool Di1; public bool Do1; public bool Do2; public bool ReadBridgeRunning() { return Di1; } public bool ReadOutput(int i) { return i == 1 ? Do1 : Do2; } public void WriteOutput(int i, bool v) { if (i == 1) Do1 = v; else if (i == 2) Do2 = v; } }
        private sealed class Configs : IBridgeConfigurationProvider { public IList<BridgeConfiguration> GetConfigurations() { return new List<BridgeConfiguration> { new BridgeConfiguration { BridgeId = "277A", StandId = "277", TransportMethod = "serial port", IpAddress = "10.1.2.82" } }; } public BridgeConfiguration FindByIp(string ipAddress) { return GetConfigurations()[0]; } }
    }
}

