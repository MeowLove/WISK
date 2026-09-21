# WISK 3.0 Handoff

## Status

Today's structure and build-boundary work is complete. The working tree is
clean with the build changes and this handoff committed locally. Local `main`
is ahead of `origin/main`; no push was performed.

## Completed today

- Audited the repository layout and clarified public `docs/` versus ignored
  local `requirements/`.
- Added `Build-Wisk.ps1` as the canonical build entry point.
- Added `Test`, `Development`, and `Release` profiles.
- Added framework-dependent and self-contained publish modes for WPF App and
  CLI components.
- Redirected SDK output into `artifacts/build/`, test evidence into
  `artifacts/test-results/`, and packages into `artifacts/publish/`.
- Kept `Build-Dev.ps1` as a compatibility wrapper.
- Updated packaging scripts, CI, UI smoke tooling, README, public architecture
  docs, and the minimal project `AGENTS.md`.
- Fixed source-root contract tests so they work from centralized artifact
  output directories.
- Removed generated project-local `src/**/bin`, `src/**/obj`, `tests/**/bin`,
  and `tests/**/obj` directories from the WISK workspace.

## Verification evidence

- `Build-Dev.ps1 -Mode Test -Restore`: 270 passed, 0 failed.
- Catalog validator passed.
- PowerShell syntax checks and `git diff --check` passed.
- Framework-dependent WPF single file:
  `artifacts/publish/Release/FrameworkDependent/win-x64/WindowsInitializer.exe`
- Self-contained WPF and CLI release wrappers produced manifests successfully.
- No WPF startup, UAC, real registry Apply/Verify, software install, or system
  setting validation was run on the development host.

## Tomorrow's plan

1. Re-read this handoff, `AGENTS.md`, and the local requirements manual before
   changing code.
2. Review the three local commits against `origin/main`, confirm the public
   release scope, then push only after the release decision is confirmed.
3. Run the catalog/CI-equivalent checks from a clean checkout or CI runner and
   inspect the generated release manifests.
4. Perform WPF startup, UI smoke, UAC, and real Check/Apply/Verify validation in
   an isolated Windows test environment only; record failures as environment
   evidence rather than changing the development host.
5. Resume registry feature phase two: audit the previous six configurable
   features, then select the next complete desktop, Explorer, or privacy
   features with explicit conflicts, backup semantics, and localization tests.
6. Decide whether the first public download should be framework-dependent or
   self-contained using the measured sizes and the target tester audience.

## Important paths

- Canonical build: `Build-Wisk.ps1`
- Rules: `AGENTS.md`
- Public architecture: `docs/WISK-3.0-Architecture.md`
- Local requirements: `requirements/WISK_Requirements_v3.0.0.md`
- Release output: `artifacts/publish/`
