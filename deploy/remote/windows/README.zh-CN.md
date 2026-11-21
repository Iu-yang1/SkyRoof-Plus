# SkyRoof Remote V1 Windows x64 多进程开发测试包

此包包含 Device、Station、Host、Supervisor 四个 .NET 10 进程，以及 React/Dockview 网页，不是完整 SkyRoof Remote V1 正式版本。

- Device：独立控制 SkyCAT/rigctld 兼容 CAT TCP；处理上下行频率、模式、PTT、设备读回、崩溃事务恢复与超时释放
- Station：独立 gRPC 命名管道控制入口
- Host：Identity/SQLite、MessagePack SignalR、身份与权限、网页
- Supervisor：普通后台运行并兼容 Windows Service 托管，可监管上述进程

Windows x64 需 .NET 10 Runtime 和可访问的本机 SkyCAT/rigctld 服务。在 supervisor.json.example 基础上创建 supervisor.json，配置安装目录、台站设备连接、CAT 端口、服务环境变量和本机管理员初始化凭据 SKYROOF_BOOTSTRAP_ADMIN_USER / SKYROOF_BOOTSTRAP_ADMIN_PASSWORD。启动后管理员账号密码以 Identity 哈希形式写入 SQLite；不要将明文凭据提交到仓库。

在 PowerShell 设置 SKYROOF_SUPERVISOR_CONFIG 环境变量指向 supervisor.json，再执行 dotnet 路径/到/Supervisor/SkyRoof.Remote.Supervisor.dll。开发模式 Host 默认 http://127.0.0.1:8076；外网使用必须由 HTTPS/WSS 代理保护。FRP TCP/QUIC 与 UDP 的单独部署文档见 deploy/remote/README.zh-CN.md。

**验收方式：** GitHub Actions 从发布目录实际启动 Device、Station 和 Host，连接模拟 rigctld TCP 端点，验证多用户 PTT、角色/Token、断开释放等。不能将此结果视为 IC-9700 Direct LAN、音频/WebRTC Audio Track、DSP、卫星跟踪或 Windows Service Session 0 的完整验收。

**回滚：** 停止独立 Supervisor 子进程，不覆盖原 SkyRoof。确保发射已释放后可重新运行原版 SkyRoof；保留 SQLite 用户数据和本机事务日志以便问题追溯。
