# Orekit / IKVM Orbit Engine (Experimental)

SkyRoof Plus supports **SGP.NET** (default) and a selectable **Orekit 13.1.9** backend converted to .NET with **IKVM 8.16.3**. Both backends propagate TLE/CCSDS OMM via SGP4/SDP4. Switching backend does not turn a TLE into a precision numerical orbit.

## Choose a backend

1. Open **Tools → Orbit / TLE Sources → Edit Source URLs...** to edit **Orbit Sources** settings.
2. Under **Orbit engine**, select **Orbit Propagation Backend**: `SgpNet` or `Orekit`.
3. Apply the settings. SkyRoof rebuilds satellite tracks and pass caches. Inspect **Active Orbit Backend** and **Orbit Data Quality** to confirm the effective backend.
4. If Orekit cannot initialize, the existing SGP.NET engine remains available, and the application displays the underlying error.

The included .NET plugin does **not** require a separate Java installation, but **all** IKVM runtime assemblies, native DLLs, the IKVM home image and its `ikvm.properties` must ship with SkyRoof. A developer build that lacks those assets is not equivalent to the tested installer.

## Time and Earth orientation data

Orekit requires a UTC–TAI history (for example `tai-utc.dat`). The installer includes a testable UTC history file, copied to the user data directory on first use. For improved Earth-fixed frame accuracy, also provide up-to-date IERS `finals2000A.all` EOP data.

Use **Tools → Orbit / TLE Sources → Update Orekit IERS EOP Data...** to download the latest available IERS file and save a SHA-256 manifest. Restart SkyRoof to reload the dataset. Alternatively, set **Orekit Data Directory** to a directory containing matching data. A running Orekit instance cannot safely swap global time-scale data contexts.

The **Orbit Data Quality** diagnostic differentiates missing EOP from available or out-of-range records. This is data-source status, **not** a claim of measured absolute pointing accuracy.

## Radio and rotator interoperability

SkyRoof keeps the existing satellite tracker API and CAT/Rotator paths. Orekit returns azimuth clockwise from true north, geometric elevation, range in metres, and **range rate positive when receding**. The compatibility layer converts these into the existing SGP.NET observation units (kilometres and kilometres/second). A one-way downlink's first-order Doppler sign is `-f0 * range_rate / c`.

Legacy SGP.NET remains available as a rollback choice. Solar-system targets still use SkyRoof's existing JPL/NAIF BSP engine; the Orekit option does not invent spacecraft attitude, polarization or EME data.

## Known validation boundaries

The automated suite covers Vallado SGP4/SDP4 reference vectors, OMM six-digit NORAD IDs, pass boundaries, and installed-package smoke tests. The full real-hardware CAT/rotator/EME path is not certified by software-only tests. The IKVM static-conversion warning related to `CharBuffer.rewind` is still under investigation; avoid claiming unsupported Java code paths are verified.

The development and evidence record is at [Orekit + IKVM architecture](../architecture/orekit-ikvm-orbit-engine-v1.zh-CN.md). Until the release gates pass, use a preview build rather than replacing a known-good production installation.
