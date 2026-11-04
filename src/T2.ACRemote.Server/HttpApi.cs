using System;
using System.Net;
using System.Text;
using System.Threading;
using T2.ACRemote.Common;

namespace T2.ACRemote.Server
{
    public sealed class HttpApi
    {
        private readonly BridgeRegistry _registry;
        private readonly HttpListener _listener = new HttpListener();
        private readonly string _apiKey;
        private readonly int _defaultLease;
        private Thread _thread;

        public HttpApi(BridgeRegistry registry, string prefix, string apiKey, int defaultLease)
        {
            _registry = registry;
            _apiKey = apiKey;
            _defaultLease = defaultLease;
            _listener.Prefixes.Add(prefix);
        }

        public void Start()
        {
            _listener.Start();
            _thread = new Thread(Listen) { IsBackground = true };
            _thread.Start();
        }

        public void Stop() { try { _listener.Stop(); } catch { } }

        private void Listen()
        {
            while (_listener.IsListening)
            {
                try { var context = _listener.GetContext(); ThreadPool.QueueUserWorkItem(_ => Handle(context)); }
                catch (HttpListenerException) { if (_listener.IsListening) throw; }
            }
        }

        private void Handle(HttpListenerContext context)
        {
            try
            {
                var path = context.Request.Url.AbsolutePath.Trim('/');
                if (path.Length == 0 && context.Request.HttpMethod == "GET")
                {
                    WriteJson(context, 200, new ApiResponse { Success = true, Message = "T2 ACRemote Server API" });
                    return;
                }
                if (!SecureEquals(context.Request.Headers["X-Api-Key"], _apiKey))
                {
                    WriteJson(context, 401, new ApiResponse { Success = false, Message = "API key 无效。" });
                    return;
                }
                if (path.Equals("api/bridges", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod == "GET")
                {
                    WriteJson(context, 200, _registry.Statuses());
                    return;
                }

                var parts = path.Split('/');
                if (parts.Length == 3 && parts[0] == "api" && parts[1] == "bridges" && context.Request.HttpMethod == "GET")
                {
                    var session = _registry.Get(Uri.UnescapeDataString(parts[2]));
                    if (session == null) { WriteJson(context, 404, new ApiResponse { Success = false, Message = "工控机不在线。" }); return; }
                    WriteJson(context, 200, session.LastStatus);
                    return;
                }
                if (parts.Length == 5 && parts[0] == "api" && parts[1] == "bridges" && parts[3] == "air-conditioner" && context.Request.HttpMethod == "POST")
                {
                    var session = _registry.Get(Uri.UnescapeDataString(parts[2]));
                    if (session == null) { WriteJson(context, 404, new ApiResponse { Success = false, Message = "工控机不在线。" }); return; }

                    ControlMode mode;
                    if (parts[4] == "start") mode = ControlMode.RemoteStart;
                    else if (parts[4] == "stop") mode = ControlMode.RemoteStop;
                    else if (parts[4] == "release") mode = ControlMode.Release;
                    else { WriteJson(context, 404, new ApiResponse { Success = false, Message = "未知操作。" }); return; }

                    var lease = _defaultLease;
                    int supplied;
                    if (int.TryParse(context.Request.QueryString["leaseSeconds"], out supplied)) lease = supplied;
                    var result = session.Send(mode, lease, 5000);
                    WriteJson(context, result.Success ? 200 : 409, result);
                    return;
                }
                WriteJson(context, 404, new ApiResponse { Success = false, Message = "接口不存在。" });
            }
            catch (Exception ex) { WriteJson(context, 500, new ApiResponse { Success = false, Message = ex.Message }); }
        }

        private static void WriteJson<T>(HttpListenerContext context, int code, T value)
        {
            Write(context, code, "application/json; charset=utf-8", Json.Serialize(value));
        }

        private static void Write(HttpListenerContext context, int code, string contentType, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = code;
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = bytes.Length;
            using (var output = context.Response.OutputStream) output.Write(bytes, 0, bytes.Length);
        }

        private static bool SecureEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            var diff = a.Length ^ b.Length;
            for (var i = 0; i < Math.Min(a.Length, b.Length); i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }

    [System.Runtime.Serialization.DataContract]
    public sealed class ApiResponse
    {
        [System.Runtime.Serialization.DataMember] public bool Success { get; set; }
        [System.Runtime.Serialization.DataMember] public string Message { get; set; }
    }
}
