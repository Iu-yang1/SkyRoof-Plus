using Grpc.Core;
using SkyRoof.Remote.Foundation.Transmission;
using SkyRoof.Remote.Ipc;

public sealed class SimulatedDeviceService : SimulatedDevice.SimulatedDeviceBase
{
    private readonly object _gate = new();
    private readonly TransmitCoordinator _tx = new();
    private long _frequency = 435_650_000;
    private string _mode = "USB";
    private string _input = "LAN";
    private ulong _revision;

    public override Task<SimDeviceReply> Execute(SimDeviceRequest request, ServerCallContext context)
    {
        lock (_gate)
        {
            string result;
            var config = new RadioTxConfiguration("ic9700-sim", _mode, _input, _frequency);
            switch (request.Kind)
            {
                case SimCommandKind.SimTune:
                    if (request.FrequencyHz <= 0) throw new RpcException(new Status(StatusCode.InvalidArgument, "frequency_hz"));
                    _frequency = request.FrequencyHz;
                    result = "tuned";
                    break;
                case SimCommandKind.SimManualPttOn:
                    config = config with {
                        FrequencyHz = request.FrequencyHz > 0 ? request.FrequencyHz : _frequency,
                        Mode = request.RadioMode.Length == 0 ? _mode : request.RadioMode,
                        ModulationInput = request.RadioInput.Length == 0 ? _input : request.RadioInput
                    };
                    var began = _tx.BeginManual(request.CommandId, request.SessionId, config);
                    _frequency = config.FrequencyHz;
                    _mode = config.Mode;
                    _input = config.ModulationInput;
                    result = began.Action.ToString();
                    break;
                case SimCommandKind.SimManualPttOff:
                    result = _tx.ReleaseManual(request.CommandId, request.SessionId).Action.ToString();
                    break;
                case SimCommandKind.SimDigitalTxOn:
                    result = _tx.BeginDigital(request.CommandId, config).Action.ToString();
                    break;
                case SimCommandKind.SimDigitalTxDone:
                    result = _tx.CompleteDigital(request.CommandId).Action.ToString();
                    break;
                case SimCommandKind.SimStopAll:
                    result = _tx.StopAll().Action.ToString();
                    break;
                default:
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "kind"));
            }
            _revision++;
            return Task.FromResult(Snapshot(result));
        }
    }

    public override Task<SimDeviceReply> Readback(SimReadbackRequest request, ServerCallContext context)
    {
        lock (_gate) return Task.FromResult(Snapshot("readback"));
    }

    private SimDeviceReply Snapshot(string result)
    {
        var active = _tx.Current;
        return new SimDeviceReply
        {
            ActualFrequencyHz = _frequency,
            ActualMode = _mode,
            Transmitting = active is not null,
            ActiveTransactionId = active?.TransactionId ?? "",
            ActiveSessionId = active?.OwnerSessionId ?? "",
            Revision = _revision,
            TxEpoch = (ulong)(active?.Epoch ?? 0),
            Result = result,
            DigitalAutomationPaused = _tx.DigitalAutomationPaused,
            DeviceProcessId = Environment.ProcessId
        };
    }
}
