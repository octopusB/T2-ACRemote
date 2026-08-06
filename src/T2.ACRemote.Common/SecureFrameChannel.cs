using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace T2.ACRemote.Common
{
    public sealed class SecureFrameChannel
    {
        private const int MaxPayload = 1024 * 1024;
        private readonly Stream _stream;
        private readonly byte[] _key;
        private readonly object _writeLock = new object();

        public SecureFrameChannel(Stream stream, string sharedSecret)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            _stream = stream;
            if (string.IsNullOrWhiteSpace(sharedSecret) || sharedSecret.Length < 16) throw new ArgumentException("SharedSecret must contain at least 16 characters.");
            _key = Encoding.UTF8.GetBytes(sharedSecret);
        }

        public void Write<T>(string type, T message)
        {
            var envelope = new WireEnvelope { Type = type, Payload = Json.Serialize(message) };
            var payload = Encoding.UTF8.GetBytes(Json.Serialize(envelope));
            byte[] mac;
            using (var hmac = new HMACSHA256(_key)) mac = hmac.ComputeHash(payload);
            var length = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(payload.Length));
            lock (_writeLock)
            {
                _stream.Write(length, 0, length.Length);
                _stream.Write(mac, 0, mac.Length);
                _stream.Write(payload, 0, payload.Length);
                _stream.Flush();
            }
        }

        public WireEnvelope ReadEnvelope()
        {
            var lengthBytes = ReadExact(4);
            var length = System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBytes, 0));
            if (length <= 0 || length > MaxPayload) throw new InvalidDataException("Invalid frame length.");
            var expectedMac = ReadExact(32);
            var payload = ReadExact(length);
            byte[] actualMac;
            using (var hmac = new HMACSHA256(_key)) actualMac = hmac.ComputeHash(payload);
            if (!ConstantTimeEquals(expectedMac, actualMac)) throw new InvalidDataException("Frame authentication failed.");
            return Json.Deserialize<WireEnvelope>(Encoding.UTF8.GetString(payload));
        }

        public static T Payload<T>(WireEnvelope envelope) { return Json.Deserialize<T>(envelope.Payload); }

        private byte[] ReadExact(int count)
        {
            var data = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = _stream.Read(data, offset, count - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            return data;
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            var diff = a.Length ^ b.Length;
            for (var i = 0; i < Math.Min(a.Length, b.Length); i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }

    public static class Json
    {
        public static string Serialize<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static T Deserialize<T>(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
    }
}
