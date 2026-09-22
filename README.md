# WISK 3.0

WISK (Windows Init Setup Kit) is an open-source Windows 11 initialization and
configuration workspace. It combines auditable system settings, registry-backed
features, software actions, recovery evidence, and a safe Check -> Apply ->
Verify execution model.

WISK is the Windows member of the MeowLove Init Setup Kit family. The Linux
counterpart is [LISK](https://github.com/MeowLove/LISK).

## What is public

This repository contains the application source, tests, public catalog data,
schemas, packaging scripts, and architecture docs.
The local product requirements manual remains under the ignored `requirements/`
directory and is never published. The superseded V1 archive is also local-only
under the ignored `legacy/` directory; `src/` is the canonical WISK 3.0
implementation.

The public catalog is [`catalog/index.json`](catalog/index.json), validated by
[`tools/validate-catalog.mjs`](tools/validate-catalog.mjs). It records WISK's
repository, version, platform, runtime requirement, and software/settings-store
placement without embedding private configuration or machine-changing scripts.

## Repository layout

```text
src/                 WISK application layers and shared contracts
tests/               Core, execution, bridge, catalog, and UI contract tests
catalog/             Public software/settings catalog metadata
schema/              Machine-readable catalog and profile schemas
packaging/           Release and CLI publishing wrappers
tools/               Read-only UI smoke and catalog validation tools
artifacts/           Ignored build, test, and publish output
docs/                Public architecture and development documentation
requirements/        Local-only V3 requirements; ignored by Git
```

The internal `WindowsInitializer.*` project names are intentionally retained
for compatibility during the V3 migration. The user-facing product, assembly
metadata, title, and release version are WISK 3.0.0.

## Build and test

The repository pins .NET SDK `10.0.401` in `global.json`.

```powershell
.\Build-Wisk.ps1 -Target Build -Profile Development -RuntimeMode FrameworkDependent -Restore
.\Build-Wisk.ps1 -Target Test -Profile Test -RuntimeMode FrameworkDependent -Restore -Coverage
.\Build-Wisk.ps1 -Target Publish -Profile Release -RuntimeMode FrameworkDependent
.\Build-Wisk.ps1 -Target Publish -Profile Release -RuntimeMode SelfContained
.\Build-Wisk.ps1 -Target Publish -Profile Release -RuntimeMode SelfContained -Component Cli
node tools/validate-catalog.mjs
```

`Build-Dev.ps1` remains a compatibility wrapper for the old `-Mode` syntax.
The script matrix keeps all intermediate output in
`artifacts/build/<profile>/<runtime-mode>/<rid>/`, test evidence in
`artifacts/test-results/<profile>/<runtime-mode>/<rid>/`, and publish payloads
in `artifacts/publish/<profile>/<runtime-mode>/<rid>/`. Framework-dependent
packages require the pinned .NET 10 runtime; self-contained packages include
the runtime. No registry, software installation, or system setting changes are
required by these checks.

For the signed release workflow, use `packaging/Publish-Release.ps1` or
`packaging/Publish-Cli.ps1`. They create Windows x64 self-contained single-file
packages under the same `artifacts/publish/Release/SelfContained/` boundary and
write manifests with source commit, hashes, build time, and signature status.

## Safety boundary

WISK is an administrative tool. All catalog entries are unselected by default;
elevated and high-risk actions require explicit authorization. Registry-backed
actions snapshot original values before writes, preserve audit information, and
use the existing Check/Apply/Verify pipeline. Real Apply/Verify validation must
be performed only in an isolated Windows test environment.

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md) and the local
development rules in [`AGENTS.md`](AGENTS.md). Do not submit local requirements,
credentials, user data, signing keys, or generated build output.

## License and names

The application lineage and catalog use the licenses recorded in their source
files. Third-party names, logos, package identifiers, and linked release assets
remain the property of their respective owners. WISK and LISK are project names
used by MeowLove; trademark and package-name availability should be reviewed
before broad commercial distribution.
