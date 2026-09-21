# WISK Documentation

- [WISK 3.0 architecture](WISK-3.0-Architecture.md) is the public design
  authority for repository boundaries, runtime layers, safety, and verification.
- The architecture document also defines the `Build-Wisk.ps1` profile/runtime
  matrix and the canonical `artifacts/` output boundary.
- `requirements/` at the repository root is intentionally local-only and is
  ignored by Git. The local WISK 3.0 requirements manual is the product
  behavior authority during development.
- `legacy/` contains the retained PowerShell implementation and historical
  assets; it is not the default WISK 3.0 execution path.

Historical Windows Initializer V2 documents are not copied into the public
WISK repository as current requirements. Their behavior was used as migration
input for the WISK 3.0 manual.
