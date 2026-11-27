using org.orekit.frames;
using org.orekit.time;
using org.orekit.utils;
using SkyRoof.Orbit.Contracts;

namespace SkyRoof.Orbit.Orekit;

public sealed partial class OrekitCelestialTrack
{
    private const double C = RadioLinkMath.SpeedOfLightMetresPerSecond;
    private sealed record Leg(AbsoluteDate OtherDate, double Delay, double Ratio);

    public EmePathPrediction PredictEmePath(GroundStationSpec local, GroundStationSpec peer, DateTimeOffset utc)
    {
        var date = ToAbsoluteDate(utc);
        // All positions/velocities share the solar-system barycentric inertial frame.
        var inertial = FramesFactory.getICRF();
        var localSite = Site(local);
        var peerSite = Site(peer);
        PVCoordinates Moon(AbsoluteDate t) => body.getPVCoordinates(t, inertial);
        PVCoordinates Local(AbsoluteDate t) => localSite.getPVCoordinates(t, inertial);
        PVCoordinates Peer(AbsoluteDate t) => peerSite.getPVCoordinates(t, inertial);

        // Receive: local reception fixes both retarded emission epochs.
        var receiveMoon = Backward(date, Local(date), Moon);
        var receivePeer = Backward(receiveMoon.OtherDate, Moon(receiveMoon.OtherDate), Peer);

        // Transmit: local emission fixes both advanced reception epochs.
        var sendMoon = Forward(date, Local(date), Moon);
        var sendPeer = Forward(sendMoon.OtherDate, Moon(sendMoon.OtherDate), Peer);
        return new(receiveMoon.Ratio * receivePeer.Ratio, sendMoon.Ratio * sendPeer.Ratio,
            receiveMoon.Delay + receivePeer.Delay, sendMoon.Delay + sendPeer.Delay);
    }

    private static Leg Backward(AbsoluteDate receiveDate, PVCoordinates receiver,
        Func<AbsoluteDate, PVCoordinates> transmitter)
    {
        double delay = 0;
        for (int iteration = 0; iteration < 8; iteration++)
        {
            var source = transmitter(receiveDate.shiftedBy(-delay));
            delay = receiver.getPosition().subtract(source.getPosition()).getNorm() / C;
        }
        var emitted = receiveDate.shiftedBy(-delay);
        return new(emitted, delay, Ratio(transmitter(emitted), receiver));
    }

    private static Leg Forward(AbsoluteDate transmitDate, PVCoordinates transmitter,
        Func<AbsoluteDate, PVCoordinates> receiver)
    {
        double delay = 0;
        for (int iteration = 0; iteration < 8; iteration++)
        {
            var target = receiver(transmitDate.shiftedBy(delay));
            delay = target.getPosition().subtract(transmitter.getPosition()).getNorm() / C;
        }
        var arrived = transmitDate.shiftedBy(delay);
        return new(arrived, delay, Ratio(transmitter, receiver(arrived)));
    }

    private static double Ratio(PVCoordinates source, PVCoordinates target)
    {
        var direction = target.getPosition().subtract(source.getPosition()).normalize();
        return (C - direction.dotProduct(target.getVelocity())) /
            (C - direction.dotProduct(source.getVelocity()));
    }
}
