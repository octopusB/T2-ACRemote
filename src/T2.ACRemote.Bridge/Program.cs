using System;
using System.Configuration;
using System.Net.Sockets;
using System.Threading;
using T2.ACRemote.Common;

namespace T2.ACRemote.Bridge
{
    internal static class Program
    {
        private static volatile bool _stopping;
        private static void Main()
        {
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; _stopping = true; };
            var bridgeId = Get("BridgeId");
            using (var io = new ModbusTcpIo(Get("IoHost"), GetInt("IoPort"), (byte)GetInt("ModbusUnitId")))
            {
                var controller = new AirConditionerController(io, bridgeId);
                while (!_stopping)
                {
                    try { RunSession(controller, bridgeId); }
                    catch (Exception ex) { Log("服务器连接中断: " + ex.Message); }
                    if (!_stopping) Thread.Sleep(3000);
                }
                controller.Apply(new CommandMessage { RequestId = "shutdown", Mode = ControlMode.Release, LeaseSeconds = 10 });
            }
        }

        private static void RunSession(AirConditionerController controller, string bridgeId)
        {
            using (var client = new TcpClient())
            {
                client.Connect(Get("ServerHost"), GetInt("ServerPort"));
                var channel = new SecureFrameChannel(client.GetStream(), Get("SharedSecret"));
                channel.Write(MessageTypes.Register, new RegisterMessage { BridgeId = bridgeId, MachineName = Environment.MachineName, Version = "1.0.0" });
                var registered = channel.ReadEnvelope();
                if (registered.Type != MessageTypes.Registered) throw new InvalidOperationException("Server rejected registration.");
                Log("已连接服务器并注册: " + bridgeId);
                var poller = new Thread(() => PollLoop(channel, controller)) { IsBackground = true };
                poller.Start();
                while (!_stopping)
                {
                    var envelope = channel.ReadEnvelope();
                    if (envelope.Type != MessageTypes.Command) continue;
                    var command = SecureFrameChannel.Payload<CommandMessage>(envelope);
                    channel.Write(MessageTypes.CommandResult, controller.Apply(command));
                }
            }
        }

        private static void PollLoop(SecureFrameChannel channel, AirConditionerController controller)
        {
            while (!_stopping)
            {
                try { channel.Write(MessageTypes.Status, controller.Poll()); }
                catch { return; }
                Thread.Sleep(GetInt("PollMilliseconds"));
            }
        }

        private static string Get(string key) { var value = ConfigurationManager.AppSettings[key]; if (string.IsNullOrWhiteSpace(value)) throw new ConfigurationErrorsException("Missing appSetting: " + key); return value; }
        private static int GetInt(string key) { return int.Parse(Get(key)); }
        private static void Log(string message) { Console.WriteLine("{0:O} {1}", DateTime.UtcNow, message); }
    }
}

