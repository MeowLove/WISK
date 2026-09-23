# WISK Documentation

- [WISK 3.0 architecture](WISK-3.0-Architecture.md) is the public design
  authority for repository boundaries, runtime layers, safety, and verification.
- [WISK 3.0.0 release testing guide](Preview-Testing.md) describes package
  selection, integrity checks, safe testing, and feedback reporting for the
  rapid 3.0 iteration cycle.
- The architecture document also defines the `Build-Wisk.ps1` profile/runtime
  matrix and the canonical `artifacts/` output boundary.
- `requirements/` at the repository root is intentionally local-only and is
  ignored by Git. The local WISK 3.0 requirements manual is the product
  behavior authority during development.
- WISK 3.0 documentation describes the current product contract; superseded
  implementation notes and compatibility paths are not runtime inputs.
