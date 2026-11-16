# Recorder Panel

The Recorder panel records the received audio or I/Q signal and plays it back later.
Up to 30 minutes of data can be stored in the recording buffer. Buffer allocation grows with recorded samples instead of allocating all 30 minutes immediately, and WAV/MP3 exporters stream samples in small blocks.

![Recorder panel](../images/recorder_panel.png)

## Recording

Click **Record** to start recording audio. To choose what to record, click the **▼** button next to Record:

![Record menu](../images/recorder_record_menu.png)

- **Record Audio** — records the selected audio source as 48-kHz mono PCM,
  including optional raw 12-kHz IF;
- **Record I/Q** — records the selected complex IQ signal as 48-kHz stereo,
  from either SDR or translated 12-kHz IF.

The same menu provides separate **Audio Source** and **I/Q Source** choices.
Audio sources include **Auto** (SDR when running, otherwise IF→AF decoded
PCM if that service is active, otherwise RS-BA1 playback), **SkyRoof SDR**,
**Windows AF capture (USB / CABLE Output)**, **RS-BA1 playback loopback**,
**IF→AF decoded PCM (direct)** and **Raw 12-kHz IF**. I/Q sources are
**SkyRoof SDR I/Q** or **12-kHz IF → complex I/Q**.

Configure the Windows endpoints in **Tools → Settings → Audio**:

- **Recorder AF Capture Device (USB / VAC)**: choose the recording endpoint,
  for example `Microphone (USB Audio CODEC)` or `CABLE Output`. This source
  is demodulated **AF**, not real 12-kHz IF.
- **Recorder 12-kHz IF Capture Device**: choose a USB capture endpoint at
  **48 kHz**; set radio **USB AF/IF Output = IF** and select the correct
  left/right channel in IF→AF settings. Recorder shares a running IF→AF raw
  input if available, or captures this USB device independently.
- **IF→AF decoded PCM (direct)** requires the IF→AF demodulator to be
  running but does not need VAC loopback.
- **Recorder RS-BA1 Playback Device**: choose the Windows **render/playback**
  device RS-BA1 is actually using. It must not be the USB microphone input.
  Loopback records the entire Windows endpoint mix, not just one process.
- For a remote IC-9700 controlled via RS-BA1/SkyCAT without a local SDR,
  select **RS-BA1 playback loopback** and its endpoint for AF recording.
  Raw IF is a different signal domain; do not interpret ordinary AF as IF.

The Recorder audio source is independent of the **CW Console** source.
If recording starts but no valid PCM samples arrive after a few seconds,
Recorder displays a warning with the selected input. Setting a radio's
frequency or opening the Icom LAN spectrum alone does **not** provide
Recorder audio.

The button turns red and the status bar shows the recording type and elapsed time while recording is active.
Click **Record** again to stop.

![Recording in progress](../images/recorder_recording.png)

The waveform display shows the recorded signal growing from the left. The full 30-minute buffer width is
always shown during recording, so the waveform expands to fill it as the recording progresses.

## Event Markers

While recording is active, the panel automatically tracks changes in the selected satellite, transmitter,
and mode, as well as QSOs saved in the [QSO Entry](qso_entry_panel.md) panel. These events appear as
colored markers along the top edge of the waveform:

- **green circle** — satellite selection changed;
- **magenta triangle** — transmitter selection changed;
- **yellow triangle** — demodulation mode changed;
- **cyan square** — QSO saved.

Move the mouse cursor over a marker to see a tooltip with the details of that event.

The event time in the tooltip is shown in the time zone selected on the [Clock](clock.md) widget,
marked with **Z** for UTC or **LT** for local time.

## Playback

Click **Play Back** to play back the recording. The button turns green and a red vertical line
moves across the waveform to show the current position. The status bar shows the elapsed time,
total duration, and progress percentage.

![Playback in progress](../images/recorder_playback.png)

Click anywhere on the waveform during playback to seek to that position.
When playing IQ into the Telemetry decoder, seeking resets modem and
deframer state instead of joining nonadjacent recorded signal segments.
Playback frames are never uploaded to SatNOGS.

Click **Play Back** again to stop playback.

## Saving and Loading

Click **Save** to save the recording. To choose the file format, click the **▼** button next to Save:

![Save menu](../images/recorder_save_menu.png)

- **Save .mp3** — available for audio recordings only; saves a compressed MP3 file resampled to 16 kHz;
- **Save .wav** — saves a 48-kHz 32-bit float WAV. Audio is mono;
  I/Q is stereo (**left = I, right = Q**).
- **Raw 12-kHz IF** audio saves as `*.if.wav` (48-kHz mono float).
  This is real IF, **not decoded AF**, and cannot be exported as MP3.
- **12-kHz IF translated to complex IQ** saves as `*.iq.wav`
  (48-kHz stereo float). It is translated narrowband IQ, **not the
  SDR's original wideband RF IQ**.

Click **Load** to load a previously saved .wav or .mp3 file into the buffer
for playback. A 48-kHz mono file named `*.if.wav` is automatically
converted with the **same IF→IQ bridge** used for live telemetry, then
routed to the Telemetry decoder while playing. Saved stereo `*.iq.wav`
files are loaded as complex IQ directly. Other mono WAV files without
the `.if.wav` suffix remain ordinary audio. Select the matching
satellite/transmitter and signal parameters in Telemetry before replay;
historical playback does not require the satellite currently to be
above the horizon. See
[Telemetry IF/AF and offline replay](../fork-guide/telemetry-if-af.md).

The Save and Load dialogs open in the **Recordings** subfolder of the [data folder](data_folder.md) by default.

## Playback Routing

During playback, audio is sent to the same soundcard that is selected in [Settings](setting_up_audio.md) for SDR audio output.
Both audio and I/Q are also routed through the same output channel (VAC or UDP) that is configured
for the [Output Stream](setting_up_output_stream.md) function in the [Settings](setting_up_output_stream.md), so external applications can receive
the played-back signal just as they would a live signal.

## Waveform Display

The vertical gain slider on the right edge of the waveform adjusts the display amplitude in dB,
making quiet signals easier to see. It does not affect the recording or playback level.

