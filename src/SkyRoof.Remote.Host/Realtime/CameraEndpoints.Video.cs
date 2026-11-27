namespace SkyRoof.Remote.Host.Realtime;

public static partial class CameraEndpoints
{
    private static async Task StreamVideoAsync(string profile, bool media2, HttpContext context)
    {
        if (!await LiveAudioWebSocket.StillAuthorizedAsync(context)) { context.Response.StatusCode = 403; return; }
        var settings = CameraSettings.Read();
        string? executable = Environment.GetEnvironmentVariable("SKYROOF_FFMPEG_PATH");
        if (settings.Endpoint.Length == 0 || string.IsNullOrWhiteSpace(executable))
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsJsonAsync(new { error = "Configure station ONVIF and FFmpeg executable." });
            return;
        }
        try
        {
            using var client = settings.Open();
            var uri = await client.GetStreamUriAsync(new SkyRoof.OnvifMediaProfile(profile, "", media2), context.RequestAborted);
            if (settings.Username.Length > 0 && uri.UserInfo.Length == 0)
                uri = new UriBuilder(uri) { UserName = settings.Username, Password = settings.Password }.Uri;
            await CameraRtspRemux.StreamAsync(executable, uri, context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception error) when (CameraSettings.IsProtocolError(error) || error is System.ComponentModel.Win32Exception)
        {
            if (context.Response.HasStarted) context.Abort();
            else
            {
                context.Response.StatusCode = 502;
                await context.Response.WriteAsJsonAsync(new { error = "Camera video unavailable", type = error.GetType().Name });
            }
        }
    }
}
