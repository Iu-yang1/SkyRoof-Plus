# SkyRoof Earth Map Engine V1

## 设计与实现

- P0：沿用地球视图的 AEQD 相机、地理覆盖区、图标与现有离线 DXCC 底图。在线底图抽象为 IMapSource，管理器与 GL 资源独立。
- P1：天地图 vec_c/cva_c、img_c/cia_c、ter_c/cta_c 组合。通过实际 WMTS Capabilities 获取地理矩阵名称、原点、尺寸与格式，使用 log2(MatrixWidth) 作为内部 LOD；不猜测服务级别编号。仅支持全球 2:1、256 px、CGCS2000 瓦片矩阵。
- P2：用矩形 AEQD 视口的外接球冠计算可见区域，跨经度环绕及极点保守覆盖；按共享槽位预算选择层级，祖先优先加载。4 个并行 HTTP 请求，异步图像解码，代数隔离；仅 RAM / GPU 驻留，不建立持久缓存。OpenGL 3.3 Texture Array + RGBA32I 稀疏索引；分片上传，父级回退，独立全屏三角形逆投影。
- P3：地图来源仅在设置中选择，地图右键恢复跟随和旋转器大圆方位指向（EL 0°），设置中的 Key/注记/瓦片容量，DPAPI 按当前 Windows 用户保护 Key。切换保持相机与缩放，缺 Key、服务、GPU 或局部瓦片缺失时保留内置地图。状态与来源归属在底部。
- P4：新增矩阵、经纬度、跨日期变更线和极点可见性、稀疏索引、RGBA 解码、DPAPI 和源切换取消测试，并运行 Windows Compile Check / aislop。

## 验收边界

实际天地图服务 Key、服务端授权、具体审图号和允许的投影/运行时缓存条款，需由使用者依所申请服务核实；本项目不提供公共 Key，不进行永久离线缓存或批量下载。真实服务图层矩阵和授权成功率、Windows 实际 OpenGL 渲染和高倍率坐标误差、拖动 60 FPS、真实电台同时运行的性能影响仍需实机验证。自动化数学/索引/异步测试不能替代真实 GPU 和在线地图的验收。内置地图路径不依赖网络。

## 关键文件

- SkyRoof/Maps/EarthTileMatrix.cs、EarthTileVisibility.cs、TiandituMapSource.cs、MapSourceManager.cs
- SkyRoof/OpenGL/GpuEarthTilePool.cs、EarthMapRenderer.cs、EarthMapShaders.cs
- SkyRoof/Settings/EarthMapSettings.cs
- VE3NEA.Dsp.Tests/EarthMapEngineTests.cs
