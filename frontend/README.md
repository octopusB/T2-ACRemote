# 独立前端

这是一个无构建依赖的静态前端，可与 `T2.ACRemote.Server` 分开部署。

## 本地运行

不要直接双击 `index.html`。在仓库根目录启动静态文件服务器：

```powershell
python -m http.server 8080 --directory frontend
```

然后访问：

```text
http://localhost:8080/
```

如果使用 IIS、Nginx 或其他 Web 服务器，只需将 `frontend` 目录作为静态站点根目录。

## 航班联动页面

- “廊桥控制”页面可设置提前开启分钟数、航班后关闭分钟数，并可一键开启/关闭所有在线 Bridge。
- 每个 Bridge 都有独立的航班联动开关。
- “查看该机位航班”会跳转航班列表并自动筛选对应机位；275A、275B 会统一筛选 275。
- “航班列表”展示航班号、机位、航线、预计飞行时间、计划/预计到达时间、动态偏差、状态和更新时间。
- 当前数据源明确标记为 `MOCK-FLIGHTS`；后续数据库接入在 Server 的 `IFlightDataProvider` 中实现。

## Server 配置

浏览器跨域访问需要 Server 允许前端来源。默认允许：

```text
http://10.1.3.5:8080
http://localhost:8080
http://localhost:18080
http://127.0.0.1:8080
http://127.0.0.1:18080
```

可通过命令行修改，多个来源使用英文逗号分隔：

```text
T2.ACRemote.Server.exe --AllowedOrigins=http://10.1.3.5:8080,http://管理电脑地址:8080
```

生产环境应只配置实际使用的前端来源，不要开放任意来源。API Key 仅保存在当前浏览器标签页的 `sessionStorage` 中。
