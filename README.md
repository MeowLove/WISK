# WISK

WISK is a public catalog for the WISK Windows initializer and the repositories,
software-store entries, and settings-store entries that belong to the project.

This repository is intentionally metadata-only. It does not contain the
initializer source code, installers, private configuration, credentials, or
scripts that modify a computer. Release binaries remain in the application
repository's release page; this repository publishes the stable links and
machine-readable list information that clients can consume.

The canonical catalog is [`catalog/index.json`](catalog/index.json). Consumers
can read the published raw file at:

```text
https://raw.githubusercontent.com/MeowLove/WISK/main/catalog/index.json
```

## Current entry

The initial entry is **WISK**, the public name for the Windows Initializer
preview. Its implementation remains in
[`MeowLove/WindowsInitTools`](https://github.com/MeowLove/WindowsInitTools),
while this repository records its version, platform, runtime requirement,
repository link, release page, and store placement.

The catalog does not claim that a release asset is available until the
application repository publishes one. The current entry is therefore marked
`preview`.

## Repository shape

- `catalog/index.json` is the single source of truth for public entries.
- `schema/catalog.schema.json` describes the data contract for consumers.
- `tools/validate-catalog.mjs` performs dependency-free structural and
  cross-reference checks.
- `.github/workflows/validate.yml` validates every pull request and push.

## Updating the catalog

1. Edit `catalog/index.json` only for a catalog change.
2. Run `node tools/validate-catalog.mjs`.
3. Keep repository and release URLs public and stable.
4. Do not add secrets, local paths, user data, binaries, or machine-changing
   scripts.

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for the entry requirements.

## Naming note

`WISK` is the current working product name. Before a broad public launch,
review trademark, package-name, domain, and repository-name availability in
the intended markets. A catalog entry can be renamed without moving the
application source repository.

## License

The catalog metadata and validation tooling are released under the MIT
License. This license does not grant rights to third-party names, logos,
software, or linked release assets.

## 中文说明

WISK 是公开的目录仓库，只维护 WISK Windows 初始化工具以及后续仓库、软件商店和设置商店所需的列表信息。主程序源码、安装包和私有配置仍保留在应用仓库；本仓库只发布结构化元数据、稳定链接和校验规则。

当前条目把现有 Windows Initializer V2.3 作为 `preview` 发布。正式公开前仍需复核 WISK 的商标、软件包名、域名和仓库名可用性。

