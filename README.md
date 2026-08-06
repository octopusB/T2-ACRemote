# T2 廊道空调远程控制

面向登机桥工控机与 USR-IO424T-EWR V2 网络 IO 的两级远程控制系统，使用 C# 和 .NET Framework 4.8。

## 架构

```text
操作员浏览器/REST API -> 服务器程序 <- 长连接/HMAC -> 工控机中转程序 -> Modbus TCP -> USR-IO424T
```

- 服务器监听工控机主动连接，维护登机桥在线状态并提供浏览器控制台和 REST API。
- 工控机轮询 DI1，通过 DO1 控制新增并联启动回路，通过 DO2 控制串联于原启动回路的常闭继电器。
- 通信帧使用 HMAC-SHA256 验证，HTTP API 使用 `X-Api-Key`。生产环境仍应部署在机场专网/VPN 内，并在反向代理终止 HTTPS。

## 说明书映射

| 信号 | Modbus 地址 | 功能码 | 含义 |
|---|---:|---:|---|
| DO1 | `0x0000` | `0x01` / `0x05` | 新增远程启动回路；闭合为 `0xFF00` |
| DO2 | `0x0001` | `0x01` / `0x05` | 励磁后使外部常闭触点断开原启动回路 |
| DI1 | `0x0020` | `0x02` | 登机桥运行状态 |

设备默认 Modbus 地址在说明书示例中为十进制 17 (`0x11`)。Modbus TCP 端口由设备的 TCPS 监听端口配置决定；示例配置采用行业常用值 502，现场必须核对。

## 控制状态机

- `RemoteStart`：仅 DI1 为断开时允许；先释放 DO2，再闭合 DO1。
- `RemoteStop`：先断开 DO1；仅 DI1 有效时闭合 DO2，切断原联动回路。
- `Release`：DO1、DO2 全部断开，恢复登机桥原有联动控制。
- DI1 从停止变为运行时自动释放远程启动；DI1 从运行变为停止时自动释放 DO2。
- 非 `Release` 命令带有限时租约（默认 300 秒，10-3600 秒范围），超时自动释放两路输出。
- 工控机正常退出时尝试释放两路输出。断网/异常断电仍应由电气设计、设备 DO 掉电不保持配置和现场安全规程兜底。

## 构建

安装 Visual Studio 2022 或 Build Tools，并勾选“.NET 桌面生成工具”和 .NET Framework 4.8 Developer Pack：

```powershell
msbuild T2.ACRemote.sln /t:Rebuild /p:Configuration=Release
tests\T2.ACRemote.Tests\bin\Release\T2.ACRemote.Tests.exe
```

## 配置与运行

1. 在 IO 控制器中配置固定 IP、Modbus 从机模式和 TCPS 监听端口。
2. 复制并修改两个程序的 `.config`，尤其是 `SharedSecret`、`ApiKey`、设备 IP 和唯一 `BridgeId`。
3. 先启动 `T2.ACRemote.Server.exe`，再在各工控机启动 `T2.ACRemote.Bridge.exe`。
4. 服务器首次使用 `http://服务器地址:19002/`；Windows 可能需要管理员执行：

   ```powershell
   netsh http add urlacl url=http://+:19002/ user=DOMAIN\service-account
   ```

5. 防火墙仅允许工控机访问 TCP 19001，仅允许管理网访问 TCP 19002。

REST 示例：

```powershell
$headers = @{ 'X-Api-Key' = 'your-api-key' }
Invoke-RestMethod http://server:19002/api/bridges -Headers $headers
Invoke-RestMethod -Method Post http://server:19002/api/bridges/T2-BRIDGE-001/air-conditioner/start -Headers $headers
Invoke-RestMethod -Method Post http://server:19002/api/bridges/T2-BRIDGE-001/air-conditioner/stop -Headers $headers
Invoke-RestMethod -Method Post http://server:19002/api/bridges/T2-BRIDGE-001/air-conditioner/release -Headers $headers
```

## 投产前必须验证

这是控制真实机电设备的参考实现，不能跳过现场联调和安全评审：

1. 确认 DI1 有效电平与代码中的 `true=登机桥运行` 一致。
2. 确认 DO1/DO2 与端子接线、COM 公共端和外部继电器逻辑一致。
3. 将设备“输出状态保持”设置为一直不保持 (`0x00B6 = 0x0003`)，并断电验证。
4. 分别测试登机桥停止/运行、网络中断、服务器重启、工控机重启、IO 断电和粘连故障。
5. 由电气与安全负责人确认 DO2 切断原联动回路不会绕过消防、急停、检修或其他安全链路。

