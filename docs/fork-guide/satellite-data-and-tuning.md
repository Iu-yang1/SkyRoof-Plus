# Satellite data, ephemerides, transmitters and frequency tuning

**Language:** English | [简体中文](../zh-cn/fork-guide/satellite-data-and-tuning.md)

## Multiple satellite/orbit sources

Open **Tools → Orbit / TLE Sources → Edit Source URLs** or **Orbit / Ephemeris Sources**. Highest to lowest **orbit-element** precedence:

1. **Explicit manual file import** — highest for 72 hours, then falls back automatically.
2. **CelesTrak GP OMM CSV/JSON** — highest automatic source; official amateur CSV by default, optional JSON and independently hosted mirrors.
3. **Configured custom orbit URLs/local files** — first entry wins; these persistent custom sources are **not** 72-hour manual file overrides.
4. **AutoTLE** automatic fallback.
5. **SatNOGS TLE** lowest automatic fallback.

The independent [CelesTrak contract workflow](https://github.com/Iu-yang1/SkyRoof-Plus/actions/runs/44053914187) confirmed 99 amateur GP records in each official CSV/JSON format, with identical NORAD IDs. New downloads use validated source failover, a two-hour GP cache, and compression; invalid responses do not replace existing valid data. SatNOGS satellite/transmitter metadata, JE9PEL frequencies, and SatYAML profiles are *not* competing orbit sources.

See [Chinese network and source mirror deployment guide](../engineering/orbit-source-mirrors.zh-CN.md).

### Lunar and solar-system tracking

The active Orekit engine uses UTC/TAI, IERS EOP, and the **Orekit-native JPL DE430** file. It supports verified HTTPS/local mirror locations and an upstream-pinned Git blob integrity check. A NAIF DE440/DE421 BSP/SPK file cannot directly replace `lnxp1990.430`. These data support Moon, Sun and Venus tracking, but do not alone establish EME links or model polarization rotations.

## Persistent base frequency and the tuning bar (PRs #2, #4–6 and #33)

The **Frequency Control** dockable panel distinguishes three independent concepts:

- **Database reference:** downlink/uplink frequency supplied by the selected transponder record.
- **Saved Base:** database frequency plus your persisted per-transmitter uplink/downlink Base correction. This is your chosen station reference, **without** current Doppler.
- **Actual radio tuning:** Base plus interactive tuning displacement and, when enabled, real-time Doppler correction.

Use **Edit Base...** to enter the persistent reference. **Reset to Database** discards your correction **after confirmation**. **Reset to Base** clears the temporary tuning displacement without wiping the saved Base. The no-Doppler readouts and tuning bar are distinct from the CAT-applied frequencies. Manual tuning can extend beyond a database passband's nominal end; always verify that the chosen RF signal is legal and within the selected equipment's range.

Separate per-satellite settings enable/disable downlink/uplink Doppler and manual corrections. Inverting linear transponders need the correct uplink/downlink direction and mode. When a transverter is configured, SkyRoof validates the usable IF-band mapping and suppresses unsupported CAT writes rather than sending out-of-band frequencies.

## User-created transmitter records (PRs #29, #31, #34)

From the **Satellite Transmitters** panel, open the context menu to create a **Local** transmitter; locally created entries can also be edited or deleted. Supply:

- Name/description;
- Optional **downlink MHz** and/or **uplink MHz** — at least one is required;
- Radio mode (including supported data-mode aliases such as `FM_D`, `USB_D` and `LSB_D`).

Records use stable local UUIDs, keeping their individual corrections, chosen modes and CTCSS settings associated during edits. Deletion is confirmed and intentionally removes the local definition. Local definitions are stored in **`custom-transmitters.json`** in the SkyRoof data folder, not in the upstream-downloaded SatNOGS transmitter list. Open **Help → Data Folder** to find the exact user folder (normally `%APPDATA%\Afreet\Products\SkyRoof`). Back up the file before manual editing.

**Limit:** the editor is for **locally created** transmitters. It does not silently overwrite externally downloaded records.

## Related original documentation

[Frequency Control](../users_guide/frequency_control.md) · [Doppler Tracking](../users_guide/doppler_tracking.md) · [Satellite Transmitters](../users_guide/satellite_transmitters_panel.md) · [Data Folder](../users_guide/data_folder.md).
