# ONVIF 连续视频（Remote V1 开发版）

Host 的摄像头面板仍支持快照及 PTZ。站点可配置 FFmpeg 可执行文件路径：`SKYROOF_FFMPEG_PATH`，使用摄像头 ONVIF Media profile 的 H.264 RTSP 流。摄像头 URI 与凭据通过 FFmpeg 标准输入传入，不出现在进程命令行或浏览器。RTSP/TCP 输入，复制视频编码并输出 fragmented MP4，浏览器 Media Source Extensions 播放。无需重新编码，不包含摄像头音频；H.265 转码不在当前实现范围。

通过 Host 认证访问视频，撤销认证或关闭连接时停止流及 FFmpeg。FFmpeg 由站点管理员安装，当前开发包不捆绑。确保实际选择 H.264 profile 与浏览器支持的编码格式；快照模式仍可用。

本地测试以实际 RTSP/TCP RTP H.264 fixture 和 FFmpeg 7.1.5 生成 fragmented MP4，检查 ftyp/avcC/moof；新版 Host HTTP fixture 和最终包 CI 尚待完成，不代表实际摄像头或浏览器端到端验收。
