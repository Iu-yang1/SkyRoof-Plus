namespace SkyRoof
{
  internal sealed class IcomScopeDiagnosticsReport
  {
    private static readonly string[] CoreFields =
    [
      "Data source:", "Control path:", "Scope write backend:", "Scope renderer:",
      "Complete sweeps captured:", "Selected complete sweeps processed:",
      "Processing queue dropped sweeps:", "Invalid scope frames:",
      "Control changes awaiting radio confirmation:", "Unconfirmed controls after timeout:"
    ];

    internal string Summary { get; }
    internal string FullText { get; }

    internal IcomScopeDiagnosticsReport(string status, string reception, string counters, string layout)
    {
      string[] lines = counters.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
      var core = lines.Where(line => CoreFields.Any(field =>
        line.StartsWith(field, StringComparison.Ordinal))).ToList();
      bool gpu = lines.Any(line => line.StartsWith("Scope renderer: OpenGL", StringComparison.Ordinal));
      string[] renderingFields = gpu
        ? ["GPU fresh waveform swaps:", "GPU fresh swap interval p95/p99/max:"]
        : ["UI fresh waveform OnPaint completions:", "Fresh waveform paint interval p95/p99/max:"];
      core.AddRange(lines.Where(line => renderingFields.Any(field =>
        line.StartsWith(field, StringComparison.Ordinal))));
      if (ShowInline(reception)) core.Insert(0, reception);
      Summary = string.Join(Environment.NewLine, new[] { status, "" }.Concat(core));
      FullText = string.Join(Environment.NewLine, status, reception, "", counters, "", layout);
    }

    // Suppress only normal streaming descriptions. Errors, no-data,
    // startup and radio-control feedback remain visible to the operator.
    internal static bool ShowInline(string status) =>
      !string.IsNullOrWhiteSpace(status) &&
      !status.StartsWith("Receiving native combined IC-9700 ", StringComparison.Ordinal) &&
      !status.StartsWith("Receiving high-rate native IC-9700 ", StringComparison.Ordinal) &&
      !status.StartsWith("Receiving IC-9700 CI-V 27 00 spectrum data", StringComparison.Ordinal);
  }
}
