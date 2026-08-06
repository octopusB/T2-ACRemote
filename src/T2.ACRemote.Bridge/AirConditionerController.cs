using System;
using T2.ACRemote.Common;

namespace T2.ACRemote.Bridge
{
    public sealed class AirConditionerController
    {
        private readonly IIoController _io;
        private readonly string _bridgeId;
        private readonly object _sync = new object();
        private ControlMode _mode;
        private DateTime? _leaseExpiresUtc;

        public AirConditionerController(IIoController io, string bridgeId) { _io = io; _bridgeId = bridgeId; }

        public CommandResult Apply(CommandMessage command)
        {
            lock (_sync)
            {
                try
                {
                    var running = _io.ReadBridgeRunning();
                    switch (command.Mode)
                    {
                        case ControlMode.RemoteStart:
                            if (running) return Failure(command.RequestId, "登机桥正在运行，远程启动回路已被硬件联锁断开。请使用释放联动或远程停机。", Snapshot(null));
                            _io.WriteOutput(2, false);
                            _io.WriteOutput(1, true);
                            break;
                        case ControlMode.RemoteStop:
                            _io.WriteOutput(1, false);
                            _io.WriteOutput(2, running);
                            break;
                        case ControlMode.Release:
                            ReleaseOutputs();
                            break;
                        default: return Failure(command.RequestId, "未知控制模式。", Snapshot(null));
                    }
                    _mode = command.Mode;
                    _leaseExpiresUtc = command.Mode == ControlMode.Release ? (DateTime?)null : DateTime.UtcNow.AddSeconds(Math.Max(10, Math.Min(command.LeaseSeconds, 3600)));
                    return new CommandResult { RequestId = command.RequestId, Success = true, Message = "命令已执行并回读。", Status = Snapshot(null) };
                }
                catch (Exception ex) { return Failure(command.RequestId, ex.Message, Snapshot(ex.Message)); }
            }
        }

        public BridgeStatus Poll()
        {
            lock (_sync)
            {
                try
                {
                    var running = _io.ReadBridgeRunning();
                    if (_leaseExpiresUtc.HasValue && DateTime.UtcNow >= _leaseExpiresUtc.Value) ReleaseState();
                    else if (_mode == ControlMode.RemoteStart && running) ReleaseState();
                    else if (_mode == ControlMode.RemoteStop && !running) ReleaseState();
                    return Snapshot(null);
                }
                catch (Exception ex) { return Snapshot(ex.Message); }
            }
        }

        private void ReleaseState() { ReleaseOutputs(); _mode = ControlMode.Release; _leaseExpiresUtc = null; }
        private void ReleaseOutputs() { _io.WriteOutput(1, false); _io.WriteOutput(2, false); }
        private BridgeStatus Snapshot(string error)
        {
            try
            {
                return new BridgeStatus { BridgeId = _bridgeId, IoConnected = error == null, BridgeRunning = _io.ReadBridgeRunning(), Do1RemoteStart = _io.ReadOutput(1), Do2CutOriginal = _io.ReadOutput(2), Mode = _mode, UpdatedUtc = DateTime.UtcNow, LeaseExpiresUtc = _leaseExpiresUtc, Error = error };
            }
            catch (Exception ex) { return new BridgeStatus { BridgeId = _bridgeId, IoConnected = false, Mode = _mode, UpdatedUtc = DateTime.UtcNow, LeaseExpiresUtc = _leaseExpiresUtc, Error = error ?? ex.Message }; }
        }
        private static CommandResult Failure(string id, string message, BridgeStatus status) { return new CommandResult { RequestId = id, Success = false, Message = message, Status = status }; }
    }
}

