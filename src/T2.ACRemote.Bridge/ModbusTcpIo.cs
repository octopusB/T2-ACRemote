using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace T2.ACRemote.Bridge
{
    public interface IIoController
    {
        bool ReadBridgeRunning();
        bool ReadOutput(int outputIndex);
        void WriteOutput(int outputIndex, bool closed);
    }

    public sealed class ModbusTcpIo : IIoController, IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private readonly byte _unitId;
        private readonly object _sync = new object();
        private TcpClient _client;
        private ushort _transactionId;

        public ModbusTcpIo(string host, int port, byte unitId) { _host = host; _port = port; _unitId = unitId; }

        public bool ReadBridgeRunning() { return ReadBit(0x02, 0x0020); }
        public bool ReadOutput(int outputIndex)
        {
            ValidateOutput(outputIndex);
            return ReadBit(0x01, (ushort)(outputIndex - 1));
        }

        public void WriteOutput(int outputIndex, bool closed)
        {
            ValidateOutput(outputIndex);
            var address = (ushort)(outputIndex - 1);
            var value = closed ? (ushort)0xFF00 : (ushort)0x0000;
            var response = Exchange(0x05, new[] { Hi(address), Lo(address), Hi(value), Lo(value) });
            if (response.Length != 5 || response[0] != 0x05 || response[1] != Hi(address) || response[2] != Lo(address) || response[3] != Hi(value) || response[4] != Lo(value))
                throw new IOException("Invalid Modbus write-coil response.");
        }

        private bool ReadBit(byte function, ushort address)
        {
            var response = Exchange(function, new[] { Hi(address), Lo(address), (byte)0, (byte)1 });
            if (response.Length != 3 || response[0] != function || response[1] != 1) throw new IOException("Invalid Modbus read-bit response.");
            return (response[2] & 0x01) != 0;
        }

        private byte[] Exchange(byte function, byte[] data)
        {
            lock (_sync)
            {
                EnsureConnected();
                try
                {
                    var transaction = ++_transactionId;
                    var pduLength = 1 + data.Length;
                    var frame = new byte[7 + pduLength];
                    frame[0] = Hi(transaction); frame[1] = Lo(transaction);
                    frame[2] = 0; frame[3] = 0;
                    frame[4] = Hi((ushort)(pduLength + 1)); frame[5] = Lo((ushort)(pduLength + 1));
                    frame[6] = _unitId; frame[7] = function;
                    Buffer.BlockCopy(data, 0, frame, 8, data.Length);
                    var stream = _client.GetStream();
                    stream.Write(frame, 0, frame.Length);
                    var header = ReadExact(stream, 7);
                    var returnedTransaction = (ushort)((header[0] << 8) | header[1]);
                    var length = (header[4] << 8) | header[5];
                    if (returnedTransaction != transaction || header[2] != 0 || header[3] != 0 || header[6] != _unitId || length < 2) throw new IOException("Invalid Modbus TCP header.");
                    var pdu = ReadExact(stream, length - 1);
                    if ((pdu[0] & 0x80) != 0) throw new IOException("Modbus exception code: " + (pdu.Length > 1 ? pdu[1].ToString() : "unknown"));
                    return pdu;
                }
                catch { Disconnect(); throw; }
            }
        }

        private void EnsureConnected()
        {
            if (_client != null && _client.Connected) return;
            Disconnect();
            _client = new TcpClient { ReceiveTimeout = 3000, SendTimeout = 3000 };
            var ar = _client.BeginConnect(_host, _port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(3000)) { Disconnect(); throw new TimeoutException("IO controller connection timed out."); }
            _client.EndConnect(ar);
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            var result = new byte[count]; var offset = 0;
            while (offset < count) { var read = stream.Read(result, offset, count - offset); if (read == 0) throw new EndOfStreamException(); offset += read; }
            return result;
        }

        private static byte Hi(ushort value) { return (byte)(value >> 8); }
        private static byte Lo(ushort value) { return (byte)value; }
        private static void ValidateOutput(int index) { if (index < 1 || index > 4) throw new ArgumentOutOfRangeException("index"); }
        private void Disconnect() { if (_client != null) { try { _client.Close(); } catch { } _client = null; } }
        public void Dispose() { lock (_sync) Disconnect(); }
    }
}
