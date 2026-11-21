# SkyRoof Remote：Windows 双进程 gRPC/Named Pipe 原型

## 目标

将多进程运行时的控制平面从理论设计推进至可运行原型。

- 服务端是独立的 ASP.NET Core 10 进程，通过 Kestrel Named Pipe / HTTP2 暴露 gRPC StationProbe.Ping。
- 客户端是独立的 .NET 10 进程，使用 SocketsHttpHandler.ConnectCallback 和 NamedPipeClientStream 建立 gRPC 通道。
- 128 次 RPC 回显验证 nonce、请求 Epoch、真实服务端进程 ID 与单调时钟，输出 P50/P95 往返延迟。测试过程不访问网络或真实设备。
- Windows GitHub Actions 启动两个独立进程并测试真实 IPC。SDK 包依赖固定版本。

## 约束与下一步

此 Probe 用于证明双进程命名管道 IPC，可作为 Station/Device/Media 等正式服务的起点，但目前没有真实服务进程监管、用户 ACL、跨 Session 0 Windows Service 权限、共享内存、设备控制命令或状态流。

Windows Service 模式下，管道 ACL 必须根据实际 Windows 账户授权配置。本原型按默认当前用户安全边界运行，不应将默认管道可达性当成跨会话已验证的权限设计。

官方参考：
https://learn.microsoft.com/en-us/aspnet/core/grpc/interprocess-namedpipes?view=aspnetcore-10.0
