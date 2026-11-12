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
            "Orekit 原生格式 DE430 星历下载网址；文件必须通过固定 Git Blob 校验，不支持直接填写 NAIF BSP 文件。",
        "OrbitSourceSettings.DataStatus" =>
            "当前设置目录下 UTC、EOP、DE430 文件的存在状态；星历质量和实际加载状态需另查轨道数据质量，更新后重启。",
        _ => null
    };
}
