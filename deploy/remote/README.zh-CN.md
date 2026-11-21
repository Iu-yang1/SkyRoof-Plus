# SkyRoof Remote · FRP TCP/QUIC + UDP 公网部署草案

本目录只提供示例配置和说明，不包含自动安装脚本，不修改用户已有 frps。技术参考为同账户维护的 TX-5DR 公网连接设计，但不复用 TX-5DR 的框架或服务端进程。

## 拓扑

浏览器 HTTPS/WSS -> Caddy/Nginx 443/TCP -> frps TCP 18076 -> frpc -> Station Host 8076/TCP。
浏览器 WebRTC UDP -> frps 52000/UDP -> frpc -> Station Media 50110/UDP。
frpc 控制链路：frps 7000/TCP，或者按配置选择 7000/UDP QUIC。

WebRTC FRP UDP 的 ICE Candidate 应明确发布公网域名的实际公网 IP 和 52000 端口，并同时保留可用的 LAN Candidate。若无 TURN 且 ICE 不可达，音频应按用户已确认的协议策略选择 WSS。当前 Media 真实 ICE/UDP Mux 尚未实现，不把部署配置示例视为实际联通验收。

## 安装与检查顺序

1. 部署官方 frps，设置独立 Token、带可信 CA 的服务端 TLS 证书；frpc 配置匹配的 trustedCaFile。
2. 先使用 frpc verify -c frpc-tcp.toml 验证语法，再用 frps -c frps.toml 启动测试。这里只验证配置解析，不代表信令与音频联通。
3. 云安全组/主机防火墙开放 TCP 443、TCP 7000、UDP 52000；选择 QUIC 时还需 UDP 7000。公网 TCP 18076 必须拒绝直连，只允许 VPS 本机反向代理访问。
4. 配置 Caddy 或 Nginx，验证公网 HTTPS 证书、SignalR 长连接、二进制 WSS；不要求同时安装两种反向代理。
5. 将连接模式写入台站设置：Remote Host 8076、本地媒体 UDP 50110、公网映射端口 UDP 52000。
6. 在真实 WebRTC 实现完成后，验证 ICE 公网 Candidate 和 DTLS/SRTP/DataChannel；比较 FRP TCP 与 QUIC 的实际丢包、音频端到端延迟和媒体缓冲增长。

注意：FRP UDP 代理不等于 TURN。即使入口媒体为 UDP，frps/frpc 底层转发仍可能承载在可靠 TCP/QUIC 连接上，不可凭协议名称推断丢包行为。FRP Token 也不代替 Station Host 的用户认证。生产部署的 HTTPS、会话、Cookie 及 WSS 必须经反向代理完整验收。

官方来源：https://gofrp.org/en/docs/reference/server-configures/ ，https://gofrp.org/en/docs/reference/client-configures/ 。
