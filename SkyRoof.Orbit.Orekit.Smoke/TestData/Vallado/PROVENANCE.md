# Vallado SGP4/SDP4 verification set

These are verification inputs and outputs for Vallado et al., *Revisiting Spacetrack Report #3*, AIAA-2006-6753 (2006), made available for public verification use with attribution.

- `SGP4-VER.TLE`: 33 standard verification element sets, unmodified from CelesTrak Vallado's public `cpp.zip` package.
- `tcppver.out`: generated using Vallado's unmodified C++ propagator in AFSPC mode with WGS-72 constants, cross-checked against independently distributed MATLAB `tmatverDec2015.out`.
- Upstream material and provenance: https://celestrak.org/software/vallado-sw.php and https://github.com/pigfox/sgp4-go/blob/main/testdata/README.md
- Source reference: https://celestrak.org/publications/AIAA/2006-6753/

Upstream reported file hashes (SHA-256):
- SGP4-VER.TLE: d246d1d9d768ace445a38a965713fa9ba52d80fd8a41a0502ff83d7acffe2881
- tcppver.out: 4642deff9709d50ec56f805a3b0061952207a04253007a4be487c997a22b9d6b

This dataset is for test and verification only. It is not included in the SkyRoof installer. Vallado's verification data is not covered by the Go application's MIT license; consult the cited CelesTrak terms.
