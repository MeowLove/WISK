# Legacy Migration Boundary

WISK 3.0 is the canonical implementation. The ignored legacy directory is
only an audit source and is not a runtime dependency.

## Migrated behavior

- Computer name, local accounts, language packs, regional formats, display
  language, supplemental fonts, wireless display, and device setup region use
  built-in adapters.
- WinGet software and runtime entries use the public catalog and fixed package
  IDs. Legacy machine-scoped entries pin `machine` in the plan and pass it to
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
- The old Windows Terminal PowerShell switcher is maintained as
  tools/Manage-WindowsTerminalPowerShell.ps1. It keeps its JSON backup and
  JSONC refusal behavior and remains an explicit helper rather than an
  automatic plan task.

## Deliberate boundaries

- Set-ExecutionPolicy RemoteSigned is not migrated. WISK launches its fixed
  bridge with an explicit process policy and does not weaken the host policy.
- Offline installers, proprietary archives, RDP Wrapper, and other legacy
  assets remain unavailable until supplied through a signed controlled
  extension package. The catalog keeps these entries visible as manual review
  items; WISK does not invent download URLs, binaries, or rollback behavior.
- Opening OptionalFeatures.exe, pausing for keyboard input, and other
  presentation-only script behavior are not automatic tasks.

Automated tests verify the contracts and generated commands. Real Windows
feature, WinGet, Terminal, registry, reboot, and UAC behavior still requires an
isolated Windows validation environment.
