using FluentAssertions;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class IcomDirectLanMediaLifetimeTests
  {
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void ClosingDirectLanSpectrumRetainsSessionOnlyWhenAudioIsRequested(
      bool rx, bool tx, bool expected)
    {
      var options = new IcomDirectLanSettings
      {
        Receive = new IcomDirectLanReceiveSettings { Enabled = rx },
        Transmit = new IcomDirectLanTransmitSettings { Enabled = tx }
      };
      IcomDirectLanMediaOwner.PreserveAudioOnScopeStop(
        IcomLanSpectrumSource.DirectLan, options).Should().Be(expected);
      IcomDirectLanMediaOwner.PreserveAudioOnScopeStop(
        IcomLanSpectrumSource.SkyCat, options).Should().BeFalse();
      IcomDirectLanMediaOwner.PreserveAudioOnScopeStop(
        IcomLanSpectrumSource.RsBa1, options).Should().BeFalse();
    }

    [Fact]
    public async Task SameRadioCannotStartOverlappingAuthenticatedMediaSessions()
    {
      using IDisposable first = await IcomDirectLanSessionSerialGate.EnterAsync(
        "192.0.2.1", 50001, CancellationToken.None);
      using var cancel = new CancellationTokenSource();
      Task<IDisposable> second = IcomDirectLanSessionSerialGate.EnterAsync(
        "192.0.2.1", 50001, cancel.Token);
      Task completed = await Task.WhenAny(second, Task.Delay(100));
      completed.Should().NotBe(second);

      using IDisposable otherRadio = await IcomDirectLanSessionSerialGate.EnterAsync(
        "192.0.2.2", 50001, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
      cancel.Cancel();
      Func<Task> finish = async () => { using IDisposable _ = await second; };
      await finish.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReleasingOldSessionAllowsNewAuthenticatedMediaSession()
    {
      IDisposable first = await IcomDirectLanSessionSerialGate.EnterAsync(
        "192.0.2.3", 50001, CancellationToken.None);
      Task<IDisposable> second = IcomDirectLanSessionSerialGate.EnterAsync(
        "192.0.2.3", 50001, CancellationToken.None);
      first.Dispose();
      using IDisposable lease = await second.WaitAsync(TimeSpan.FromSeconds(2));
      lease.Should().NotBeNull();
    }

    [Fact]
    public async Task AudioStartWaitsForPriorWasapiShutdownWithoutBlockingUi()
    {
      var oldDeviceClosed =
        new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      using var capture = new IcomLanSpectrumCapture("127.0.0.1", 50002);
      var options = new IcomDirectLanSettings
      {
        Receive = new IcomDirectLanReceiveSettings { Enabled = false },
        Transmit = new IcomDirectLanTransmitSettings { Enabled = false }
      };
      using var audio = new IcomDirectLanAudioDevices(
        options, capture, priorEndpointShutdown: oldDeviceClosed.Task);
      audio.Start();
      await Task.Delay(100);
      audio.Status.Should().Be("Audio endpoints not started");
      oldDeviceClosed.SetResult();
      await Task.Delay(200);
      audio.Status.Should().Contain("disabled");
    }
  }
}
