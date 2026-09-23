# WISK 3.0.0 Preview Testing Guide

## English (authoritative)

WISK 3.0.0 Preview is an unsigned, early test build. Download the source and
preview packages from the official WISK repository:
<https://github.com/MeowLove/WISK>.

### Choose a package

- `FrameworkDependent` requires Windows 11 x64 and the .NET 10 Desktop Runtime
  for x64. If that runtime is not already installed, use the self-contained
  package instead.
- `SelfContained` includes the .NET runtime and requires Windows 11 x64.

Extract the ZIP to a new folder. Do not run WISK from inside the ZIP. Compare
the ZIP's SHA-256 with the separately provided `SHA256SUMS.txt`, then run
`powershell.exe -NoProfile -File .\Verify-Package.ps1` from the extracted
folder. The verifier compares the executable length and SHA-256 with
`release-manifest.json`. This detects inconsistent package contents; it does
not prove who published the file. If the current PowerShell policy blocks the
verifier, do not relax it; use `Get-FileHash .\WISK.exe -Algorithm SHA256`
and compare its result and file length with the manifest instead.

The release manifest marks this local build as unsigned. Windows may show a
security warning. The hash is not a substitute for a trusted digital signature.

### Suggested feedback

Run WISK as a standard user and browse the Home, Settings, Software, and
Backup & recovery workspaces. Check language and theme presentation, settings
configuration and cancellation, plan preview, history labels, and diagnostic
export wording. Do not approve elevation prompts during ordinary preview
testing. Do not click Apply on a daily-use, work, or production computer.

Testing real system changes requires a disposable, isolated Windows 11 test
environment with a recoverable snapshot. This preview has not completed full
isolated UAC, registry, software-install, reboot, or real Apply/Verify
acceptance. UI startup screenshots, if supplied with the package, cover only
the listed views and do not establish system-change safety.

When reporting a problem, include WISK version, package type, Windows edition
and build, reproduction steps, expected result, actual result, and the relevant
task ID if visible. Review screenshots, logs, and diagnostic exports before
sharing them. Remove names, account identifiers, device identifiers, network
details, file paths, and other personal information. Do not attach passwords,
tokens, or signing material.

## 简体中文

WISK 3.0.0 Preview 是尚未签名的早期测试版本。请从 WISK 官方仓库获取
源码和试用包：<https://github.com/MeowLove/WISK>。

### 选择试用包

- `FrameworkDependent` 需要 Windows 11 x64，并且机器已安装 x64 的
  .NET 10 Desktop Runtime。没有该运行库时，请使用自包含版本。
- `SelfContained` 已包含 .NET 运行库，要求 Windows 11 x64。

先将 ZIP 解压到新目录，不要直接从 ZIP 中启动程序。将 ZIP 的 SHA-256
与单独提供的 `SHA256SUMS.txt` 对照，再在解压目录运行
`powershell.exe -NoProfile -File .\Verify-Package.ps1`。校验器会把程序文件大小和 SHA-256
与 `release-manifest.json` 比较。它能发现包内文件不一致，但不能证明发布者身份。如果当前
PowerShell 策略阻止运行校验器，请勿放宽策略；可用内置命令
`Get-FileHash .\WISK.exe -Algorithm SHA256`，并将结果和文件大小与发布清单对照。

清单明确标记此本地构建未签名，Windows 可能显示安全警告。哈希值不能替代
可信数字签名。

### 建议反馈内容

请以普通用户运行，浏览首页、设置、软件、备份与恢复工作区；检查语言和主题、
设置配置与取消、计划预览、历史状态文字和诊断导出提示。普通预览过程中不要
同意提升权限，也不要在日常、工作或生产电脑上点击“执行”。

真实系统变更只能在有可恢复快照的 Windows 11 一次性隔离测试环境中验证。
本版本尚未完成隔离环境中的完整 UAC、注册表、软件安装、重启和真实
Apply/Verify 验收。随包提供的启动截图（如有）只覆盖所列界面，不能证明系统
修改安全。

反馈请包含 WISK 版本、包类型、Windows 版本和内部版本号、复现步骤、预期结果、
实际结果，以及界面上可见的任务 ID。分享截图、日志和诊断导出前，请检查并移除
姓名、账户标识、设备标识、网络信息、文件路径等个人信息。不要附上密码、令牌或
签名材料。
