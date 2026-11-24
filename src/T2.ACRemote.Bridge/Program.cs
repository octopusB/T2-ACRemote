using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using T2.ACRemote.Common;

namespace T2.ACRemote.Bridge
{
    internal static class Program
    {
        private static volatile bool _stopping;
        private static RuntimeSettings _settings;

        private static void Main(string[] args)
        {
            if (RuntimeSettings.WantsHelp(args)) { PrintHelp(); return; }
            _settings = new RuntimeSettings(args, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "BridgeId", "AUTO" }, { "ServerHost", "10.1.3.5" }, { "ServerPort", "19001" },
                { "SharedSecret", "CHANGE-THIS-BRIDGE-SHARED-SECRET" },
                { "IoHost", "192.168.10.1" }, { "IoPort", "28899" }, { "ModbusUnitId", "17" },
                { "SerialBaudRate", "9600" }, { "PollMilliseconds", "1000" }
            });
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; _stopping = true; };
            while (!_stopping)
            {
                try { RunSession(); }
                catch (Exception ex) { Log("服务器连接中断: " + ex.Message); }
                if (!_stopping) Thread.Sleep(3000);
            }
        }

        private static void RunSession()
        {
            using (var client = new TcpClient())
            {
                client.Connect(Get("ServerHost"), GetInt("ServerPort"));
                var channel = new SecureFrameChannel(client.GetStream(), Get("SharedSecret"));
                channel.Write(MessageTypes.Register, new RegisterMessage { BridgeId = Get("BridgeId"), MachineName = Environment.MachineName, Version = "2.0.0" });
                var envelope = channel.ReadEnvelope();
                if (envelope.Type != MessageTypes.Registered) throw new InvalidOperationException("Server rejected registration.");
                var registration = SecureFrameChannel.Payload<RegisteredMessage>(envelope);
                if (string.IsNullOrWhiteSpace(registration.BridgeId)) throw new InvalidOperationException("服务器未返回桥配置。");

                IDisposable ioResource;
                IIoController io;
                if (string.Equals(registration.TransportMethod, "serial port", StringComparison.OrdinalIgnoreCase))
                {
                    var serial = new ModbusRtuIo("COM4", GetInt("SerialBaudRate"), (byte)GetInt("ModbusUnitId"));
                    io = serial; ioResource = serial;
                    Log("桥 " + registration.BridgeId + " 使用 RS-485 串口 COM4，Modbus RTU。 ");
                }
                else if (string.Equals(registration.TransportMethod, "TCP", StringComparison.OrdinalIgnoreCase))
                {
                    var tcp = new ModbusTcpIo(Get("IoHost"), GetInt("IoPort"), (byte)GetInt("ModbusUnitId"));
                    io = tcp; ioResource = tcp;
                    Log("桥 " + registration.BridgeId + " 使用 TCP 连接 IO 控制器 " + Get("IoHost") + ":" + GetInt("IoPort") + "。 ");
                }
                else throw new InvalidOperationException("数据库 transMethod 不支持: " + registration.TransportMethod);

                using (ioResource)
                {
                    var controller = new AirConditionerController(io, registration.BridgeId);
                    try
                    {
                        Log("已按 IP 匹配并注册: " + registration.BridgeId + "，机位 " + registration.StandId);
                        var poller = new Thread(() => PollLoop(channel, controller)) { IsBackground = true };
                        poller.Start();
                        while (!_stopping)
                        {
                            var commandEnvelope = channel.ReadEnvelope();
                            if (commandEnvelope.Type != MessageTypes.Command) continue;
                            var command = SecureFrameChannel.Payload<CommandMessage>(commandEnvelope);
                            channel.Write(MessageTypes.CommandResult, controller.Apply(command));
                        }
                    }
                    finally
                    {
                        controller.Apply(new CommandMessage { RequestId = "disconnect", Mode = ControlMode.Release, LeaseSeconds = 10 });
                    }
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

        private static string Get(string key) { return _settings.Get(key); }
        private static int GetInt(string key) { return _settings.GetInt(key); }
        private static void Log(string message) { Console.WriteLine("{0:O} {1}", DateTime.UtcNow, message); }

        private static void PrintHelp()
        {
            Console.WriteLine("T2.ACRemote.Bridge 单文件版");
            Console.WriteLine("服务器根据工控机来源 IP 匹配 ac_remote_bridge_config，并下发 TCP 或 serial port 传输方式。");
            Console.WriteLine("serial port 固定使用 COM4、Modbus RTU；TCP 继续使用 IoHost/IoPort。");
            Console.WriteLine("可用参数: BridgeId, ServerHost, ServerPort, SharedSecret, IoHost, IoPort, ModbusUnitId, SerialBaudRate, PollMilliseconds");
        }
    }
}
