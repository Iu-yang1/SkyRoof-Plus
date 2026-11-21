# Remote Supervisor P2 进程监管原型

已增加 Windows 服务兼容的 .NET Worker 进程。普通后台运行时通过 JSON 子进程清单启动受控进程；Windows Service 模式通过 AddWindowsService 接入 SCM。子进程异常退出后按可控退避重新启动，不恢复发射中的人工 PTT；关闭 Supervisor 时终止其所启动的子进程。

独立 CI 故障注入：模拟 Device 第一次启动即退出码 17，验证 Supervisor 重启该进程。此测试不是 Windows SCM 开机、注销、Session 0 ACL 或真实设备 PTT 复位验收。

剩余：设备健康探针、gRPC 状态上报、崩溃后设备 readback、进程顺序依赖、配置迁移和 Windows Service 实机验证。
