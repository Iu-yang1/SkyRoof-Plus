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

## Actual GitHub Actions evidence — 2026-11-17

**This test has discovered a production decoder gap, not passed the full gate.**
Do not label UNNE-1B real-RF reception as verified from the synthetic tests.

- [SkyRoof actual IQ attempt #44030786752](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/44030786752):
  3 verified genuine IQ fixtures; source SHA-256 values printed. For each
  fixture 36 combinations of 2 tone half-deviations, 3 residual centre
  offsets, default/disabled shape gate and 3 input amplitude gains were
  evaluated. The production default gate recovered **zero bursts** for all
  three. Disabling the shape gate recovered only 1-2 short bursts from the
  fading type-14 recording, at most **271 soft bits**, with **zero complete
  CRC-valid frames**. Type-1 power and CODEC2 voice recovered **zero bursts**
  even with RMS normalised to 0.1/0.3.
- [Pinned HADESx reference job #44031323621](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/44031323621):
  the **same pinned captures and Git blob IDs** decoded in the independent
  HADESx software to **CRC-OK type-1 clock 192304**, **CRC-OK type-14 clock
  192124**, and **six consecutive type-15 CODEC2 subframes 0..5**. This
  independently proves the IQ payloads and expected answers.
- The observed differences are **upstream of** `Unne1bDeframer` (the
  application receives insufficient soft symbols). Increasing CRC repair
  budgets or changing the GENESIS packet-length table would not address
  this failure. Pinned-source test shows that the present SkyTlm FSK
  burst acquisition/segmentation at 200 Bd requires further debugging.
- The real IQ test is intentionally **red** until the actual SkyRoof pipeline
  meets the same verified packet expectations. Keep this PR as a regression
  investigation, not a software release or claim of fixed on-air decoding.

Next focused work: measure SkyTlm's detected burst onset and end relative to
the real 128-bit alternating preamble, inspect its 200-Bd timing acquisition,
minimum burst length, gating and pre-roll; validate any fix against all three
recordings and preserve AO-123's already working CCSDS/IF route.
