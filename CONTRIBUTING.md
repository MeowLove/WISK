# Contributing to WISK

WISK 3.0 contains the Windows application, its automated tests, and the
public catalog used by software and settings-store clients. Contributions are
welcome when they preserve the repository's safety boundaries and stable task
contracts.

## Before opening a pull request

```powershell
.\Build-Wisk.ps1 -Target Build -Profile Development -RuntimeMode FrameworkDependent -Restore
.\Build-Wisk.ps1 -Target Test -Profile Test -RuntimeMode FrameworkDependent -Restore -Coverage
node tools/validate-catalog.mjs
git diff --check
```

`Build-Dev.ps1` remains a compatibility wrapper. All build and test output
must remain under the canonical `artifacts/` subdirectories. Do not run
registry Apply, software installation, restore-point, or reboot actions on a
development host.

## Application changes

- Keep task IDs, plan hashes, template compatibility, and Check/Apply/Verify
  semantics stable unless the change includes an explicit migration note.
- Add catalog entries with tests for validation, risk, relations, backup policy,
  and localized labels where applicable.
- Preserve configure-before-add behavior: cancellation must not enqueue a task,
  and enabled, disabled, and default states must remain distinct.
- Do not make version-dependent or unreliable registry behavior look like a
  reversible switch. Keep those items as fixed presets with an honest note.
- Never put credentials, user data, local paths, signing keys, or generated
  `bin/`, `obj/`, and `artifacts/` output in a pull request.

## Catalog changes

`catalog/index.json` is public metadata. Entries require stable IDs, public
repository and release links, platform/runtime information, lifecycle status,
and explicit software/settings-store placement. The schema and validator must
be updated together when the data contract changes.

## Requirements boundary

The authoritative WISK 3.0 requirements manual is local-only under the ignored
`requirements/` directory. Do not commit it or copy it into a release. Public
behavioral decisions belong in `docs/WISK-3.0-Architecture.md` and the relevant
source-level tests.

## Pull requests

Describe the user-visible behavior, safety impact, verification commands, and
any environment-dependent checks that were not run. Keep unrelated legacy
cleanup out of feature changes; the `legacy/` tree is retained for migration
context and is not the default WISK runtime.
