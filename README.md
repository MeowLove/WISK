# WISK 3.1

<p><img src="src/Wisk.App/Assets/wisk-logo.png" alt="WISK application icon" width="112" /></p>

WISK (Windows Init Setup Kit) is an open-source Windows 11 initialization and
configuration workspace. It combines auditable system settings, registry-backed
features, software actions, recovery evidence, and a safe Check -> Apply ->
Verify execution model.

WISK is the Windows member of the MeowLove Init Setup Kit family. The Linux
counterpart is [LISK](https://github.com/MeowLove/LISK).

## WISK 3.1 update and feedback

The current source version is WISK 3.1.0. The first public release of this
implementation was WISK 3.0.0. WISK 3.1 adds a controlled plugin workspace
and an AI assistant placeholder labeled “In beta development.” The assistant is
intended to answer WISK initialization and supported-settings questions only.
WISK 3.1.0 does not call a cloud AI API, run a local AI model, or execute
AI-generated actions. We plan to iterate rapidly based on community feedback.
Please report reproducible issues so we can prioritize fixes:

- [Download WISK 3.0.0](https://github.com/MeowLove/WISK/releases/tag/v3.0.0)
- [Read the release testing guide](docs/Preview-Testing.md)
- [Report a problem](https://github.com/MeowLove/WISK/issues/new?template=bug_report.md)

This build is unsigned and has not completed isolated UAC, registry, software
installation, reboot, or real Apply/Verify acceptance. For ordinary testing,
run as a standard user and do not apply changes on a daily-use, work, or
production computer. See the testing guide for the full boundary.

## What is public

This repository contains the application source, tests, public catalog data,
schemas, packaging scripts, and architecture docs.
The local product requirements manual remains under the ignored `requirements/`
directory and is never published. Superseded source and compatibility paths are
not part of this workspace. `src/` is the only WISK 3.1 implementation.

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
requirements/        Local-only V3.1 requirements; ignored by Git
```

The source namespaces and project paths use `Wisk.*`; the product, assembly
metadata, executable names, title, and application version use WISK 3.1.0. The
transparent logo at `src/Wisk.App/Assets/wisk-logo.png` is embedded as a WPF
resource and used in the main window; the white-background variant is retained
as a design reference.

## Build and test

The repository pins .NET SDK `10.0.401` in `global.json`.

```powershell
.\Build-Wisk.ps1 -Target Run -Profile Development -RuntimeMode FrameworkDependent -Restore
.\Build-Wisk.ps1 -Target Build -Profile Development -RuntimeMode FrameworkDependent -Restore
.\Build-Wisk.ps1 -Target Test -Profile Test -RuntimeMode FrameworkDependent -Restore -Coverage
.\Build-Wisk.ps1 -Target Publish -Profile Release -RuntimeMode FrameworkDependent
.\Build-Wisk.ps1 -Target Publish -Profile Release -RuntimeMode SelfContained
.\Build-Wisk.ps1 -Target Publish -Profile Release -RuntimeMode SelfContained -Component Cli
node tools/validate-catalog.mjs
```

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
