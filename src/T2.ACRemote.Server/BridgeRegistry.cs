using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using T2.ACRemote.Common;

namespace T2.ACRemote.Server
{
    public sealed class BridgeRegistry
    {
        private readonly ConcurrentDictionary<string, BridgeSession> _sessions = new ConcurrentDictionary<string, BridgeSession>(StringComparer.OrdinalIgnoreCase);
        public void Add(BridgeSession session) { BridgeSession old; if (_sessions.TryGetValue(session.BridgeId, out old)) old.Close(); _sessions[session.BridgeId] = session; }
        public void Remove(BridgeSession session) { BridgeSession current; if (_sessions.TryGetValue(session.BridgeId, out current) && ReferenceEquals(current, session)) _sessions.TryRemove(session.BridgeId, out current); }
        public BridgeSession Get(string id) { BridgeSession result; return _sessions.TryGetValue(id, out result) ? result : null; }
        public IList<BridgeStatus> Statuses() { return _sessions.Values.Select(x => x.LastStatus ?? new BridgeStatus { BridgeId = x.BridgeId, IoConnected = false, UpdatedUtc = DateTime.UtcNow, Error = "等待首个状态上报" }).OrderBy(x => x.BridgeId).ToList(); }
    }

    public sealed class BridgeSession
    {
        private readonly TcpClient _client;
        private readonly SecureFrameChannel _channel;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<CommandResult>> _pending = new ConcurrentDictionary<string, TaskCompletionSource<CommandResult>>();
        public string BridgeId { get; private set; }
        public BridgeStatus LastStatus { get; private set; }

        public BridgeSession(TcpClient client, SecureFrameChannel channel, string bridgeId) { _client = client; _channel = channel; BridgeId = bridgeId; }
        public void Run(BridgeRegistry registry)
        {
            try
            {
                while (true)
                {
                    var envelope = _channel.ReadEnvelope();
                    if (envelope.Type == MessageTypes.Status) LastStatus = SecureFrameChannel.Payload<BridgeStatus>(envelope);
                    else if (envelope.Type == MessageTypes.CommandResult)
                    {
                        var result = SecureFrameChannel.Payload<CommandResult>(envelope);
                        LastStatus = result.Status;
                        TaskCompletionSource<CommandResult> waiter;
                        if (_pending.TryRemove(result.RequestId, out waiter)) waiter.TrySetResult(result);
                    }
                }
            }
            finally { registry.Remove(this); Close(); }
        }

        public CommandResult Send(ControlMode mode, int leaseSeconds, int timeoutMilliseconds)
        {
            var request = new CommandMessage { RequestId = Guid.NewGuid().ToString("N"), Mode = mode, LeaseSeconds = leaseSeconds };
            var waiter = new TaskCompletionSource<CommandResult>();
            if (!_pending.TryAdd(request.RequestId, waiter)) throw new InvalidOperationException("Duplicate request ID.");
            try
            {
                _channel.Write(MessageTypes.Command, request);
                if (!waiter.Task.Wait(timeoutMilliseconds)) throw new TimeoutException("工控机命令响应超时。执行结果未知，请检查状态后再操作。 ");
                return waiter.Task.Result;
            }
            finally { TaskCompletionSource<CommandResult> ignored; _pending.TryRemove(request.RequestId, out ignored); }
        }
        public void Close() { try { _client.Close(); } catch { } }
    }
}
