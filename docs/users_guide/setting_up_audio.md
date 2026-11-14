# Setting Up Audio

Click on **Tools / Settings** in the main menu to open the [Settings window](settings_window.md):

![Audio Settings](../images/audio_settings.png)

- **Speaker Audio Device** - select the audio device that will be used to output the audio
    received with SDR;
- **FM Squelch** enable or disable squelch in the FM mode.

## Recording decoded IF → AF audio

The **Recorder Audio Source** property and **Recorder > Record ▼ > Audio Source**
menu now include **IF-AF Demodulated**. This uses the live receiver's
**post-demodulation, post-leveler 48-kHz mono PCM** directly; it does not
record RS-BA1 speaker-loopback, radio USB IF, or the entire virtual cable
playback mix. Start the 12-kHz IF → AF receiver first, choose the source,
then **Record Audio**, **Stop** and **Save .wav** in the Recorder panel.

Windows virtual cable endpoints have opposite names by direction:
**CABLE Input** is the Windows *playback/render* end to which SkyRoof may
play decoded AF; **CABLE Output** is the corresponding *recording/capture*
end from which another program reads it. The existing
**Recorder Source = RS-BA1 Loopback** uses the configured
**Recorder RS-BA1 Playback Device**, regardless of what
**Recorder USB AF Capture Device** is set to.

**Auto** recording now prioritizes an active SDR, then a running IF→AF
demodulator, then RS-BA1 playback. You can select a source explicitly.

The output to the soundcard can be toggled by clicking on the **Soundcard** label
on the status bar. A drop-down list next to **Soundcard** allows switching between the audio devices:

![Audio Settings](../images/audio_labels.png)
