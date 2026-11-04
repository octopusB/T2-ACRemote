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
