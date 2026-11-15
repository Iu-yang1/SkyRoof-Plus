using System.Reflection;
using SkyRoof.Orbit.Contracts;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: InstallerSmoke <installed-SkyRoof-root>");
    return 2;
}

try
{
    string root = Path.GetFullPath(args[0]);
    string runtimeAssembly = Path.Combine(root, "SkyRoof.Orbit.Orekit.dll");
    string dataRoot = Path.Combine(root, "orekit-data");
    if (!File.Exists(runtimeAssembly) || !Directory.Exists(dataRoot))
        throw new FileNotFoundException("Installed Orekit runtime or data is missing.", runtimeAssembly);

    // Load localization from the actual installed root. Compile-time xUnit tests
    // pass even when the Inno payload silently omits zh-CN satellite assemblies.
    string skyRoofAssemblyFile = Path.Combine(root, "SkyRoof.dll");
    string zhResourceFile = Path.Combine(root, "zh-CN", "SkyRoof.resources.dll");
    if (!File.Exists(skyRoofAssemblyFile) || !File.Exists(zhResourceFile))
        throw new FileNotFoundException("Installed SkyRoof binary or zh-CN satellite resources missing.");

    Assembly appAssembly = Assembly.LoadFrom(skyRoofAssemblyFile);
    var uiResources = new System.Resources.ResourceManager(
        "SkyRoof.Localization.UiStrings", appAssembly);
    var chineseCulture = System.Globalization.CultureInfo.GetCultureInfo("zh-CN");
    var englishCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
    foreach (var (key, chinese, english) in new (string Key, string Chinese, string English)[]
    {
        ("Menu.File", "文件(&F)", "&File"),
        ("Menu.Tools", "工具(&T)", "&Tools"),
        ("Menu.Language", "语言(&L)", "&Language"),
        ("Panel.Passes", "卫星过境", "Satellite Passes"),
        ("Panel.FrequencyControl", "频率控制", "Frequency Control")
    })
    {
        string? zh = uiResources.GetString(key, chineseCulture);
        string? en = uiResources.GetString(key, englishCulture);
        if (zh != chinese || en != english)
            throw new InvalidOperationException(
                $"Installed UI resources for {key} were wrong: zh-CN='{zh}', en-US='{en}'.");
    }
    Console.WriteLine("Installed SkyRoof UI localization OK: zh-CN and en-US resource resolution");

    // Intentionally avoid an AssemblyLoadContext.Resolving handler so missing
    // runtime dependencies cannot be hidden by the test harness.
    Assembly assembly = Assembly.LoadFrom(runtimeAssembly);
    Type contextType = assembly.GetType("SkyRoof.Orbit.Orekit.OrekitDataContext", true)
        ?? throw new TypeLoadException("Installed OrekitDataContext is absent");
    MethodInfo init = contextType.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public)
        ?? throw new MissingMethodException(contextType.FullName, "Initialize");
    init.Invoke(null, [dataRoot]);

    Type backendType = assembly.GetType("SkyRoof.Orbit.Orekit.OrekitOrbitBackend", true)
        ?? throw new TypeLoadException("Installed OrekitOrbitBackend is absent");
    if (Activator.CreateInstance(backendType) is not IOrbitBackend backend)
        throw new InvalidCastException("Installed Orekit plugin does not implement IOrbitBackend");

    const string line1 = "1 00005U 58002B   00179.78495062  .00000023  00000-0  28098-4 0  4753";
    const string line2 = "2 00005  34.2682 348.7242 1859667 331.7664  19.3264 10.82419157413667";
    IOrbitTrack track = backend.CreateTleTrack(line1, line2);
    if (track.NoradNumber != 5)
        throw new InvalidOperationException("Installed TLE parser changed NORAD number");

    var utc = new DateTimeOffset(2000, 6, 27, 18, 50, 19, TimeSpan.Zero);
    OrbitState state = track.PredictTeme(utc);
    OrbitGeodetic geo = track.PredictGeodetic(utc);
    OrbitObservation obs = track.Observe(new GroundStationSpec(28.5, 115.9, 40), utc);
    if (!double.IsFinite(state.XMetres) ||
        !double.IsFinite(geo.LongitudeDegrees) ||
        !double.IsFinite(obs.RangeRateMetresPerSecond) ||
        obs.RangeMetres <= 0)
        throw new InvalidOperationException("Installed Orekit calculations returned invalid values");

    var omm = new OrbitMeanElements(
        100001, "2026-001A", 'U',
        new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero),
        15.5, 0.00001, 0, 0.001, 51.6, 120,
        80, 25, 0.0001, 0, 999, 12345);
    IOrbitTrack ommTrack = backend.CreateOmmTrack(omm);
    if (ommTrack.NoradNumber != 100001 ||
        !double.IsFinite(ommTrack.PredictTeme(omm.EpochUtc.AddMinutes(10)).XMetres))
        throw new InvalidOperationException("Installed Orekit OMM / six-digit NORAD failed");

    Console.WriteLine($"Installed Orekit plugin OK: {root}");
    Console.WriteLine($"TEME X: {state.XMetres:F3} m, range rate: {obs.RangeRateMetresPerSecond:F6} m/s");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
