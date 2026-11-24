# Earth View

The Earth View panel shows the view of the Earth from the satellite. Use the radio buttons at the top of the panel to switch between two modes:

- **Real Time** centers the view on the satellite and highlights the area it can see at this moment.
- **Selected Pass** centers the view on your station and draws the coverage contour of the currently selected pass.

Use the mouse wheel to zoom the view in and out. Hold the left mouse button and drag to move the geographic view centre, including across the antimeridian. The view stays at the chosen centre while the satellite moves or the selected pass changes; the actual footprint and station markers remain geographically fixed to their own coordinates. Right-click the map and select **Resume follow** (恢复跟随), or double-click the map, to resume the current mode's automatic centre without changing the zoom. Right-click a ground location and select **Point rotator here** to command the initial great-circle bearing from your station to that location, at 0° elevation for terrestrial operation. The existing manual rotator limits and active-pass TRACK ownership apply; the right-click alone never moves the antenna. Space outside the map disk and undefined bearings cannot be used as targets.

## Real Time

The highlighted area is what the satellite can see from its current position.
<br>
The satellite is above the horizon for the observers located in this area.

![Earth View in Real Time mode](../images/earth_view_realtime.png)

## Selected Pass

The blue contour outlines the entire area swept by the satellite footprint during the pass. Any observer inside this contour will have the satellite above the horizon at some point during the pass.

![Earth View showing pass coverage](../images/earth_view_pass.png)

While the pass is in progress, the panel switches back to Real Time mode so you can watch the satellite move across its coverage area:

![Earth View panel](../images/pass_coverage.png)



## Map sources / 地图源

Only Settings → Earth view / Map sources → Default map source switches map providers. The choices include the offline built-in DXCC map, Tianditu vector/imagery/terrain WMTS maps, Amap vector/imagery, and Google road/satellite/terrain/labelled terrain XYZ layers. Apply settings to change the open map. For the native HTTPS client, register and verify a developer account, create a Server-side application in the Tianditu console, authorize Map Service (WMTS), and copy its application key (tk). Configure the egress IP whitelist if required. Enable/disable labels and set the shared GPU tile capacity in the same settings section. The key is protected by Windows DPAPI and is not included in the installer. Register at https://lbs.tianditu.gov.cn/ and check the service terms, attribution and approved map-display requirements for your intended use. No permanent tile cache or bulk download is implemented.

The map keeps its geographical centre, zoom and satellite overlays when switching sources. For Tianditu the online renderer obtains actual CGCS2000 geographic tile matrix identifiers from capabilities; for experimental XYZ sources it uses square Web Mercator matrices and a GCJ-02 forward offset only for the corresponding layers. It requests only the visible cap and its ancestors, uploads decoded 256 px tiles incrementally into a shared GPU texture array, and falls back to parents or the built-in texture. Network, credentials, incompatible capabilities and GPU failures do not interrupt satellite tracking. Offline maps become pixelated at high zoom; actual provider coverage and resolution vary. The online attribution/status strip remains visible.

**中文：** 仅在设置 → 地球视图与地图源 → 默认地图源中选择内置地图、天地图、高德矢量/卫星影像、Google 街道/卫星影像/地形/带注记地形。天地图控制台创建服务端（服务器端）应用，开通地图 WMTS 服务，复制 tk，必要时配置出口 IP 白名单。地图右键可恢复跟随或指向点击位置的大圆初始方位角（仰角 0°）；遵循旋转器现有机械限位及 TRACK 状态。默认 256 槽（64 MiB 基础纹理），仅运行时驻留；地图源切换不更改相机、卫星或过境覆盖计算。服务或 GPU 不可用时仍显示内置地图。实机密钥、服务授权、地图审图信息及高倍率 GPU 显示精度仍需实际核验。

### Experimental Amap / Google XYZ sources

高德和 Google 使用用户提供的文章中的非官方 XYZ 地址，不使用天地图 Key，也不要求 Google Cloud 或高德开发者 API Key。高德卫星影像可通过“显示地图注记”叠加 style=8；Google 的街道和带注记地形自带注记。Google 的混合影像 y 和单独 h 不直接叠加，避免 WGS-84 影像与 GCJ-02 注记错位。Google 文章地址为 HTTP，网络传输未加密；不发送 API Key。非公开端点可能变更、返回非图像、受服务条款限制或无法访问；使用前请核对服务商授权与地图显示要求。高德 GCJ-02 使用常见近似正向算法及矩形适用范围，不是法定国界，也不声称达到测绘精度。Web Mercator 覆盖纬度为 ±85.05112878°，极区回退内置地图。卫星轨道、覆盖、台站、右键旋转器方位计算仍为原有地理坐标。
