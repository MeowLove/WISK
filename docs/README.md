# WISK Documentation

- [WISK 3.0 architecture](WISK-3.0-Architecture.md) is the public design
  authority for repository boundaries, runtime layers, safety, and verification.
- The architecture document also defines the `Build-Wisk.ps1` profile/runtime
  matrix and the canonical `artifacts/` output boundary.
- `requirements/` at the repository root is intentionally local-only and is
  ignored by Git. The local WISK 3.0 requirements manual is the product
  behavior authority during development.
- `/legacy/` remains ignored and is not part of the public WISK 3.0 source or
  release archives.
- WISK 3.0 documentation describes the current product contract; superseded
  implementation notes are not runtime inputs.
