# Telemetry IF/AF Reception and Offline Replay (v1.82)

**Language:** English | [简体中文](../zh-cn/fork-guide/telemetry-if-af.md)

SkyRoof Plus v1.82 extends the built-in [Telemetry panel](../users_guide/telemetry_panel.md) with a real 12-kHz intermediate-frequency (IF) receiver for the IC-9700 and a compatible path for already FM-demodulated AFSK audio. It preserves the original SDR IQ source. **Receiving telemetry does not require an IF→AF virtual-cable playback endpoint.** This is separate from the optional [IF→AF / WSJT-X voice/audio chain](if-af-test.md).

## 1. Choose the correct signal domain

| Telemetry RX Input in Tools → Settings → Telemetry | Actual input | Receiver behavior |
|---|---|---|
| `SdrIq` (default) | 48-kHz complex SDR baseband | Unchanged SDR telemetry path |
| `Shared12kIf` | Raw 12-kHz real IF at 48 kHz | Shares a running IF→AF source, or receives independently from USB IF / authenticated Direct LAN according to **IF→AF input source** settings; **VAC is not required** |
| `Capture12kIf` | Separate Windows USB capture endpoint with real IF | Independent 48-kHz USB capture; selects **12 kHz IF capture channel** |
| `CaptureAf` | Windows AF recording endpoint (e.g. VB-CABLE **CABLE Output**) | Already-demodulated AF input |
| `LoopbackAf` | Windows render endpoint carrying RS-BA1 or other AF playback | Captures the whole endpoint mix, not a dedicated program stream |
| `IfAfDemodulatedAf` | AF PCM from a running SkyRoof IF→AF demodulator | Uses internal AF directly, no VAC loopback; **IF→AF processing must be running** |

For real IF, the receiver forms an analytic complex signal with a Hilbert FIR and translates **12,000 Hz** to baseband using a continuous NCO. For **AFSK audio already recovered by FM discrimination**, v1.82 maps the AF to an FM-IQ representation expected by SkyTlm's AFSK demodulator, instead of discriminating analytic AF a second time. Select the transmitter's correct modulation, baud rate, framing, audio carrier and deviation in **Signal Details**. **AF tone center (Hz)** applies to the general analytic AF route; the AFSK branch instead uses the AFSK signal parameters.

A 9600-bit/s GMSK/FSK discriminator output is **not** Bell-202 AFSK audio. The new **FSK/GMSK AF waveform** property distinguishes **ToneAudio** (USB/SSB audio carrying the actual carrier tones, the unchanged default) from **FmDiscriminator** (audio already FM-discriminated by a receiver). For FM-discriminator FSK/GFSK/GMSK, SkyRoof reconstructs a continuous FM-IQ stream for SkyTlm; use **FM discriminator deviation (Hz) = 0** for the satellite's deviation or the baud/4 default, or supply the measured Hz. This affects only AF capture/loopback, not raw IF or SDR IQ. Verified 48-kHz reference WAVs: AISTECHSAT-3, 4800 Bd / 1200 Hz (5 verified AX100 frames); Suomi 100, 9600 Bd / 2400 Hz (2 verified frames); AO-27 AFSK (2 verified AX.25 frames). AO-73 DBPSK remains on **ToneAudio**, 1200 Bd AO-40 FEC at a 1700-Hz AF tone center (one RS-valid 256-byte frame). Recorded-waveform reconstruction tests do not prove live hardware AFC.

## 2. IC-9700 USB IF configuration

1. On the radio set **USB AF/IF Output = IF**, not AF. Select the radio's **USB Audio CODEC** Windows *capture* endpoint at **48 kHz**; disable Windows microphone effects/AGC where possible. Choose the actual IF-bearing left/right channel, not a sum of potentially inverted stereo channels.
2. In SkyRoof, open **Tools → Settings → Telemetry → Receive input** and choose `Capture12kIf` for a dedicated input, or `Shared12kIf` to reuse an already-running IF→AF raw capture. If IF→AF is stopped and USB is the configured IF source, `Shared12kIf` can open its own capture endpoint.
3. Select the desired satellite and **data transmitter** in Telemetry. Verify modulation, baud and framing in the signal-parameter dialog; the input selection alone does not identify the protocol.
4. Hover over the Telemetry **status line** for input samples, dropped PCM blocks, discontinuities, input RMS in dBFS, frame counters, last CFO and last SNR.

