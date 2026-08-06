using System;
using System.Configuration;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using T2.ACRemote.Common;

namespace T2.ACRemote.Server
{
    internal static class Program
    {
        private static volatile bool _stopping;
        private static void Main()
        {
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; _stopping = true; };
            var registry = new BridgeRegistry();
            var bridgeListener = new TcpListener(IPAddress.Any, GetInt("BridgeListenPort"));
            bridgeListener.Start();
            var acceptThread = new Thread(() => AcceptBridges(bridgeListener, registry)) { IsBackground = true };
            acceptThread.Start();
            var api = new HttpApi(registry, Get("HttpPrefix"), Get("ApiKey"), GetInt("DefaultLeaseSeconds"));
            api.Start();
            Console.WriteLine("服务器已启动。工控机端口 {0}，管理地址 {1}", GetInt("BridgeListenPort"), Get("HttpPrefix"));
            while (!_stopping) Thread.Sleep(500);
            api.Stop(); bridgeListener.Stop();
        }

        private static void AcceptBridges(TcpListener listener, BridgeRegistry registry)
        {
            while (!_stopping)
            {
                try
                {
                    var client = listener.AcceptTcpClient();
                    var thread = new Thread(() => HandleBridge(client, registry)) { IsBackground = true };
                    thread.Start();
                }
                catch (SocketException) { if (!_stopping) throw; }
            }
        }

        private static void HandleBridge(TcpClient client, BridgeRegistry registry)
        {
            try
            {
                var channel = new SecureFrameChannel(client.GetStream(), Get("SharedSecret"));
                var envelope = channel.ReadEnvelope();
                if (envelope.Type != MessageTypes.Register) throw new InvalidOperationException("First frame must register the bridge.");
                var registration = SecureFrameChannel.Payload<RegisterMessage>(envelope);
                if (string.IsNullOrWhiteSpace(registration.BridgeId) || registration.BridgeId.Length > 100) throw new InvalidOperationException("Invalid bridge ID.");
                var session = new BridgeSession(client, channel, registration.BridgeId);
                registry.Add(session);
                channel.Write(MessageTypes.Registered, registration);
                Console.WriteLine("{0:O} 工控机上线: {1} ({2})", DateTime.UtcNow, registration.BridgeId, registration.MachineName);
                session.Run(registry);
            }
            catch (Exception ex) { Console.WriteLine("{0:O} 工控机连接失败: {1}", DateTime.UtcNow, ex.Message); try { client.Close(); } catch { } }
        }
        private static string Get(string key) { var value = ConfigurationManager.AppSettings[key]; if (string.IsNullOrWhiteSpace(value)) throw new ConfigurationErrorsException("Missing appSetting: " + key); return value; }
        private static int GetInt(string key) { return int.Parse(Get(key)); }
    }
}

