# WISK 3.0 Architecture

## 1. Purpose

WISK 3.0 is the public Windows implementation of the MeowLove Init Setup Kit
family. It provides a Windows 11 x64 workspace for inspecting, configuring,
planning, and safely executing system initialization tasks. The product remains
auditable: a user can inspect the catalog, configure parameters, preview an
immutable plan, run Check -> Apply -> Verify, and export evidence without
depending on private service infrastructure.

The public repository is the source of implementation and catalog truth. The
local `requirements/` directory is the behavior authority for maintainers but
is deliberately excluded from Git and release archives.
`src/` is the canonical WISK 3.0 implementation. `/legacy/` is ignored as a
guardrail and is not a runtime or build input.

## 2. Repository architecture

```text
WISK/
  src/
    Wisk.Contracts/       stable JSON and task contracts
    Wisk.Core/             catalog, planning, validation
    Wisk.Execution/        run state, history, diagnostics
    Wisk.Platform.Windows/ fixed Windows capability adapters
    Wisk.PowerShell/       bounded structured bridge
    Wisk.App/              WPF workspaces and localization
    Wisk.Cli/              unattended read-only/explicit CLI
  tests/                                 automated contract and behavior tests
  catalog/                               public software/settings index
  schema/                                public machine-readable schemas
  packaging/                            release and CLI packaging
  tools/                                validation and UI smoke utilities
  docs/                                 public architecture and verification boundary
  requirements/                         local-only maintainer manual
  artifacts/                            ignored build, test, and publish output
```

The project and namespace names under `src/` use `Wisk.*`. Product metadata,
UI copy, executable names, release version, and documentation use WISK.

## 3. Runtime layers

```text
WPF App / CLI
    -> Contracts
    -> Core (catalog, plan builder, templates, relations)
    -> Execution (Check -> Apply -> Verify, state, history, reports)
    -> Platform.Windows (registry, WinGet, Windows feature adapters)
    -> PowerShell bridge (fixed operations and framed JSON responses)
```

- **Contracts** owns WISK 3.0 task, plan, template, result, and boundary
  records. Machine-readable fields change only with an explicit WISK contract
  decision.
- **Core** owns catalog descriptors, configuration validation, typed relations,
  dependency closure, conflict detection, deterministic order, and plan hashes.
- **Execution** owns immutable plans, atomic run state, cancellation, timeout,
  retries, history retention, failure diagnostics, and self-test reports.
- **Platform.Windows** maps allow-listed task IDs to Windows APIs, registry
  targets, WinGet package IDs, and native settings entry points.
- **PowerShell** runs only fixed bridge operations. Scripts and JSON requests are
  framed through standard input so user content is not concatenated into code or
  oversized process arguments.
- **App** exposes the Home, Settings, Software, and Backup & recovery
  workspaces, configuration dialogs, localization, theme resources, and the
  persistent plan/execution pane.
- **CLI** reuses the same contracts and execution boundaries. Its default mode
  is read-only; Apply requires an explicit command and authorization.

## 4. Configuration and safety

All entries are unselected by default. Items needing input use a configure
before add flow: validation completes first, cancellation does not enqueue an
item, and an existing draft can be edited. Plan additions preserve explicit
enabled, disabled, and default semantics; disabled never means restore default.

Registry-backed tasks use a shared data-driven catalog for task ID, hive,
subkey, value name, accepted states, risk, backup policy, and conflicts. Before
the first write in a run, supported original values are snapshotted under the
run ID. Check, Apply, Verify, template import/export, and rollback evidence use
the same allow-listed mapping. Unsupported or version-dependent presets remain
fixed and are not presented as invented reversible switches.

The application never silently creates a restore point, changes a registry, or
installs software during detection. A system restore point is an explicit
separate task. Native management links open Windows settings without pretending
that an external UI action was applied by WISK.

## 5. Public catalog boundary

`catalog/index.json` is public metadata only. It may contain repository links,
release links, versions, supported platforms, runtime requirements, package IDs,
categories, and store placement. It must not contain credentials, private URLs,
user data, local filesystem paths, signing material, telemetry records, or
scripts that mutate a machine.

The catalog schema and dependency-free validator run in CI. The application
repository can consume a pinned catalog revision or a reviewed release; runtime
updates must not silently replace an executable or trust key.

## 6. Versioning and release

WISK 3.0.0 is a new public source baseline. It owns its task IDs, contracts,
audit semantics, backup semantics, namespaces, and user-data roots. Future
breaking contract changes require a major version and a release note.

Release artifacts are Windows x64 single-file packages with a manifest recording
source commit, UTC build time, SHA-256, and signature status. Local unsigned
builds are clearly marked. Signing certificates, private keys, and store
credentials remain outside the repository.

## 7. Build and output boundary

`Build-Wisk.ps1` is the single build entry point. The profile/runtime matrix is
explicit:

| Profile | Configuration | Runtime mode |
| --- | --- | --- |
| `Test` | Debug | Framework-dependent or self-contained |
| `Development` | Debug | Framework-dependent or self-contained |
| `Release` | Release | Framework-dependent or self-contained |

All MSBuild `bin` and `obj` output is redirected below
`artifacts/build/<profile>/<runtime-mode>/<rid>/`; test evidence is below
`artifacts/test-results/<profile>/<runtime-mode>/<rid>/`; publish payloads are
below `artifacts/publish/<profile>/<runtime-mode>/<rid>/`. The root `.gitignore`
also blocks project-local `bin/` and `obj/` output as a drift guard.

## 8. Verification boundary

Automated validation includes Core/Execution tests, template and history tests,
catalog mapping, bridge framing, controlled WinGet responses, and WPF contract
checks. Build and test commands are:

```powershell
.\Build-Wisk.ps1 -Target Build -Profile Development -RuntimeMode FrameworkDependent -Restore
.\Build-Wisk.ps1 -Target Test -Profile Test -RuntimeMode FrameworkDependent -Restore -Coverage
.\Build-Wisk.ps1 -Target Publish -Profile Release -RuntimeMode FrameworkDependent
.\Build-Wisk.ps1 -Target Publish -Profile Release -RuntimeMode SelfContained
.\Build-Wisk.ps1 -Target Publish -Profile Release -RuntimeMode SelfContained -Component Cli
node tools/validate-catalog.mjs
```

These checks do not prove real Windows Apply/Verify behavior. Actual registry,
software, restore-point, reboot, UAC, and Authenticode acceptance tests belong
in an isolated Windows 11 VM or dedicated test machine. Do not run them on a
development host or report them as passed without evidence.