The IF receiver is a **receive-only** signal path: it does not route PCM to the transceiver's transmit audio playback device or take over CAT/PTT.

## 3. Authenticated IC-9700 Direct LAN IF

1. Configure authenticated **Direct LAN audio receive** and the IC-9700's separate **LAN AF/IF Output = IF**. The USB AF/IF setting does not configure LAN output.
2. In SkyRoof IF→AF settings select the **Authenticated LAN IF** input source; in Telemetry select `Shared12kIf`. The telemetry receiver can attach to the Direct LAN media owner without starting IF→AF/VAC playback.
3. Direct LAN PCM is reordered/resampled to **48 kHz**. Packet gaps, stream restarts and receive-queue drops are discontinuities: SkyRoof resets the Hilbert/NCO and rebuilds the stateful telemetry burst detector, modem and deframer instead of joining unrelated samples as one signal.
4. Check the Telemetry status tooltip for rising **discontinuities** or **dropped PCM blocks**. Validate the actual receive PCM before interpreting a lack of CRC-valid frames as an incorrect modulation.

**Limits:** The LAN source must deliver actual IF, not ordinary AF. Source reconfiguration, network continuity and application queue load all affect decoding. The decoder's live satellite mode still follows its selected satellite/pass horizon logic.

## 4. Recorder: `.if.wav` and `.iq.wav`

The [Recorder manual](../users_guide/recorder_panel.md) describes the common controls.

- **Raw IF**: **Recorder → Record ▼ → Audio Source → Raw 12-kHz IF**, then **Record Audio**. Save as `*.if.wav`: **48-kHz, mono, 32-bit float** samples, containing the original 12-kHz real IF, **not AF**.
- **Converted IQ**: **Recorder → Record ▼ → I/Q Source → 12-kHz IF → complex I/Q**, then **Record I/Q**. Save as `*.iq.wav`: **48-kHz, stereo, 32-bit float** (left=I, right=Q). This is translated narrowband complex IQ, **not wideband RF IQ**.

For Recorder IF acquisition, running IF→AF may supply shared raw USB/LAN IF; otherwise use the configured **Recorder 12-kHz IF Capture Device** (USB). Recorder's independent LAN acquisition should not be inferred solely from the Telemetry Direct LAN option.

**Offline telemetry decoding:**

1. Open Telemetry, select the matching satellite/transmitter and verify its demodulator parameters.
2. Recorder → **Load** a 48-kHz mono file named `*.if.wav`. SkyRoof detects this suffix and converts the entire raw IF recording via the **same 12-kHz IF→IQ converter used by live reception**. For a saved `*.iq.wav`, load the stereo IQ directly. An ordinary mono `*.wav` without the `.if.wav` suffix remains AF playback.
3. Press **Play Back**. The replay IQ is routed to Telemetry even when its selected live RX input is not `SdrIq`; historical playback bypasses the normal live horizon gate. Seeking resets modem/decoder state before new samples are delivered.
4. **Replay frames are not uploaded to SatNOGS.** Check the frame tree, diagnostics and KISS clients locally. Stop playback to return to live decoding.

## 5. Decoder diagnostics and KISS output

The Telemetry **status-line tooltip** reports input sample count, PCM-block drops, stream resets, IF/AF RMS (dBFS), total decoded frames, CRC pass/fail counts, reassembled inner packets and the most recently decoded CFO/SNR. These are receiver/decoder indicators, not measured BER, PER or calibrated RF sensitivity. A **CRC-valid** frame is required to mark a pass as carrying valid telemetry and to confirm manual demodulator overrides.

