# WISK Project Rules

## Boundaries

- `src/` contains application layers; `tests/` contains automated tests.
- `catalog/` and `schema/` contain public machine-readable metadata.
- `docs/` contains public design and development documentation.
- `packaging/` contains release wrappers; `tools/` contains validation and
  smoke utilities; `legacy/` is migration-only code, not the default runtime.
- `requirements/` is the local product manual and is ignored by Git.
- `artifacts/` is generated output and is ignored by Git.
- `handoff/`, `work/`, and other task-state directories are local-only and
  ignored by Git; never publish their contents.
- Keep the `WindowsInitializer.*` project and namespace names stable during
  the WISK 3.0 migration.

## Build contract

- Use the SDK pinned in `global.json`.
- Use `./Build-Wisk.ps1` for application/solution build, test, run, and publish
  operations. `packaging/Publish-*.ps1` are release wrappers that delegate
  compilation to it before signing and writing manifests.
- `Build-Dev.ps1` is a compatibility wrapper only.
- Build profiles are `Test`, `Development`, and `Release`; runtime modes are
  `FrameworkDependent` and `SelfContained`.
- All intermediate output belongs under
  `artifacts/build/<profile>/<runtime-mode>/<rid>/`; publish output belongs
  under `artifacts/publish/<profile>/<runtime-mode>/<rid>/`; test results belong
  under `artifacts/test-results/<profile>/<runtime-mode>/<rid>/`.
- Never intentionally create project-local `bin/` or `obj/` output. The
  `**/bin/` and `**/obj/` ignore rules are a guardrail, not an output contract.

Examples:

```powershell
.\Build-Wisk.ps1 -Target Build -Profile Development -RuntimeMode FrameworkDependent -Restore
.\Build-Wisk.ps1 -Target Test -Profile Test -RuntimeMode FrameworkDependent -Restore -Coverage
.\Build-Wisk.ps1 -Target Publish -Profile Release -RuntimeMode FrameworkDependent
.\Build-Wisk.ps1 -Target Publish -Profile Release -RuntimeMode SelfContained
.\Build-Wisk.ps1 -Target Publish -Profile Release -RuntimeMode SelfContained -Component Cli
```

## Safety and verification

- Preserve stable task IDs, JSON contracts, backup semantics, and explicit
  Check/Apply/Verify boundaries.
- Do not apply registry changes, install software, create restore points, or
  change system settings on the development host.
- Shared-code changes require build and relevant tests; run `git diff --check`
  before committing.
- Do not claim WPF startup, UAC, real Apply/Verify, or Authenticode validation
  without actually running and inspecting it in an isolated Windows test
  environment.
- Never commit credentials, signing keys, local requirements, task state, or
  generated artifacts.
