# P10 WebRTC C ABI two-peer DataChannel 原型

固定 libdatachannel v0.24.7 对应提交 800e849a1ede5667caaf8f6caa1eb21e4906dd87 及官方 submodule。编译时 NO_MEDIA=OFF，后续可加入标准 Audio Track。当前模拟信令路由两个独立 PeerConnection 的 SDP/ICE Candidate；通过真实 DTLS/SCTP DataChannel 交换并回显 skyroof。

本原型仅检验单机 WebRTC 的 ICE/SDP/DataChannel：未经过 FRP 公网 UDP、真实 Chrome/Edge、CGNAT、标准 Audio Track、Opus 编码或台站设备；不能以单机联通替代公网验收。
官方 API：https://libdatachannel.org/pages/reference.html
