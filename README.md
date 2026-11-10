# T2 廊道空调远程控制

面向登机桥工控机与 USR-IO424T-EWR V2 网络 IO 的两级远程控制系统，使用 C# 和 .NET 10。Server 与 Bridge 可发布为携带运行时的 32 位单文件 EXE。

## 架构

```text
独立前端/REST 客户端 -> Server API <- 长连接/HMAC -> 工控机中转程序 -> Modbus TCP -> USR-IO424T
```

- 服务器监听工控机主动连接，维护登机桥在线状态并只提供 JSON REST API；前端单独部署。
- 工控机轮询 DI1，通过 DO1 控制新增并联启动回路，通过 DO2 控制串联于原启动回路的常闭继电器。
- 航班联动服务根据预计到达时间提前开启空调，并在到达后的可配置空闲时间内没有新航班时自动关闭；275A、275B 等多桥统一映射到机位 275。
- 通信帧使用 HMAC-SHA256 验证，HTTP API 使用 `X-Api-Key`。生产环境仍应部署在机场专网/VPN 内，并在反向代理终止 HTTPS。

## 航班联动

- 默认提前 `15` 分钟开启，到达后 `20` 分钟没有新航班时关闭，页面可在 `0-240` 分钟范围内调整。
- 支持每个 Bridge 单独开启/关闭，也支持一键开启或关闭全部当前在线 Bridge。
- 模拟数据按 `CollectingSystem.coll_flight` 的 MongoDB 文档字段生成（包括 `_id`、`FLID`、`AFID`、`flt`、`sign_io`、`stand`、`st`、`et`、`at`、`u_t` 等），同时生成到达和出发航班；空调联动只使用 `sign_io=A`、未删除且有机位的到达航班。
- 模拟数据提供 275、276、277、278、301、302 等机位的未来航班，每 10 秒轻微调整 `et` 预计到达时间，到达后填写 `at`，并自动补充后续航班。
- `IFlightDataProvider` 是后续数据库接入点。生产实现读取数据库后，只需将记录映射为 `FlightRecord` 并在 Server 启动时替换 `MockFlightDataProvider`。
- 航班联动关闭时会发送 `Release`，释放 DO1/DO2 并恢复原登机桥联动控制。

## 说明书映射

| 信号 | Modbus 地址 | 功能码 | 含义 |
|---|---:|---:|---|
| DO1 | `0x0000` | `0x01` / `0x05` | 新增远程启动回路；闭合为 `0xFF00` |
| DO2 | `0x0001` | `0x01` / `0x05` | 励磁后使外部常闭触点断开原启动回路 |
| DI1 | `0x0020` | `0x02` | 登机桥运行状态 |

设备默认 Modbus 地址在说明书示例中为十进制 17 (`0x11`)。Modbus TCP 端口由设备的 TCPS 监听端口配置决定；本项目默认配置采用 `28899`，现场必须核对。

## 控制状态机

- `RemoteStart`：仅 DI1 为断开时允许；先释放 DO2，再闭合 DO1。
- `RemoteStop`：先断开 DO1；仅 DI1 有效时闭合 DO2，切断原联动回路。
- `Release`：DO1、DO2 全部断开，恢复登机桥原有联动控制。
- DI1 从停止变为运行时自动释放远程启动；DI1 从运行变为停止时自动释放 DO2。
- 非 `Release` 命令带有限时租约（默认 300 秒，10-3600 秒范围），超时自动释放两路输出。
- 工控机正常退出时尝试释放两路输出。断网/异常断电仍应由电气设计、设备 DO 掉电不保持配置和现场安全规程兜底。

## 构建与单文件发布

安装带 .NET 10 SDK 的 Visual Studio 或独立 .NET 10 SDK：

```powershell
dotnet build T2.ACRemote.sln -c Release
dotnet run --project tests\T2.ACRemote.Tests\T2.ACRemote.Tests.csproj -c Release --no-build

dotnet publish src\T2.ACRemote.Bridge\T2.ACRemote.Bridge.csproj `
  -p:PublishProfile=SingleFile-x86 -o artifacts\bridge

dotnet publish src\T2.ACRemote.Server\T2.ACRemote.Server.csproj `
  -p:PublishProfile=SingleFile-x86 -o artifacts\server
```

发布结果分别只有：

```text
artifacts\bridge\T2.ACRemote.Bridge.exe
artifacts\server\T2.ACRemote.Server.exe
```

