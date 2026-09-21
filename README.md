# WISK 3.0

WISK (Windows Init Setup Kit) is an open-source Windows 11 initialization and
configuration workspace. It combines auditable system settings, registry-backed
features, software actions, recovery evidence, and a safe Check -> Apply ->
Verify execution model.

WISK is the Windows member of the MeowLove Init Setup Kit family. The Linux
counterpart is [LISK](https://github.com/MeowLove/LISK).

## What is public

This repository contains the application source, tests, public catalog data,
schemas, packaging scripts, legacy migration material, and architecture docs.
The local product requirements manual remains under the ignored `requirements/`
directory and is never published.

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
packaging/           Release and CLI publishing scripts
tools/               Read-only UI smoke and catalog validation tools
docs/                Public architecture and development documentation
legacy/              Retained V1 PowerShell implementation for migration context
requirements/        Local-only V3 requirements; ignored by Git
```

The internal `WindowsInitializer.*` project names are intentionally retained
for compatibility during the V3 migration. The user-facing product, assembly
metadata, title, and release version are WISK 3.0.0.

## Build and test

The repository pins .NET SDK `10.0.401` in `global.json`.

```powershell
.\Build-Dev.ps1 -Mode Build -Restore
.\Build-Dev.ps1 -Mode Test
node tools/validate-catalog.mjs
```

The first clean build may use `-Restore`; subsequent local builds can omit it.
The default build targets the WPF application, while the full test mode runs
the complete automated suite. No registry, software installation, or system
setting changes are required by these checks.

For a release artifact, use `packaging/Publish-Release.ps1`. The script creates
a Windows x64 single-file package and a manifest with source commit, hashes,
build time, and signature verification status. Published artifacts are kept in
ignored `artifacts/` output and are not committed as source.

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