The optional **KISS-over-TCP server** (default port **8100**) multiplexes protocol layers using the **KISS virtual port** in the command byte:

| Virtual port | KISS command byte | Content |
|---|---|---|
| 0 | `0x00` | Outer decoded RF/transfer frame (CRC accepted or not explicitly failed) |
| 1 | `0x10` | Reassembled inner packets, such as AO-123/ASRTU-1 TM/KISS Space Packets; their KISS payload has no separate FCS |
| 2 | `0x20` | Outer frame with **explicitly failed** CRC |

The bytes of a frame are KISS-escaped; the port number **is not another TCP listening port**. A downstream decoder that reads only KISS virtual port 0 will not see port 1/2. Only eligible **outer** radio frames may be submitted to SatNOGS; inner packets, replay and unconfirmed manual overrides are not submitted. For AO-123, a failed CCSDS TM frame flushes partial KISS packet assembly to avoid concatenating payload across missing data. For UNNE-1B, the dedicated HADES type-C frame/CRC/descrambling path remains in place.

### UNNE-1B / UZ7HO SoundModem and official decoder DLL

The official AMSAT-EA [UNNE-1B transmission overview](https://www.amsat-ea.org/app/download/14192756/AMSAT%2BEA%2B-%2BTransmissions%2Boverview%2Bfor%2BUNNE-1B.pdf) specifies **binary FSK**, initial **200 bit/s**, **1125-Hz tone spacing** (nominal half-deviation 562.5 Hz), **mark = lower tone / bit 1**, and `BF35` sync. The downlink is **436.888 MHz**; higher rates require uplink commands and should not be inferred from the modem's menu options. Use **FSK / 200 baud / HADES/GENESIS** for ordinary 200-baud telemetry, adjusting the deviation only against real signal evidence.

An October 2026 official UZ7HO screen shows **FSK UNNE-1B 800bd selected** and a separate **200bd** option. Its 2500-Hz RX BPF, 2700-Hz TX BPF, 600-Hz post-detector LPF, 256/128 FIR taps, two additional RX pairs at 100-Hz steps and disabled bit recovery are **SoundModem audio-demodulator settings**, not SkyRoof IF→IQ configuration. Two additional decoder pairs correspond to five center-frequency hypotheses (center, ±100, ±200 Hz). TX delay/tail are not relevant to a receive-only telemetry session.

The supplied **`unne1b.dll`** is a 32-bit native **AMSAT-EA v1.14 (Bytes)** protocol/business decoder, **not** a SoundModem FSK demodulator. Its exports include `telemetry_packet_size`, `crc16`, `SelfSyncDeScrambler`, `procesar` and the `visualiza_*` telemetry functions. A 64-bit SkyRoof build cannot directly load this 32-bit DLL. The independent C# port keeps the documented per-type lengths, scrambled-byte CRC-CCITT-FALSE and the self-synchronising LFSR. Source updates after v1.82 correct engineering presentation details: the onboard clock/counters and packed 16-bit sensor words are **little-endian**, panel power is **2 mW per LSB**, and several 12-bit sensors require native packed-field extraction. These changes improve decoded values; they do not by themselves improve FSK symbol timing or link BER.

Reference: [AMSAT-EA native decoder source](https://github.com/AMSAT-EA/HADES-R/tree/main/byte_version) · [UZ7HO modem documentation](https://uz7.ho.ua/packetradio.htm).

## 6. Verification and troubleshooting

The v1.82 Windows release workflow built the application/installer and passed **1,632 xUnit tests** plus its pinned aislop gate. Tests include IF→IQ tone/stream consistency, AFSK FM-IQ audio reconstruction and KISS port framing. **No real IC-9700 USB/LAN capture, UNNE-1B 200 Bd FSK or AO-123 9600 Bd BPSK BER/PER measurements were performed as part of that release.**

If decoding fails, check the signal domain first (USB/LAN **IF vs AF**), then sample rate/channel, input RMS and discontinuities, transmitter selection, modulation/baud/framing, and finally CRC results. A recovered network connection or green audio indicator alone does not prove a received satellite frame decoded successfully.

See also: [Telemetry setup](../users_guide/setting_up_telemetry_decoding.md) · [Telemetry panel](../users_guide/telemetry_panel.md) · [Recorder](../users_guide/recorder_panel.md) · [IF→AF for WSJT-X](if-af-test.md) · [v1.82 Release](https://github.com/Iu-yang1/SkyRoof-Plus/releases/tag/v.1.82).

## 7. UNNE-1B automatic selection, receiver performance, BIN+LOG (development, 2026-11-18)

SatNOGS currently mislabels the UNNE-1B (NORAD 101101) 436.888 MHz 200-baud telemetry transmitter as BPSK. On this **exact satellite / frequency / 200-baud transmitter only**, with no operator override, the decoder chooses **FSK / HADES / 200 baud / 820 Hz peak tone deviation**. Independent genuine UNNE-1B off-air IQ fixtures recovered CRC-valid type-1/type-14 and all six sequential CODEC2 subframes with an 820 Hz configuration, while 562.5 Hz did not recover the type-1/14 frames. The operator can override any of these fields. Selecting the same-frequency FM repeater leaves CAT/radio mode unchanged but now also routes the corresponding FSK telemetry transmitter into the telemetry panel. CW, other satellites and the existing AOS/LOS gate are unaffected.

With no active decoder, telemetry capture continues to monitor ingress count and RMS but skips unused 48-kHz Hilbert/NCO work and IQ allocations. The 12-kHz real IF converter now skips the zero-valued Hilbert taps and uses a continuous complex oscillator instead of trigonometric calls on every sample. For the validated 820 Hz case, the two-tone signal-shape gate avoids expensive rejected-burst demodulations. The WinForms panel avoids redundant 1-Hz status text/tooltip updates; the hover diagnostics include **DSP work / ingress realtime (%)** (greater than 100% means processing is slower than acquisition).

Enable **Telemetry Settings → Save Decoded BIN + LOG** to write each decoded frame as a matching `.bin` (post-deframing/desedambling `Frame.Bytes`, with protocol FCS removed where applicable) and UTF-8 `.log` (UTC, source, length, CRC/FEC, corrections, CFO, SNR, HEX) under `TelemetryDecodes/Binary/<NORAD>/<UTC day>/`. These are *decoded payload bytes*, not IQ nor an exact on-air codeword; CRC-failed frames retain a FAIL marker and are still archived for diagnosis. The existing Save to File text archive is unchanged. A dedicated SatNOGS row shows disabled/misconfigured/pending/success/failed and recent HTTP result; replay and unconfirmed user override suppress upload.

**Limitation:** A passed public genuine-IQ regression does not validate the user's particular IC-9700 recording, packet error rate, Windows docked FPS or hardware end-to-end setup.


## Current v1.86 replacement build: SAVE/STOP in telemetry panel

Use **SAVE** next to the satellite telemetry signal-parameter gear to choose an archive destination and start recording. Press **STOP** to finish recording; outstanding BIN/LOG writes drain asynchronously. The **BIN+LOG** status is displayed separately from the **SatNOGS upload** status (no hover required). Decoded `.bin` and corresponding UTF-8 `.log` files are stored under `<selected directory>/<NORAD>/<UTC date>/`, with the readable `frames_YYYYMMDD.txt` alongside the selected directory. The two legacy archive configuration switches are hidden but their serialized fields remain compatible. Recording does not enable SatNOGS uploading.

UNNE-1B's FM repeater and BPSK-labelled telemetry row share the same 436.888-MHz downlink. In both selections the telemetry panel can run the 2-FSK/HADES Type-C/200-Bd/820-Hz decoder. The FM row keeps CAT/radio FM mode; it borrows its digital sibling or falls back to the confirmed protocol parameters if that database row is absent. An omitted SatNOGS baud value no longer blocks the digital FSK auto-resolution. Other satellites and AOS/LOS remain unchanged.
