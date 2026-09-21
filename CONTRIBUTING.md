# Contributing to WISK

WISK is a catalog repository. Contributions should update public metadata and
should not turn this repository into an application or installer repository.

## Adding or updating an application

Every application entry must include:

- a stable lowercase `id`;
- a public repository reference;
- a public release page or documented distribution endpoint;
- supported operating system, architecture, and runtime information;
- a clear lifecycle status such as `preview`, `stable`, or `retired`;
- explicit software-store and settings-store placement.

Do not add local filesystem paths, credentials, telemetry data, unsigned
executables, or scripts that modify a user's machine. Links must point to a
public page that a reviewer can inspect.

## Pull requests

Run the validator before opening a pull request:

```powershell
node tools/validate-catalog.mjs
```

Keep unrelated documentation, product source, and release assets in their
own repositories. Changes to the schema should explain the compatibility
impact for catalog consumers.

