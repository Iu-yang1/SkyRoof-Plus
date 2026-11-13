# Orekit / IKVM Unified Orbit & Celestial Engine

SkyRoof Plus now uses **Orekit 13.1.9, converted to .NET using IKVM**, as its only production orbit propagator. Satellite TLE/CCSDS OMM inputs still use Orekit's proper SGP4/SDP4 implementations. The former SGP.NET propagation fallback is disabled; Moon, Sun and Venus are likewise observed through Orekit's celestial-body and WGS-84 topocentric APIs.

## Automatic Orekit data

Open **Settings → Orbit / Ephemeris Sources → Orekit ephemeris** to configure one unified place for the automatic download switch, solar-system target visibility, local data directory, IERS EOP download sources and the pinned DE430 download URL. Select this section to expose **Update Orekit Data**, which refreshes EOP and checks/downloads DE430 on demand. This control replaces the former Tools-menu EOP-only operation.

On startup SkyRoof uses the locally installed UTC/TAI leap-second file and any cached Orekit data immediately, then restores the saved docking windows **without waiting for a network download**. The Orekit data directory is either **Orekit Data Directory** or SkyRoof's default user-data `orekit-data` folder. Once the main window is visible, the application checks for missing or stale external files in the background. With **Auto-download Orekit Data** enabled (default):

- IERS `finals2000A.all` is retrieved from USNO/IERS and refreshed when the cached copy is more than seven days old. A validated file is moved into place atomically and a SHA-256 source manifest is written.
- The Orekit-native JPL DE430 binary `lnxp1990.430` is downloaded once from the eSpace-EPFL orekit-data mirror and checked against the pinned upstream Git blob hash before it replaces any file. This covers the contemporary Moon/Sun/Venus data, unlike the old NAIF `.bsp` path.
- Existing files remain usable offline if a refresh cannot connect. An initialization error is shown rather than silently switching to an alternative orbit algorithm.

The ephemeris cache is not the same as a NAIF SPK/BSP kernel. Existing custom `.bsp` files must be replaced with an Orekit-compatible DE binary data file if the custom path is to be used for solar-system tracking.

**Startup behavior:** When a local satellite database exists, its UI and docking panels load first, and the initial all-satellite pass recalculation runs after the first visible frame. A truly clean install still needs its first SatNOGS satellite-database download before populated panels can be restored. Weekly satellite-list refresh remains available but starts after the window is visible. If DE430 was previously absent and is downloaded in the background, Moon/Sun/Venus can be added on the current launch if the Orekit provider recognizes the file; otherwise restart SkyRoof. Logs record the time needed to restore the initial docking layout.

**Important:** Orekit caches global data/time-scale state. Restart SkyRoof after changing the data directory, solar-system target visibility or refreshing EOP to adopt the new data. **Orbit Data Quality** reports coverage status rather than a formal pointing-accuracy guarantee.

## CAT/rotator and Doppler

The application-facing orbit contract reports azimuth clockwise from north, geometric elevation, range in metres and range rate in metres per second (positive when receding). A one-way downlink first-order Doppler shift is `-f0 * rangeRate / c`. The legacy WinForms observation DTOs are currently retained as interoperability adapters, not as alternate propagation engines; these types will be removed in a separate compatibility refactor.

For the Moon, Sun and Venus, Orekit's `CelestialBodyFactory` supplies the ephemeris state and `TopocentricFrame` calculates the site-relative observation. Earth orientation data are provided by IERS, and the celestial-body ephemeris by the DE430 binary.

## Validation status

Source-level integration and automated smoke checks are distinct from a certified hardware installation. A clean Windows x64 compile/test, installed-runtime verification, independent Moon ephemeris comparison, and live CAT/rotator verification must be recorded before a production release. The old JPL SPK decoder has been removed. SGP.NET type adapters remain temporarily for pass and legacy observation DTOs but are not used for numerical propagation; they require a subsequent compatibility refactor.

See [implementation architecture](../architecture/orekit-ikvm-orbit-engine-v1.zh-CN.md) and GitHub pull request #195 for evidence and remaining work.

## Responsive satellite selection and manual data updates

Selecting a satellite no longer evaluates a full day of passes in the WinForms ComboBox callback. The active satellite's next pass is computed on a prioritized background worker after a short selection debounce; the 2-minute cache is keyed by the orbit-track instance so new TLEs do not reuse a previous orbit. Late completions are discarded after a different satellite is selected and cannot overwrite an explicitly selected pass. Group and frequency-range prediction results are computed from detached snapshots, published on the UI thread, and canceled/replaced when the operator changes group, station, or orbit data. The current CAT, Doppler and rotator controls still consume the published observations and are not disabled.

The group list's one-second refresh only checks completed next-pass cache entries rather than performing one-day Orekit propagation synchronously. Orbit details and photo-cache decode run on workers. The manual **Update Orekit Data** command likewise performs network access, IERS/DE430 validation and file I/O outside the WinForms synchronization context; the button indicates that an update is active, and closing the dialog cancels the request. An already validated DE430 file can reuse its length/modified-time verification record within the same process. A changed file is rehashed.

For performance validation, use repeated satellite/group switches and repeated manual star-ephemeris refreshes while observing that the application continues repainting and handling controls. A successful Windows CI build does not establish actual UI latency on a specific Windows PC.
