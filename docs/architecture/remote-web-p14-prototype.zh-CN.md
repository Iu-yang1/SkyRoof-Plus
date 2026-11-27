# P14 React/Dockview Web 工作台原型

包含可拖拽的 Satellite、Radio、Spectrum、Rotator 和 FT4/CW 窗口，按工作区保存布局；用户使用 Host 的 HttpOnly Cookie 登录，SignalR MessagePack 协商已集成。模拟频谱通过 WebGL2 显示开发 Host 的 475-bin WSS 数据，并明确标注非真实设备。

第二工作区只是独立布局示例，目前不能连接第二台真实 Host。未接入实际 CAT、PTT、多普勒、旋转器、CW 或 FT4 DSP，也没有完整 WebGL 瀑布和实机测试，不得作为 P14 验收完毕。

开发启动：运行 Host Development 服务且监听 8076；运行 Web 目录 npm install 和 npm run dev。浏览器访问 Vite 监听地址，由 Vite 转发 API、SignalR 和模拟 WSS。

部署生产版还须完整后端功能及会话验证，当前没有 Release 构建。
