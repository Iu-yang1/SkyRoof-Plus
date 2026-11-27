using System.Buffers.Binary;
using System.Text;
using Grpc.Core;
using NAudio.Wave;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private readonly CheckBox microphoneEnabled = new() { Text = "Desktop MIC", AutoSize = true };
    private readonly ComboBox spectrumKind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly ComboBox resultKind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly RemoteSpectrumView spectrumView = new() { Dock = DockStyle.Fill };
    private readonly TextBox decoderText = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
    private readonly Label streamStatus = new() { AutoSize = true };
    private CancellationTokenSource? rxStream, spectrumStream, decoderStream;
    private DesktopMicrophone? microphone;

    private TabPage CreateSignalsPage()
    {
        var page = new TabPage("AUDIO / SPECTRUM / DECODE");
        spectrumKind.Items.AddRange(new object[] { StationSignalKind.StationSignalAfSpectrum,
            StationSignalKind.StationSignalRfMain, StationSignalKind.StationSignalRfSub });
        spectrumKind.SelectedIndex = 0;
        resultKind.Items.AddRange(new object[] { StationSignalKind.StationSignalFt4, StationSignalKind.StationSignalCw,
            StationSignalKind.StationSignalQ65, StationSignalKind.StationSignalTelemetry });
        resultKind.SelectedIndex = 0;
        var top = Row(ActionButton("PLAY RX", PlayRxAsync), ActionButton("STOP RX", () => StopStream(rxStream)),
            spectrumKind, ActionButton("SCOPE", ShowSpectrumAsync), resultKind,
            ActionButton("RESULTS", ShowDecoderAsync), ActionButton("STOP STREAMS", () => { StopSignalStreams(); return Task.CompletedTask; }));
        top.Dock = DockStyle.Top;
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(900, 600), SplitterDistance = 260 };
        split.Panel1.Controls.Add(spectrumView);
        split.Panel2.Controls.Add(decoderText);
        streamStatus.Dock = DockStyle.Bottom;
        page.Controls.Add(split);
        page.Controls.Add(top);
        page.Controls.Add(streamStatus);
        return page;
    }

    private static Task StopStream(CancellationTokenSource? source)
    {
        source?.Cancel();
        return Task.CompletedTask;
    }

    private void StopSignalStreams()
    {
        rxStream?.Cancel();
        spectrumStream?.Cancel();
        decoderStream?.Cancel();
    }

    private async Task PlayRxAsync()
    {
        rxStream?.Cancel();
        using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        rxStream = source;
        try
        {
            using var playback = new DesktopRxPlayback();
            await ConsumeAsync(StationSignalKind.StationSignalRxAudio, playback.Accept, source.Token);
        }
        finally { if (ReferenceEquals(rxStream, source)) rxStream = null; }
    }

    private async Task ShowSpectrumAsync()
    {
        if (spectrumKind.SelectedItem is not StationSignalKind kind) return;
        spectrumStream?.Cancel();
        using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        spectrumStream = source;
        try { await ConsumeAsync(kind, frame => spectrumView.Accept(kind, frame), source.Token); }
        finally { if (ReferenceEquals(spectrumStream, source)) spectrumStream = null; }
    }

    private async Task ShowDecoderAsync()
    {
        if (resultKind.SelectedItem is not StationSignalKind kind) return;
        decoderStream?.Cancel();
        using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        decoderStream = source;
        decoderText.Clear();
        try { await ConsumeAsync(kind, ShowDecoderFrame, source.Token); }
        finally { if (ReferenceEquals(decoderStream, source)) decoderStream = null; }
    }

    private void ShowDecoderFrame(StationSignalFrame frame)
    {
        if (decoderText.TextLength > 120000) decoderText.Clear();
        decoderText.AppendText(Encoding.UTF8.GetString(frame.Payload.Span) + Environment.NewLine);
    }

    private async Task ConsumeAsync(StationSignalKind kind, Action<StationSignalFrame> accept, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var call = client.Subscribe(kind, token);
                while (await call.ResponseStream.MoveNext(token))
                {
                    if (IsDisposed || token.IsCancellationRequested) return;
                    var frame = call.ResponseStream.Current;
                    accept(frame);
                    streamStatus.Text = $"{kind} · epoch {frame.Epoch} · lost {frame.LostFrames}";
                }
            }
            catch (RpcException error) when (error.StatusCode == StatusCode.Unavailable)
            {
                if (!IsDisposed) streamStatus.Text = $"{kind} · reconnecting";
            }
            catch (RpcException error) when (token.IsCancellationRequested && error.StatusCode == StatusCode.Cancelled) { return; }
            await Task.Delay(350, token);
        }
    }

    private void StartMicrophone(string transaction, ulong epoch)
    {
        StopMicrophone();
        microphone = new DesktopMicrophone(client, transaction, epoch, lifetime.Token);
        microphone.Start();
    }

    private void StopMicrophone()
    {
        microphone?.Dispose();
        microphone = null;
    }
}
