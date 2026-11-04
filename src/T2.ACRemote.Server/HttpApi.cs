using System;
using System.IO;
using System.Linq;
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
        public HttpApi(BridgeRegistry registry, string prefix, string apiKey, int defaultLease) { _registry = registry; _apiKey = apiKey; _defaultLease = defaultLease; _listener.Prefixes.Add(prefix); }
        public void Start() { _listener.Start(); _thread = new Thread(Listen) { IsBackground = true }; _thread.Start(); }
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
                if (path.Length == 0) { Write(context, 200, "text/html; charset=utf-8", Dashboard); return; }
                if (!SecureEquals(context.Request.Headers["X-Api-Key"], _apiKey)) { WriteJson(context, 401, new ApiResponse { Success = false, Message = "API key 无效。" }); return; }
                if (path.Equals("api/bridges", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod == "GET") { WriteJson(context, 200, _registry.Statuses()); return; }
                var parts = path.Split('/');
                if (parts.Length == 3 && parts[0] == "api" && parts[1] == "bridges" && context.Request.HttpMethod == "GET")
                {
                    var session = _registry.Get(Uri.UnescapeDataString(parts[2]));
                    if (session == null) { WriteJson(context, 404, new ApiResponse { Success = false, Message = "工控机不在线。" }); return; }
                    WriteJson(context, 200, session.LastStatus); return;
                }
                if (parts.Length == 5 && parts[0] == "api" && parts[1] == "bridges" && parts[3] == "air-conditioner" && context.Request.HttpMethod == "POST")
                {
                    var session = _registry.Get(Uri.UnescapeDataString(parts[2]));
                    if (session == null) { WriteJson(context, 404, new ApiResponse { Success = false, Message = "工控机不在线。" }); return; }
                    ControlMode mode;
                    if (parts[4] == "start") mode = ControlMode.RemoteStart; else if (parts[4] == "stop") mode = ControlMode.RemoteStop; else if (parts[4] == "release") mode = ControlMode.Release; else { WriteJson(context, 404, new ApiResponse { Success = false, Message = "未知操作。" }); return; }
                    var lease = _defaultLease; int supplied;
                    if (int.TryParse(context.Request.QueryString["leaseSeconds"], out supplied)) lease = supplied;
                    var result = session.Send(mode, lease, 5000);
                    WriteJson(context, result.Success ? 200 : 409, result); return;
                }
                WriteJson(context, 404, new ApiResponse { Success = false, Message = "接口不存在。" });
            }
            catch (Exception ex) { WriteJson(context, 500, new ApiResponse { Success = false, Message = ex.Message }); }
        }

        private static void WriteJson<T>(HttpListenerContext context, int code, T value) { Write(context, code, "application/json; charset=utf-8", Json.Serialize(value)); }
        private static void Write(HttpListenerContext context, int code, string contentType, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body); context.Response.StatusCode = code; context.Response.ContentType = contentType; context.Response.ContentLength64 = bytes.Length;
            using (var output = context.Response.OutputStream) output.Write(bytes, 0, bytes.Length);
        }
        private static bool SecureEquals(string a, string b) { if (a == null || b == null) return false; var diff = a.Length ^ b.Length; for (var i = 0; i < Math.Min(a.Length, b.Length); i++) diff |= a[i] ^ b[i]; return diff == 0; }

        private const string Dashboard = @"<!doctype html>
<html lang='zh-CN'>
<head>
<meta charset='utf-8'>
<title>廊道空调远程控制</title>
<style>
body{font-family:Segoe UI,Microsoft YaHei,sans-serif;max-width:1100px;margin:36px auto;background:#f5f7fa;color:#172b4d}
header,section{background:white;padding:24px;border-radius:12px;margin-bottom:18px;box-shadow:0 2px 12px #0001}
input,button{padding:10px 14px;margin:4px;border:1px solid #ccd4df;border-radius:7px}
button{cursor:pointer;color:white;background:#1769aa}.stop{background:#c62828}.release{background:#59636e}
table{width:100%;border-collapse:collapse}th,td{padding:11px;border-bottom:1px solid #e8edf2;text-align:left}
.warn{color:#b42318}.status{min-height:24px;color:#59636e}.error{color:#b42318}
</style>
</head>
<body>
<header>
<h1>廊道空调远程控制</h1>
<p>API Key <input id='key' type='password'><button onclick='load()'>刷新</button></p>
<p id='status' class='status'>请输入 API Key 后点击刷新。</p>
<p class='warn'>操作前必须确认现场无人检修，并遵守机场设备操作规程。</p>
</header>
<section><table><thead><tr><th>登机桥</th><th>DI1</th><th>DO1</th><th>DO2</th><th>模式</th><th>操作</th></tr></thead><tbody id='rows'></tbody></table></section>
<script>
const key=()=>document.getElementById('key').value.trim();
const h=()=>({'X-Api-Key':key()});
const showStatus=(message,isError=false)=>{status.textContent=message;status.className=isError?'status error':'status'};
async function load(){
  if(!key()){showStatus('请输入 API Key 后点击刷新。');return}
  try{
    let r=await fetch('/api/bridges',{headers:h()});
    if(!r.ok){showStatus(await r.text(),true);return}
    let a=await r.json();
    rows.innerHTML=a.map(x=>`<tr><td>${x.BridgeId}</td><td>${x.BridgeRunning?'运行':'停止'}</td><td>${x.Do1RemoteStart?'闭合':'断开'}</td><td>${x.Do2CutOriginal?'闭合':'断开'}</td><td>${x.Mode}</td><td><button onclick=cmd('${encodeURIComponent(x.BridgeId)}','start')>远程开启</button><button class=stop onclick=cmd('${encodeURIComponent(x.BridgeId)}','stop')>远程关闭</button><button class=release onclick=cmd('${encodeURIComponent(x.BridgeId)}','release')>释放联动</button></td></tr>`).join('');
    showStatus(`已连接，共 ${a.length} 台工控机。`);
  }catch(e){showStatus('无法连接服务器：'+e.message,true)}
}
async function cmd(id,op){
  if(!key()){showStatus('请先输入 API Key。',true);return}
  if(!confirm('确认执行 '+op+'？'))return;
  try{
    let r=await fetch('/api/bridges/'+id+'/air-conditioner/'+op,{method:'POST',headers:h()});
    let message=await r.text();
    showStatus(message,!r.ok);
    if(r.ok)load();
  }catch(e){showStatus('操作失败：'+e.message,true)}
}
document.getElementById('key').addEventListener('keydown',e=>{if(e.key==='Enter')load()});
setInterval(load,3000);
</script>
</body>
</html>";
    }

    [System.Runtime.Serialization.DataContract]
    public sealed class ApiResponse
    {
        [System.Runtime.Serialization.DataMember] public bool Success { get; set; }
        [System.Runtime.Serialization.DataMember] public string Message { get; set; }
    }
}

