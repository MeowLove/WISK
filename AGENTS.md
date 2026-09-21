# WISK Project Rules

## Scope

WISK is the open-source Windows initialization and configuration workspace.
The repository contains the WISK application source, tests, public catalog
metadata, packaging scripts, and public architecture documentation. The
product requirements manual remains local-only under the Git-ignored
`requirements/` directory.

The `WindowsInitializer.*` project and namespace names are retained for
compatibility during the V3 migration. The public product name and assembly
metadata are WISK.

## Development build

- Use the repository-pinned SDK from `global.json`.
- Use `./Build-Dev.ps1 -Mode Build` for normal WPF development builds.
- Use `./Build-Dev.ps1 -Mode Test` for the full test suite.
- Use `./Build-Dev.ps1 -Mode Run` to build and launch the debug application.
- Add `-Restore` only after a clean checkout or project/dependency change.
- Keep WPF-UI theme dictionaries and `FluentWindow` integration intact.

## Verification

- Shared-code or UI changes require a successful build and relevant tests.
- Run `git diff --check` before committing.
- Do not claim visual runtime verification unless the WPF executable was
  actually launched and inspected.
- Do not apply registry changes, install software, or change system settings
  on the development host.

## Release

- Use `packaging/Publish-Release.ps1` only for a release artifact.
- Release output belongs under the ignored `artifacts/publish/win-x64/` path and
  includes `release-manifest.json`.
- Local builds are unsigned unless a release certificate is explicitly
  supplied; never commit signing keys or credentials.

## Repository boundaries

- Public source, tests, schemas, catalog metadata, and docs belong in Git.
- Internal requirements, test secrets, local runtime state, and generated
  artifacts do not belong in Git.
- Preserve stable task IDs, JSON contracts, backup semantics, and explicit
  Check/Apply/Verify boundaries when evolving the application.
