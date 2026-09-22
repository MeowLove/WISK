# WISK 3.0 Feature Boundary

WISK 3.0 is the only implementation. Superseded source and compatibility paths
are not runtime dependencies.

## Implemented behavior

- Computer name, local accounts, language packs, regional formats, display
  language, supplemental fonts, wireless display, and device setup region use
  built-in adapters.
- Supplemental fonts use explicit selection: the configuration dialog requires
  at least one Japanese, Korean, European, or
  Indic group, the plan stores canonical selection IDs, and the bridge
  validates only the selected Windows capabilities.
- Display language preferences expose target-scope options. The
  plan can target the current user, the system UI language, and welcome/new
  users after the target language pack is installed. Windows has no supported
  readback for the last synchronization scope, so Verify reports manual
  review after confirming the readable current-user and system scopes.
- WinGet software and runtime entries use the public catalog and fixed package
  IDs. Machine-scoped entries pin `machine` in the plan and pass it to
  Check, Apply, and Verify; an explicit profile proxy is carried into the same
  commands.
- Windows Subsystem for Linux configures the optional feature first; after the
  required reboot, a second Apply completes WSL 2 installation, default
  version, and kernel update.
- The virtual machine platform task covers both VirtualMachinePlatform and
  HypervisorPlatform as one functional capability.
- Registry presets use tracked resources and the shared registry catalog,
  preserving Check -> Apply -> Verify, original-value evidence, and conflict
  rules.
- Eighteen low-risk registry settings are configurable with explicit
  `enabled`, `disabled`, and `default` states. The current desktop, Explorer,
  and privacy additions include Meet Now, Copilot, and Windows Search web
  results. Each uses the shared WISK registry source and target catalogs.
- Fixed presets remain grouped by user purpose. A configurable setting that
  overlaps a fixed preset receives an explicit conflict relation; unsupported
  or version-dependent entries are not exposed as reversible switches.
- Apply results that require a restart remain completed. The audit keeps
  `RebootRequired`, while a later read-only Verify produces a derived
  `VerificationStatus`. The derived state distinguishes pending restart,
  verified, verification failed, and manual verification required without
  rewriting the original snapshot.
- The Windows Terminal PowerShell switcher is maintained as
  tools/Manage-WindowsTerminalPowerShell.ps1. It keeps its JSON backup and
  JSONC refusal behavior and remains an explicit helper rather than an
  automatic plan task.

## Deliberate boundaries

- Set-ExecutionPolicy RemoteSigned is outside WISK scope. WISK launches its fixed
  bridge with an explicit process policy and does not weaken the host policy.
- The one-off `winget update winget` and global
  `ProxyCommandLineOptions` changes are not plan tasks. WISK keeps App
  Installer self-update manual and carries a profile proxy explicitly on each
  package command, so execution does not mutate a global WinGet setting.
- Offline installers, proprietary archives, RDP Wrapper, and other unavailable
  assets remain unavailable until supplied through a signed controlled
  extension package. The catalog keeps these entries visible as manual review
  items; WISK does not invent download URLs, binaries, or rollback behavior.
- Opening OptionalFeatures.exe, pausing for keyboard input, and other
  presentation-only script behavior are not automatic tasks.

Automated tests verify the contracts and generated commands. Real Windows
feature, WinGet, Terminal, registry, reboot, and UAC behavior still requires an
isolated Windows validation environment.
