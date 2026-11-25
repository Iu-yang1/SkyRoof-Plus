using System.Diagnostics;
using System.Text;

namespace SkyRoof.Remote.Host.Realtime;

internal static class CameraRtspRemux
{
    internal static async Task StreamAsync(string executable, Uri uri, HttpContext context)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        // Remux only. The camera encodes video and the browser decodes it.
        foreach (string arg in new[] { "-hide_banner", "-loglevel", "error", "-f", "concat", "-safe", "0",
            "-protocol_whitelist", "pipe,rtsp,tcp,udp,rtp,tls,crypto", "-i", "pipe:0",
            "-map", "0:v:0", "-c:v", "copy", "-an", "-f", "mp4",
            "-movflags", "frag_keyframe+empty_moov+default_base_moof", "pipe:1" })
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Could not start camera remux.");
        Task drain = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        try
        {
            var ct = context.RequestAborted;
            // ffconcat quoting keeps credentials out of argv and avoids command interpolation.
            string escaped = uri.AbsoluteUri.Replace("'", "'\\''", StringComparison.Ordinal);
            await process.StandardInput.WriteAsync(("ffconcat version 1.0\nfile '" + escaped +
                "'\noption rtsp_transport tcp\n").AsMemory(), ct);
            process.StandardInput.Close();
            await CopyAsync(process.StandardOutput.BaseStream, context);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await drain;
        }
    }

    private static async Task CopyAsync(Stream input, HttpContext context)
    {
        var ct = context.RequestAborted;
        byte[] buffer = new byte[65536];
        bool started = false;
        var authorization = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            var read = input.ReadAsync(buffer.AsMemory(), ct).AsTask();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (!read.IsCompleted)
            {
                await Task.WhenAny(read, Task.Delay(1000, ct));
                ct.ThrowIfCancellationRequested();
                if (DateTimeOffset.UtcNow >= deadline) throw new IOException("Camera stream stalled.");
                if (DateTimeOffset.UtcNow >= authorization)
                {
                    if (!await LiveAudioWebSocket.StillAuthorizedAsync(context)) return;
                    authorization = DateTimeOffset.UtcNow.AddSeconds(1);
                }
            }
            int count = await read;
            if (count == 0)
            {
                if (!started) throw new IOException("Camera produced no MP4 data.");
                return;
            }
            if (DateTimeOffset.UtcNow >= authorization)
            {
                if (!await LiveAudioWebSocket.StillAuthorizedAsync(context)) return;
                authorization = DateTimeOffset.UtcNow.AddSeconds(1);
            }
            if (!started)
            {
                context.Response.ContentType = "video/mp4";
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["X-Accel-Buffering"] = "no";
                started = true;
            }
            await context.Response.Body.WriteAsync(buffer.AsMemory(0, count), ct);
            await context.Response.Body.FlushAsync(ct);
        }
    }
}
