namespace SkyRoof.Remote.Host.Realtime;

public sealed partial class StationControlHub
{
    public async Task<RemoteRotatorPosition> GetRotatorPosition()
    {
        await VerifyCurrentSessionAsync(requireOperator: false);
        return await device.ReadRotatorAsync(Context.ConnectionAborted);
    }

    public async Task<RemoteRotatorPosition> PointRotator(double az, double el)
    {
        await VerifyCurrentSessionAsync(requireOperator: true);
        return await device.PointRotatorAsync(az, el, Context.ConnectionAborted);
    }

    public async Task<RemoteRotatorPosition> StopRotator()
    {
        await VerifyCurrentSessionAsync(requireOperator: true);
        return await device.StopRotatorAsync(Context.ConnectionAborted);
    }
}
