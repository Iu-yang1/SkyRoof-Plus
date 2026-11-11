using FluentAssertions;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class IcomNativeScopeControlTests
  {
    [Fact]
    public void DirectLanUsesAuthenticatedRadioAddressNotHardcodedA2()
    {
      byte[] command = IcomDirectScopeControlCodec.Encode(
        0xAC, IcomScopeControlRequest.ForSelectedScope(1));
      command.Should().Equal(
        0xFE, 0xFE, 0xAC, 0xE0, 0x27, 0x12, 0x01, 0xFD);
    }

    [Fact]
    public void DirectLanModeAndSpanMatchIcomScopeWriteProtocol()
    {
      IcomDirectScopeControlCodec.Encode(
        0xA2, IcomScopeControlRequest.ForMode(0, IcomScopeMode.ScrollCenter))
        .Should().Equal(0xFE, 0xFE, 0xA2, 0xE0, 0x27, 0x14, 0x00, 0x02, 0xFD);
      IcomDirectScopeControlCodec.Encode(
        0xA2, IcomScopeControlRequest.ForSpan(1, 25_000))
        .Should().Equal(0xFE, 0xFE, 0xA2, 0xE0, 0x27, 0x15,
          0x01, 0x00, 0x50, 0x02, 0x00, 0x00, 0xFD);
    }

    [Fact]
    public void NativeReferenceAndSweepSpeedUseIcomPackedFields()
    {
      IcomDirectScopeControlCodec.Encode(
        0xA2, IcomScopeControlRequest.ForReferenceLevel(1, -5.5))
        .Should().Equal(0xFE, 0xFE, 0xA2, 0xE0, 0x27, 0x19,
          0x01, 0x05, 0x50, 0x01, 0xFD);
      IcomDirectScopeControlCodec.Encode(
        0xA2, IcomScopeControlRequest.ForSweepSpeed(
          1, IcomScopeSweepSpeed.Fast))
        .Should().Equal(0xFE, 0xFE, 0xA2, 0xE0, 0x27, 0x1A,
          0x01, 0x00, 0xFD);
    }

    [Fact]
    public void NativeScopeWritesUseSeparateRoutingFromPttAndDoppler()
    {
      int scopeWrites = 0;
      var controller = new IcomScopeController(
        () => false,
        directLanControlRequest: request =>
        {
          scopeWrites++;
          return request.Kind == IcomScopeControlKind.ReferenceLevel;
        });
      controller.RequestControl(
        IcomLanSpectrumSource.DirectLan,
        IcomScopeControlPath.Auto,
        IcomScopeControlRequest.ForReferenceLevel(0, 8))
        .Should().BeTrue();
      controller.EffectivePath.Should().Be(IcomScopeControlPath.DirectLan);
      scopeWrites.Should().Be(1);
      controller.RequestControl(
        IcomLanSpectrumSource.RsBa1,
        IcomScopeControlPath.Auto,
        IcomScopeControlRequest.ForReferenceLevel(0, 8))
        .Should().BeFalse();
      scopeWrites.Should().Be(1);
    }

    [Fact]
    public async Task NativeAudioReceiverFailureTriggersRecoveryEvenWhenScopeKeepsStreaming()
    {
      var rx = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var keepAlive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      Task<Task>? monitor = IcomLanDirectSession.MonitorAudioWorkers(
        true, false, rx.Task, keepAlive.Task, Task.CompletedTask);
      monitor.Should().NotBeNull();
      monitor!.IsCompleted.Should().BeFalse();
      rx.SetResult();
      (await monitor).Should().BeSameAs(rx.Task);
    }

    [Fact]
    public async Task ReceiveOnlyAudioNeverMonitorsUnusedTxWorker()
    {
      var rx = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var keepAlive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      Task<Task>? monitor = IcomLanDirectSession.MonitorAudioWorkers(
        true, false, rx.Task, keepAlive.Task, Task.CompletedTask);
      monitor.Should().NotBeNull();
      monitor!.IsCompleted.Should().BeFalse();
      keepAlive.SetResult();
      (await monitor).Should().BeSameAs(keepAlive.Task);
      IcomLanDirectSession.MonitorAudioWorkers(
        false, false, rx.Task, keepAlive.Task, Task.CompletedTask)
        .Should().BeNull();
    }

    [Fact]
    public void InvalidNativeScopeRequestNeverProducesCivPacket()
    {
      Action invalidEdge = () => IcomDirectScopeControlCodec.Encode(
        0xA2, IcomScopeControlRequest.ForEdge(0, 9));
      invalidEdge.Should().Throw<ArgumentException>();

      Action invalidReference = () => IcomDirectScopeControlCodec.Encode(
        0xA2, IcomScopeControlRequest.ForReferenceLevel(0, 40));
      invalidReference.Should().Throw<ArgumentException>();
    }
  }
}
