# UNNE-1B real over-the-air IQ acceptance corpus

This Action tests original N6RFM 2026-10-04 satellite IQ recordings against
SkyRoof's actual StreamingPipeline, BurstDecoded soft symbols and
Unne1bDeframer, then checks CRC and decoded type/clock or CODEC2 index.

Reference repository and immutable recording source:
https://github.com/N6RFM/HADESx-Decoder/tree/238386904c44a60657bac0039c2edb681446d2c0/examples/iq

The three fixtures are public captured complex float32 little-endian IQ at 50
ksps. They are NOT synthetic frames. Downloaded bytes must match the source
repository's pinned git blob object ID and exact size before test execution.
The test resamples to 48 ksps (SkyRoof receiver input rate) and applies an
explicit oscillator correction based on the published measured burst centre
offsets, emulating CAT/Doppler tuning performed before the decoder in SkyRoof.
Three candidate residual offsets and two half-tone deviations are measured.

Expected verified frames:
- pass_t211s_type01.iq: type 1 Power, sclock 192304
- pass_t032s_type14.iq: type 14 Time series, sclock 192124
- pass_t122s_voice.iq: type 15 CODEC2, subframe number 0 through 5

Each trial's burst count, recovered soft bit count, CFO, SNR, CRC and frame
headers is recorded to JSON and uploaded even on test failure. No fixture
matching only synthetic symbols can pass this gate.

This verifies real-recording IQ-to-FSK-to-CRC integration, NOT radio USB/LAN
hardware, raw 12-kHz real IF ingestion, or an independently measured link
BER/PER. The test is opt-in outside the dedicated workflow; absent the
fixture directory it does not fetch any IQ data in the ordinary unit suite.
