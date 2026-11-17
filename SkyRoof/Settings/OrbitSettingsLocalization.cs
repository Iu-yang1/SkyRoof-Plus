namespace SkyRoof;

/// <summary>Localized labels for the unified Orekit ephemeris settings group.</summary>
internal static class OrbitSettingsLocalization
{
    internal static string Category(string source, UiLanguage language) =>
        language != UiLanguage.SimplifiedChinese ? source : source switch
        {
            "Orekit ephemeris" => "Orekit 星历与数据",
            "Orbit elements" => "轨道根数来源",
            "Satellite database" => "卫星数据库",
            _ => source
        };

    internal static string? Description(string key) => key switch
    {
        "OrbitSourceSettings.AutoDownloadOrekitData" =>
            "首次启动自动下载缺失的 IERS EOP 与 JPL DE430 数据；EOP 超过七天时刷新。无网络时使用已有缓存。",
        "OrbitSourceSettings.ShowSolarSystemTargets" =>
            "显示月球、太阳和金星。此选项在重启 SkyRoof 后应用。",
        "OrbitSourceSettings.OrekitDataDirectory" =>
            "Orekit 时间、地球定向参数与 JPL DE430 星历的统一目录；留空使用用户数据目录下的 orekit-data。切换目录后需重启。",
        "OrbitSourceSettings.OrekitEopSources" =>
            "IERS finals2000A.all 官方下载源，多个网址使用分号或换行分隔；按顺序尝试。",
        "OrbitSourceSettings.OrekitDe430Url" =>
            "Orekit 原生 DE430 星历来源，可按顺序填写 HTTPS 镜像或本地文件路径（分号、换行分隔）；每份文件必须通过固定 Git Blob 校验，不可直接使用 NAIF BSP。",
        "OrbitSourceSettings.SourceTimeoutSeconds" =>
            "每个卫星数据源镜像的请求超时秒数；超时后继续尝试下一镜像，下载错误不会覆盖已验证缓存。此配置不限制大型 JPL 星历的独立下载。",
        "OrbitSourceSettings.Je9pelSources" =>
            "JE9PEL 卫星频率辅助资料来源，可填多个 HTTPS URL 或本地 CSV 文件；下载失败保留已有数据，不会阻断卫星列表导入。",
        "OrbitSourceSettings.SatyamlZipSources" =>
            "gr-satellites 卫星数字遥测协议资料 ZIP 镜像；按顺序选择有效压缩包，完整提取成功后才替换本地 SatYAML 文件。",
        "OrbitSourceSettings.DataStatus" =>
            "当前设置目录下 UTC、EOP、DE430 文件的存在状态；星历质量和实际加载状态需另查轨道数据质量，更新后重启。",
        _ => null
    };
}
