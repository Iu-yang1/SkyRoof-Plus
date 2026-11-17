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


## 2026-11-17 follow-up: UNNE-specific receive gate and stronger voice acceptance

The production `TelemetryDecocder` now constructs the UNNE-1B
`StreamingPipeline` with `GateByShape = false`, while preserving the
previous default options for every other satellite. In the initial real-IQ
matrix, the default gate rejected all three captures; the gate-off
configuration recovered some type-14 soft symbols (up to 271), still fewer
than a complete type-14 packet. This is a targeted partial receive-path
correction, **not** proof of successful on-air or IQ-to-CRC decoding.

The acceptance test now requires all six published CODEC2 subframe numbers
0–5 in a single tuning/demodulation attempt. Type-1 and type-14 still
require a matching satellite clock and a CRC-verified frame.

Until this stricter recorded-IQ gate passes, keep this PR in draft, do not
merge as a proven UNNE-1B receiver fix and do not publish a release claiming
that the receiver has passed real-IQ end-to-end validation.

## Source-level SkyTlm fork integration (2026-11-17)

The real-IQ Action now checks out a fixed commit of
[Iu-yang1/VE3NEA.SkySignals-Plus](https://github.com/Iu-yang1/VE3NEA.SkySignals-Plus/pull/1),
builds `VE3NEA.SkyTlm.dll` and `VE3NEA.Dsp.dll` from source, then builds and
tests SkyRoof against those assemblies. A normal release currently still
uses the old committed vendor DLL; **the fork changes have not been shipped**.

Fixes in the fork include a strict native UNNE-1B `IDeframer`, an injected
`DeframerOverride` used by all native baud/CFO/timing CRC trials, repaired
early-exit shape-gate logic, `CorrectedBits` reporting, a weak-FSK
`RecoverFadedTail` option and the AO-123 `Asrtu1Header` API compatibility.

[Action #44036464827](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/44036464827)
passed **22/22 SkyTlm protocol tests** and recovered four real CODEC2
subframes numbered 0–3 using the native UNNE decoder. The regression still
failed: 4–5 absent, no type-1 or type-14 verified frame.

The follow-up detector trace on the three excerpts showed type-1 peak Z
**308.6** (onset threshold approx. **6.0**) and type-14 peak Z **252.1**
(threshold **5.5**), both with over a hundred in-burst detector frames.
Thus `BurstDecoded=0` here is **not evidence of zero raw detection**.
`RecoverFadedTail` defers decoding for up to `MaxFrameBits / 200` seconds
(about 5.48 s), but the older fixture fed only about 1.2 s of trailing
silence and never invoked `StreamingPipeline.Flush()`, so pending decodes
were lost. The test now primes the receiver's noise-floor history and calls
`Flush()` explicitly; SkyRoof `TelemetryDecocder.Dispose()` also flushes
before freeing the native DSP.

The corrected real-IQ Action is
[here](https://github.com/Iu-yang1/SkyRoof-Plus/actions/workflows/unne1b-real-iq.yml).
Its completion, expected CRC-valid type1/type14 and six CODEC2 subframes,
not compilation or just burst detection, remains the acceptance criterion.


### Reproducible production and installer path

The fork is no longer consumed only by the real-IQ Action. Normal Compile
Check and Build and Release Actions independently check out its pinned
commit and execute scripts/stage-skytlm-fork.ps1 before compiling SkyRoof.
That script verifies the exact Git SHA and copies the newly built SkyTlm
and DSP DLLs into the vendor locations. The installed-payload smoke test
compares the installer-extracted SkyTlm SHA-256 to the exact fork build.
A local build without the staging step still uses the historical vendor copy.

The real-IQ workflow downloads the SkySignals Git-LFS off-air corpus and
runs the full SkyTlm test suite (FSK, GFSK, GMSK, BPSK, CCSDS, HADES and
USP) before three UNNE-1B IQ excerpts. The normal SkyRoof Compile Check
runs its own test suite and the unsigned installer smoke. Neither test
constitutes hardware-on-air validation of the operator's own radio.
