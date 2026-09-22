# WISK Tools

These scripts are maintained helpers for workflows that are not safe to model
as automatic WISK plan tasks.

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
