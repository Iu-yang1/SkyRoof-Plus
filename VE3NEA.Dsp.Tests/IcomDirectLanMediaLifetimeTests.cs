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

    [Theory]
    [InlineData("SkyRoof.IcomDirectLanMeterSettings.Enabled", 1)]
    [InlineData("SkyRoof.IcomDirectLanMeterSettings.PollIntervalMs", 1)]
    [InlineData("SkyRoof.IcomLanTxAgcSettings.TargetRmsDbfs", 1)]
    [InlineData("SkyRoof.IcomDirectLanTransmitSettings.MicrophoneGain", 1)]
    [InlineData("SkyRoof.IcomDirectLanReceiveSettings.SpeakerVolume", 1)]
    [InlineData("SkyRoof.IcomDirectLanTransmitSettings.MicrophoneDeviceId", 2)]
    [InlineData("SkyRoof.IcomDirectLanReceiveSettings.SpeakerDeviceId", 2)]
    [InlineData("SkyRoof.IcomDirectLanTransmitSettings.CaptureBufferMs", 2)]
    [InlineData("SkyRoof.IcomDirectLanTransmitSettings.SampleRate", 3)]
    [InlineData("SkyRoof.IcomDirectLanTransmitSettings.JitterPrefillMs", 3)]
    [InlineData("SkyRoof.IcomDirectLanReceiveSettings.Codec", 3)]
    public void LocalSettingsDoNotCauseUnnecessaryRadioMediaRenegotiation(
      string field, int expected) =>
      ((int)IcomLanSettingsChangePolicy.Classify([field])).Should().Be(expected);

    [Fact]
    public void MostDisruptiveSettingWinsWhenApplyingSeveralAtOnce()
    {
      IcomLanSettingsChangePolicy.Classify([
        "SkyRoof.IcomDirectLanTransmitSettings.MicrophoneGain",
        "SkyRoof.IcomDirectLanTransmitSettings.MicrophoneDeviceId"
      ]).Should().Be(IcomLanSettingsAction.RestartWindowsEndpoints);

      IcomLanSettingsChangePolicy.Classify([
        "SkyRoof.IcomDirectLanReceiveSettings.SpeakerVolume",
        "SkyRoof.IcomDirectLanReceiveSettings.Enabled"
      ]).Should().Be(IcomLanSettingsAction.RenegotiateMedia);
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
      Task initialized = audio.EndpointInitialization;
      (await Task.WhenAny(initialized, Task.Delay(100))).Should().NotBe(initialized);
      audio.Status.Should().Be("Audio endpoints not started");
      oldDeviceClosed.SetResult();
      await initialized.WaitAsync(TimeSpan.FromSeconds(2));
      audio.Status.Should().Contain("disabled");
    }
  }
}
