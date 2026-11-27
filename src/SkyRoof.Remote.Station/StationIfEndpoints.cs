using Grpc.Core;
using NAudio.CoreAudioApi;
using SkyRoof.Remote.Ipc;
using System.Runtime.InteropServices;

namespace SkyRoof.Remote.Station;

internal static class StationIfEndpoints
{
    internal static AfcEndpointList Read(CancellationToken token)
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            var result = new AfcEndpointList();
            foreach (var flow in new[] { DataFlow.Capture, DataFlow.Render })
            foreach (var device in devices.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                using (device)
                {
                    token.ThrowIfCancellationRequested();
                    var row = new AfcEndpoint { Id = device.ID, Name = device.FriendlyName, Loopback = flow == DataFlow.Render };
                    try
                    {
                        using var audio = device.AudioClient;
                        var format = audio.MixFormat;
                        row.SampleRate = format.SampleRate;
                        row.Channels = format.Channels;
                        row.Detail = format.SampleRate == 48000 ? "48 kHz mix format" : "Set Windows mix format to 48 kHz";
                    }
                    catch (COMException error) { row.Detail = error.Message; }
                    result.Endpoints.Add(row);
                }
            }
            return result;
        }
        catch (COMException error) { throw new RpcException(new Status(StatusCode.Unavailable, error.Message)); }
    }
}
