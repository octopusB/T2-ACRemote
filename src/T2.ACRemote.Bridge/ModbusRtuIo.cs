using System;
using System.IO;
using System.IO.Ports;
using T2.ACRemote.Common;

namespace T2.ACRemote.Bridge
{
    public sealed class ModbusRtuIo : IIoController, IDisposable
    {
        private readonly SerialPort _port;
        private readonly byte _unitId;
        private readonly object _sync = new object();

        public ModbusRtuIo(string portName, int baudRate, byte unitId)
        {
            _unitId = unitId;
            _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 3000, WriteTimeout = 3000, Handshake = Handshake.None,
                DtrEnable = false, RtsEnable = false
            };
        }

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
                throw new IOException("Invalid Modbus RTU write-coil response.");
        }

        private bool ReadBit(byte function, ushort address)
        {
            var response = Exchange(function, new[] { Hi(address), Lo(address), (byte)0, (byte)1 });
            if (response.Length != 4 || response[0] != function || response[1] != 1) throw new IOException("Invalid Modbus RTU read-bit response.");
            return (response[2] & 0x01) != 0;
        }

        private byte[] Exchange(byte function, byte[] data)
        {
            lock (_sync)
            {
                EnsureOpen();
                try
                {
                    _port.DiscardInBuffer();
                    var request = new byte[2 + data.Length + 2];
                    request[0] = _unitId; request[1] = function;
                    Buffer.BlockCopy(data, 0, request, 2, data.Length);
                    WriteCrc(request, request.Length - 2);
                    _port.Write(request, 0, request.Length);

                    var header = ReadExact(3);
                    if (header[0] != _unitId) throw new IOException("Unexpected Modbus RTU unit ID.");
                    var remainder = (header[1] & 0x80) != 0 ? ReadExact(2) : ReadExact(header[1] == 0x05 ? 5 : header[2] + 2);
                    var frame = new byte[header.Length + remainder.Length];
                    Buffer.BlockCopy(header, 0, frame, 0, header.Length);
                    Buffer.BlockCopy(remainder, 0, frame, header.Length, remainder.Length);
                    ValidateCrc(frame);
                    if ((frame[1] & 0x80) != 0) throw new IOException("Modbus RTU exception code: " + frame[2]);
                    var pdu = new byte[frame.Length - 3];
                    Buffer.BlockCopy(frame, 1, pdu, 0, pdu.Length);
                    return pdu;
                }
                catch { ClosePort(); throw; }
            }
        }

        private byte[] ReadExact(int count)
        {
            var result = new byte[count]; var offset = 0;
            while (offset < count) offset += _port.Read(result, offset, count - offset);
            return result;
        }

        private void EnsureOpen() { if (!_port.IsOpen) _port.Open(); }
        private void ClosePort() { if (_port.IsOpen) { try { _port.Close(); } catch { } } }
        private static byte Hi(ushort value) { return (byte)(value >> 8); }
        private static byte Lo(ushort value) { return (byte)value; }
        private static void ValidateOutput(int index) { if (index < 1 || index > 4) throw new ArgumentOutOfRangeException("index"); }

        private static ushort Crc(byte[] data, int count)
        {
            ushort crc = 0xFFFF;
            for (var i = 0; i < count; i++)
            {
                crc ^= data[i];
                for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
            }
            return crc;
        }

        private static void WriteCrc(byte[] frame, int count)
        {
            var crc = Crc(frame, count); frame[count] = (byte)crc; frame[count + 1] = (byte)(crc >> 8);
        }

        private static void ValidateCrc(byte[] frame)
        {
            var expected = Crc(frame, frame.Length - 2);
            if (frame[frame.Length - 2] != (byte)expected || frame[frame.Length - 1] != (byte)(expected >> 8)) throw new IOException("Invalid Modbus RTU CRC.");
        }

        public void Dispose() { lock (_sync) { ClosePort(); _port.Dispose(); } }
    }
}
