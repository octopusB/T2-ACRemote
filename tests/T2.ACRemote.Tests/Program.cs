using System;
using T2.ACRemote.Bridge;
using T2.ACRemote.Common;

namespace T2.ACRemote.Tests
{
    internal static class Program
    {
        private static int _tests;
        private static void Main()
        {
            StartWhenStopped(); RejectStartWhenRunning(); StopWhenRunning(); StopWhenStopped(); ReleaseRestoresLinkage(); BridgeStartReleasesRemoteStart(); BridgeStopReleasesRemoteStop();
            Console.WriteLine("PASS: " + _tests + " safety-state tests");
        }
        private static void StartWhenStopped() { var f = new Fake(); var r = Controller(f).Apply(C(ControlMode.RemoteStart)); Yes(r.Success && f.Do1 && !f.Do2, "remote start"); }
        private static void RejectStartWhenRunning() { var f = new Fake { Di1 = true }; var r = Controller(f).Apply(C(ControlMode.RemoteStart)); Yes(!r.Success && !f.Do1 && !f.Do2, "reject remote start while bridge runs"); }
        private static void StopWhenRunning() { var f = new Fake { Di1 = true, Do1 = true }; var r = Controller(f).Apply(C(ControlMode.RemoteStop)); Yes(r.Success && !f.Do1 && f.Do2, "cut original circuit"); }
        private static void StopWhenStopped() { var f = new Fake(); Controller(f).Apply(C(ControlMode.RemoteStop)); Yes(!f.Do1 && !f.Do2, "do not hold cut relay unnecessarily"); }
        private static void ReleaseRestoresLinkage() { var f = new Fake { Do1 = true, Do2 = true }; Controller(f).Apply(C(ControlMode.Release)); Yes(!f.Do1 && !f.Do2, "release linkage"); }
        private static void BridgeStartReleasesRemoteStart() { var f = new Fake(); var c = Controller(f); c.Apply(C(ControlMode.RemoteStart)); f.Di1 = true; c.Poll(); Yes(!f.Do1 && !f.Do2, "bridge start safety release"); }
        private static void BridgeStopReleasesRemoteStop() { var f = new Fake { Di1 = true }; var c = Controller(f); c.Apply(C(ControlMode.RemoteStop)); f.Di1 = false; c.Poll(); Yes(!f.Do1 && !f.Do2, "bridge stop restores original circuit"); }
        private static AirConditionerController Controller(Fake f) { return new AirConditionerController(f, "B1"); }
        private static CommandMessage C(ControlMode mode) { return new CommandMessage { RequestId = Guid.NewGuid().ToString(), Mode = mode, LeaseSeconds = 60 }; }
        private static void Yes(bool condition, string name) { _tests++; if (!condition) throw new Exception("FAILED: " + name); }
        private sealed class Fake : IIoController { public bool Di1; public bool Do1; public bool Do2; public bool ReadBridgeRunning() { return Di1; } public bool ReadOutput(int i) { return i == 1 ? Do1 : Do2; } public void WriteOutput(int i, bool v) { if (i == 1) Do1 = v; else if (i == 2) Do2 = v; } }
    }
}