EXE 内含 .NET 运行时，不要求目标计算机安装 .NET Framework。

在 Visual Studio 中也可右键项目选择“发布”，然后选择仓库自带的 `SingleFile-x86` 配置文件。

## 独立前端

`frontend` 目录提供无构建依赖的 HTML/CSS/JavaScript 控制台，可部署到 IIS、Nginx 或任意静态文件服务器。开发测试可在仓库根目录运行：

```powershell
python -m http.server 8080 --directory frontend
```

访问 `http://localhost:8080/`，填写 Server API 地址与 API Key。Server 默认允许 `10.1.3.5:8080` 以及本机 `localhost/127.0.0.1` 的 `8080`、`18080` 端口跨域访问，并支持浏览器私有网络访问预检；部署到其他地址时，通过 `--AllowedOrigins=来源1,来源2` 明确配置允许来源。

前端包含“廊桥控制”和“航班列表”两个视图。在廊桥列表点击“查看该机位航班”会跳转到航班列表并自动筛选机位；例如 275A、275B 均筛选为 275。

## 配置与运行

1. 在 IO 控制器中配置固定 IP、Modbus 从机模式和 TCPS 监听端口。
2. 先启动 `T2.ACRemote.Server.exe`，再在各工控机启动 `T2.ACRemote.Bridge.exe`。直接双击时使用内置默认参数。
3. 需要修改参数时，在 Windows 快捷方式“目标”末尾追加 `--名称=值`，或者设置同名的 `T2AC_名称` 环境变量。例如：

   ```text
   T2.ACRemote.Server.exe --SharedSecret=至少16位共享密钥 --ApiKey=管理密钥 --AllowedOrigins=http://10.1.3.5:8080
   T2.ACRemote.Bridge.exe --BridgeId=275 --ServerHost=10.1.3.5 --SharedSecret=至少16位共享密钥 --IoHost=192.168.10.1
   ```

   可用参数可通过 `程序.exe --help` 查看。命令行优先于环境变量，环境变量优先于内置默认值。
4. Server API 默认地址为 `http://服务器地址:19002/`，根路径返回 JSON 服务信息，不再提供内嵌网页；Windows 可能需要管理员执行：

   ```powershell
   netsh http add urlacl url=http://+:19002/ user=DOMAIN\service-account
   ```

5. 防火墙仅允许工控机访问 TCP 19001，仅允许管理网访问 TCP 19002。

> 兼容性警告：微软不支持 .NET 10 在 Windows 7/Windows Embedded Standard 7 上运行。此 `win-x86` 单文件版本是按用户现有 .NET 10 项目的发布方式生成，必须在目标工控机上完成启动、网络、加密、Modbus、长时间运行和异常恢复测试；不能仅以“能够双击启动”作为投产依据。

REST 示例：

```powershell
$headers = @{ 'X-Api-Key' = 'your-api-key' }
Invoke-RestMethod http://server:19002/api/bridges -Headers $headers
Invoke-RestMethod -Method Post http://server:19002/api/bridges/275/air-conditioner/start -Headers $headers
Invoke-RestMethod -Method Post http://server:19002/api/bridges/275/air-conditioner/stop -Headers $headers
Invoke-RestMethod -Method Post http://server:19002/api/bridges/275/air-conditioner/release -Headers $headers
Invoke-RestMethod http://server:19002/api/flights -Headers $headers
Invoke-RestMethod http://server:19002/api/flights?stand=275 -Headers $headers
Invoke-RestMethod http://server:19002/api/flight-linkage -Headers $headers
Invoke-RestMethod -Method Post http://server:19002/api/bridges/275/flight-linkage/enable -Headers $headers
Invoke-RestMethod -Method Post http://server:19002/api/flight-linkage/all/enable -Headers $headers
```

## 投产前必须验证

这是控制真实机电设备的参考实现，不能跳过现场联调和安全评审：

1. 确认 DI1 有效电平与代码中的 `true=登机桥运行` 一致。
2. 确认 DO1/DO2 与端子接线、COM 公共端和外部继电器逻辑一致。
3. 将设备“输出状态保持”设置为一直不保持 (`0x00B6 = 0x0003`)，并断电验证。
4. 分别测试登机桥停止/运行、网络中断、服务器重启、工控机重启、IO 断电和粘连故障。
5. 由电气与安全负责人确认 DO2 切断原联动回路不会绕过消防、急停、检修或其他安全链路。

