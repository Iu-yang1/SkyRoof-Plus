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

Only Settings → Earth map / Tianditu → Default map source switches between the offline built-in DXCC map and Tianditu vector, imagery and terrain WMTS base maps. Apply settings to change the open map. For the native HTTPS client, register and verify a developer account, create a Server-side application in the Tianditu console, authorize Map Service (WMTS), and copy its application key (tk). Configure the egress IP whitelist if required. Enable/disable labels and set the shared GPU tile capacity in the same settings section. The key is protected by Windows DPAPI and is not included in the installer. Register at https://lbs.tianditu.gov.cn/ and check the service terms, attribution and approved map-display requirements for your intended use. No permanent tile cache or bulk download is implemented.

The map keeps its geographical centre, zoom and satellite overlays when switching sources. The online renderer obtains actual CGCS2000 geographic tile matrix identifiers from capabilities, requests only the visible cap and its ancestors, uploads decoded 256 px tiles incrementally into a shared GPU texture array, and falls back to parents or the built-in texture. Network, credentials, incompatible capabilities and GPU failures do not interrupt satellite tracking. Offline maps become pixelated at high zoom; actual provider coverage and resolution vary. The online attribution/status strip remains visible.

**中文：** 仅在设置 → 地球视图与天地图 → 默认地图源中选择内置地图 / 天地图矢量 / 影像 / 地形。天地图控制台创建服务端（服务器端）应用，开通地图 WMTS 服务，复制 tk，必要时配置出口 IP 白名单。地图右键可恢复跟随或指向点击位置的大圆初始方位角（仰角 0°）；遵循旋转器现有机械限位及 TRACK 状态。默认 256 槽（64 MiB 基础纹理），仅运行时驻留；地图源切换不更改相机、卫星或过境覆盖计算。服务或 GPU 不可用时仍显示内置地图。实机密钥、服务授权、地图审图信息及高倍率 GPU 显示精度仍需实际核验。
