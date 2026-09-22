# WISK Documentation

- [WISK 3.0 architecture](WISK-3.0-Architecture.md) is the public design
  authority for repository boundaries, runtime layers, safety, and verification.
- The architecture document also defines the `Build-Wisk.ps1` profile/runtime
  matrix and the canonical `artifacts/` output boundary.
- `requirements/` at the repository root is intentionally local-only and is
  ignored by Git. The local WISK 3.0 requirements manual is the product
  behavior authority during development.
- The superseded V1 source archive is local-only under ignored `legacy/`; it is
  not part of the public WISK 3.0 source or release archives.

Historical Windows Initializer V2 documents are not copied into the public
WISK repository as current requirements. Their behavior was used as migration
input for the WISK 3.0 manual.
