# IC-705 / IC-905 P3: Microwave satellite frequency classification

## Problem

Before this change, `SatnogsDbTransmitter.IsHamFrequency()` recognized only **144–148 MHz** and **430–440 MHz**. `RadioLink.HasUplink` called it before enabling the satellite uplink path. Consequently, a transmitter with an otherwise valid 1296 MHz / 2400 MHz / 5760 MHz / 10 GHz uplink could appear to have no amateur uplink, even when connected CAT hardware could tune that frequency.

Similarly, `RadioLink.IsCrossBand` treated any two frequencies outside the original two bands as belonging to the same category, so a 1296/2400 MHz cross-band link was not recognized.

## Implementation

An explicit `AmateurRadioBand` classifies the **frequency domain** (not transceiver hardware capabilities):

| Band | Software recognition range |
| --- | --- |
| 6 m | 50–54 MHz |
| 2 m | 144–148 MHz |
| 70 cm | 430–440 MHz |
| 23 cm | 1240–1300 MHz |
| 13 cm | 2300–2450 MHz |
| 5 cm | 5650–5925 MHz |
| 3 cm | 10.0–10.5 GHz |

`HasUplink` uses the same predicate via `IsHamFrequency`; `IsCrossBand` now compares distinct recognized bands instead of comparing two VHF/UHF booleans. This retains legacy 2 m/70 cm behavior and supports 23/13/5/3 cm links. Frequencies are `double` Hz in `RadioLink` and converted to integral `long` by CAT, preserving 10 GHz precision at 1 Hz resolution.

## Important: no added RF restrictions

**This is a user-interface/satellite-routing classifier, not legal permission to transmit.** The chosen ranges describe commonly recognized amateur bands; national allocations, licence privileges, primary/secondary status, amateur-satellite direction and attached radio hardware remain the operator's responsibility. It must not become an additional CAT/PTT frequency lock. In particular, region-specific frequency-plan guidance is not sufficient justification for silently disabling operator-requested CAT control.

Authoritative references:

- IARU Region 3, *Band Plan, R3-004 Rev. 2*, 6 November 2024: https://www.iaru-r3.org/wp-content/uploads/2025/01/R3-004-Band-Plans-IARU-Region-3.pdf
- IARU Global Band Plans: https://www.iaru.org/on-the-air/band-plans/
- FCC 47 CFR 97.301 (US allocation example, not applicable to all stations): https://www.ecfr.gov/current/title-47/section-97.301

## Verification and scope

The automated tests exercise band identification, band edges, old 2 m / 70 cm regression and actual `RadioLink.HasUplink` / `IsCrossBand` behavior for 1296 ↔ 2400 and 5760 ↔ 10368 MHz.

This PR does **not** claim that a physical IC-705/905 can perform any particular cross-band simultaneous RX/TX combination or that its micro-wave hardware/antenna modules are installed. It only prevents frequency-range heuristics from suppressing supported CAT/satellite workflows.

## P3: precision Doppler and PTT staging

- `RadioLink` uses the exact vacuum speed of light **299,792.458 km/s**, rather than approximating 300,000 km/s. At 10.3681 GHz and 7.5 km/s radial velocity, the old constant creates approximately 179 Hz Doppler error.
- `CatSettings.MicrowaveTuningStep = 1` Hz by default at **1 GHz and above**. The original `TuningStep` (default 10 Hz) is retained below 1 GHz. Operators can set another microwave step to match radio hardware. Calculations use double Hz for Doppler and signed long Hz for actual CAT writes.
- `FrequencyWidget` quantizes the *final CAT frequency after transverter LO offset* with the same rule used by `CatControlEngine` and `AfcCatTimeline`; previously applying a long truncation early would drop the fractional-Hz Doppler component and the AFC timeline could track a different 10-Hz grid.
- For PTT ON, `CatControlEngine` now stages **supported pending TX frequency and mode writes first**. In one-VFO simplex, the RX dial is tuned to the intended TX value before keying. In Split / TxOnly / Duplex the dedicated TX write command is used if advertised. A rejected/timeout CAT write holds that particular pending PTT ON for a later retry, rather than transmitting on a stale frequency. A CAT field that the backend does *not* advertise remains operator-managed: no new blanket PTT restriction is introduced.
- **PTT OFF is not gated** by frequency/mode/CTCSS readiness. Existing application PTT ownership tracking, OFF retry and independent hardware PTT readback are preserved.
- Separate RX/TX CAT endpoints already work through `CatControl` (different host/port); a single IC-705/IC-905 does not become full-duplex merely because its frequency bands differ. A single radio uses its actual SkyCAT capability profile to decide Split support; full-duplex is only used when advertised.
- CAT loop update speed uses `CatSettings.MicrowaveCycleDelayMs` (default **100 ms**) when a requested CAT RX or TX frequency is ≥ 1 GHz; below 1 GHz it preserves `CatSettings.Delay` (default 500 ms). The faster microwave interval cannot override an even faster operator-selected normal cycle. Actual serialized CAT transaction duration, device dial/readback latency and radio firmware behavior still require physical verification.

**Evidence**: synthetic 10.368 GHz / 5.760 GHz Doppler and RIT/XIT, RX/TX transverter routing, 64-bit CAT quantization, state-condition tests for staging PTT ON and unconditional OFF, separate physical endpoints and cross-band Split capability. Real radio CI-V ACK and RF/PTT state transitions have **not** been verified by hardware in this PR; a command ACK must not be presented as evidence of RF emission or independent PTT readback.
