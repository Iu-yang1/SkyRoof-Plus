# SkyRoof-Plus v.1.73

SkyRoof Plus 1.73 — **Responsive satellite selection, asynchronous orbit predictions and Orekit data refresh**.  
SkyRoof Plus 1.73 — **切换卫星与手动更新星历的 UI 卡顿修复**。

## 中文

### 功能改进

- **切换卫星与过境缓存：** 选择卫星时优先复用已计算的两小时过境结果，避免重复计算；缓存未命中时由后台线程预测未来一天的过境。后台任务包含 60 ms 连续切换防抖、当前卫星优先级、代次核对，过时结果不再覆盖当前选择，也不替换操作员人工指定的过境。下一次过境缓存以轨道跟踪器实例为键，有效约两分钟；更新轨道后自动使用新实例的缓存条目。
- **卫星群组：** 两天过境预测、排序、路径采样在单独后台任务中完成，主线程只接受当前群组结果，取消旧任务并为最近使用的群组缓存预测快照；台站位置、卫星数据库或 TLE 更新时使旧快照失效。
- **SDR 发射机切换：** 频段范围改变导致的过境重建改为异步，清除前一频段的过境数据并拒绝失效计算的回写；修改 TLE/OMM 时同步更新缓存状态。
- **地图与详情：** 卫星详情中的轨道位置计算、地球地图覆盖区域多点预测、卫星照片解码、缩放及磁盘写入移到后台；地图覆盖区域也进行短防抖与缓存，避免快速切换时重建旧卫星图层。
- **手动星历更新：** 设置窗口的 IERS EOP、DE430 数据下载、结构验证、哈希检查和文件缓存维护在后台工作线程执行。更新期间不再将文件校验放在 WinForms UI 同步上下文。手动和启动自动更新共享串行下载机制，避免 .download 文件并发写入。关闭设置窗口可取消请求。
- **DE430 完整性缓存：** 已校验成功且文件路径、长度、修改时间不变的 DE430 在当前进程内可复用校验结果；检测到变更仍执行完整 Git Blob 哈希校验。

### 计算与控制兼容性

- Orekit/IKVM 13.1.9 的 TLE/CCSDS OMM 传播与 JPL DE430 月球/太阳/金星计算仍使用上一版本的轨道算法。
- 站心方位角/仰角、距离及距离变化率、多普勒、CAT 和旋转器协议未改变；没有新增 TX/PTT 限制或功能锁定。
- 运行时结果由 UI 线程原子提交；预测尚未完成时可能暂时不显示未来过境，但界面应保持可交互。

### 软件验收与边界

- [PR #200](https://github.com/Iu-yang1/SkyRoof-Plus/pull/200) 的 Windows .NET 10 x64 编译/单元测试、Orekit/IKVM 安装包冒烟、`aislop` 质量门禁和双语 DocFX 检查；此正式安装包还须通过发布流水线的完整测试、安装包依赖完整性和下载后 SHA-256 校验。
- 新增异步结果代次校验测试，验证旧卫星结果不能覆盖当前卫星、手工选择的过境不被后台替换。
- 共享 CI 的测试通过**不是**用户电脑上的真实切星延迟、WinForms 首帧时间或手动更新响应时间测量；未进行 IC-9700、CAT、GS232B 物理设备测试。
- 本次用户允许创建正式迭代 Release。如构建环境未配置 SignPath，安装器仍可能 **Authenticode 未签名**；应以官方 GitHub Release 来源、文件 SHA-256 和实际构建日志核对，不能声称已签名。

## English

- **Responsive selection:** Use cached near-term passes immediately; compute missing 24-hour passes on prioritized background workers. Short debounce, track-identity cache and generation checks prevent stale pass results from overwriting current/manual selections.
- **Async group and SDR passes:** Compute detached two-day group and frequency-range snapshots off the UI thread; cancel obsolete requests and commit only the latest valid result. Group snapshots cache briefly and are invalidated when location, database or TLE/OMM changes.
- **Map/photo/detail:** Defer Orekit detail predictions, Earth coverage polygons and satellite picture decode/resize to background workers; avoid stale-image commits.
- **Manual Orekit refresh:** Network, EOP/DE430 validation and file checksums execute outside the WinForms synchronization context. Closing settings cancels updates; startup and manual tasks share serialized cache writes. Verified DE430 file metadata prevents redundant hashing while the file is unchanged.
- **No control restrictions:** CAT, Doppler, rotator and existing PTT capabilities remain available; no numerical orbit algorithm change.
- **Verification boundary:** CI compile/unit/integration/installed-runtime and quality checks are distinct from actual end-user WinForms latency or connected hardware acceptance. SignPath-unavailable Windows installers may be unsigned and must be verified against the published SHA-256.

**Work items:** [PR #200](https://github.com/Iu-yang1/SkyRoof-Plus/pull/200) · [v.1.72 release](https://github.com/Iu-yang1/SkyRoof-Plus/releases/tag/v.1.72) (unchanged).
