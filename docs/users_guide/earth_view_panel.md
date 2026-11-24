# Earth View

The Earth View panel shows the view of the Earth from the satellite. Use the radio buttons at the top of the panel to switch between two modes:

- **Real Time** centers the view on the satellite and highlights the area it can see at this moment.
- **Selected Pass** centers the view on your station and draws the coverage contour of the currently selected pass.

Use the mouse wheel to zoom the view in and out. Hold the left mouse button and drag to move the geographic view centre, including across the antimeridian. The view stays at the chosen centre while the satellite moves or the selected pass changes; the actual footprint and station markers remain geographically fixed to their own coordinates. Click **Reset View** (恢复跟随), or double-click the map, to resume the current mode's automatic centre without changing the zoom.

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

The map selector switches between the offline built-in DXCC map and Tianditu vector, imagery and terrain WMTS base maps. In Settings → Earth map / Tianditu, enter a service key authorized for desktop WMTS requests, enable/disable labels and set the shared GPU tile capacity. The key is protected by Windows DPAPI and is not included in the installer. Register at https://lbs.tianditu.gov.cn/ and check the service terms, attribution and approved map-display requirements for your intended use. No permanent tile cache or bulk download is implemented.

The map keeps its geographical centre, zoom and satellite overlays when switching sources. The online renderer obtains actual CGCS2000 geographic tile matrix identifiers from capabilities, requests only the visible cap and its ancestors, uploads decoded 256 px tiles incrementally into a shared GPU texture array, and falls back to parents or the built-in texture. Network, credentials, incompatible capabilities and GPU failures do not interrupt satellite tracking. Offline maps become pixelated at high zoom; actual provider coverage and resolution vary. The online attribution/status strip remains visible.

**中文：** 顶部选择内置地图 / 天地图矢量 / 影像 / 地形。设置 → 地球视图与天地图中配置服务端 API Key、注记及 GPU 瓦片容量。默认 256 槽（64 MiB 基础纹理），仅运行时驻留；地图源切换不更改相机、卫星或过境覆盖计算。服务或 GPU 不可用时仍显示内置地图。实机密钥、服务授权、地图审图信息及高倍率 GPU 显示精度仍需实际核验。
