using System;
using System.Runtime.Serialization;

namespace T2.ACRemote.Common
{
    public static class MessageTypes
    {
        public const string Register = "register";
        public const string Registered = "registered";
        public const string Command = "command";
        public const string CommandResult = "command-result";
        public const string Status = "status";
    }

    public enum ControlMode { Release = 0, RemoteStart = 1, RemoteStop = 2 }

    [DataContract]
    public sealed class WireEnvelope
    {
        [DataMember(Order = 1)] public string Type { get; set; }
        [DataMember(Order = 2)] public string Payload { get; set; }
    }

    [DataContract]
    public sealed class RegisterMessage
    {
        [DataMember(Order = 1)] public string BridgeId { get; set; }
        [DataMember(Order = 2)] public string MachineName { get; set; }
        [DataMember(Order = 3)] public string Version { get; set; }
    }

    [DataContract]
    public sealed class CommandMessage
    {
        [DataMember(Order = 1)] public string RequestId { get; set; }
        [DataMember(Order = 2)] public ControlMode Mode { get; set; }
        [DataMember(Order = 3)] public int LeaseSeconds { get; set; }
    }

    [DataContract]
    public sealed class CommandResult
    {
        [DataMember(Order = 1)] public string RequestId { get; set; }
        [DataMember(Order = 2)] public bool Success { get; set; }
        [DataMember(Order = 3)] public string Message { get; set; }
        [DataMember(Order = 4)] public BridgeStatus Status { get; set; }
    }

    [DataContract]
    public sealed class BridgeStatus
    {
        [DataMember(Order = 1)] public string BridgeId { get; set; }
        [DataMember(Order = 2)] public bool IoConnected { get; set; }
        [DataMember(Order = 3)] public bool BridgeRunning { get; set; }
        [DataMember(Order = 4)] public bool Do1RemoteStart { get; set; }
        [DataMember(Order = 5)] public bool Do2CutOriginal { get; set; }
        [DataMember(Order = 6)] public ControlMode Mode { get; set; }
        [DataMember(Order = 7)] public DateTime UpdatedUtc { get; set; }
        [DataMember(Order = 8)] public DateTime? LeaseExpiresUtc { get; set; }
        [DataMember(Order = 9)] public string Error { get; set; }
    }
}

