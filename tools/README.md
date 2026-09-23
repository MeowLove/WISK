# WISK Tools

These scripts are maintained helpers for workflows that are not safe to model
as automatic WISK plan tasks.

## Preview packages

`Build-PreviewPackages.ps1` creates separate framework-dependent and
self-contained WISK 3.0.0 Preview ZIPs under the ignored
`artifacts/preview/3.0.0/` directory. It checks each release manifest against
the current source commit, stages the required license and testing guide, and
verifies both the staged package and the completed ZIP. It refuses to overwrite
existing ZIPs by default and leaves unrelated files or subdirectories alone.
`-ReplaceExisting` moves the known ZIPs and checksum file into a timestamped
`.superseded-*` folder before creating replacements.

`Verify-PreviewPackage.ps1` checks an extracted package or ZIP for the exact
expected file set and verifies the WISK executable's length and SHA-256 against
its release manifest. This establishes package consistency, not publisher
authenticity.

## Manage-WindowsTerminalPowerShell.ps1

This migrated helper switches the Windows Terminal default profile between
Windows PowerShell 5.1 and PowerShell 7, reports detected profiles, installs
PowerShell 7 through WinGet, and opens the Terminal settings UI or file.

It creates a timestamped settings.json backup before changing the default
profile. It intentionally refuses JSONC files with comments or trailing
commas; use the Windows Terminal Settings UI for those files. It is not part
of the automatic Check -> Apply -> Verify plan pipeline because Windows
Terminal settings are user-owned JSON state and their schema can vary by
package version.

Run it from an elevated or non-elevated PowerShell session as appropriate for
the selected action:

~~~powershell
pwsh -NoProfile -File .\tools\Manage-WindowsTerminalPowerShell.ps1 -Action Status
pwsh -NoProfile -File .\tools\Manage-WindowsTerminalPowerShell.ps1 -Action PS7
~~~
