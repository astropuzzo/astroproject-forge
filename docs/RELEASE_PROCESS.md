# Release process

The version and channel are defined in `version.json`.

## Validation

```powershell
.\qa-gate.ps1
.\scripts\verify-cross-platform-parity.ps1
```

## Windows package

```powershell
.\build-distribution.ps1 -Channel Stable -Version 1.0.0
```

This produces the installer and portable ZIP under `artifacts/distribution`.

## Linux and macOS

The `Cross-platform parity QA` GitHub workflow builds:

- Linux x64 and ARM64 `.deb` and `.tar.gz`;
- macOS Intel and Apple Silicon `.dmg` and `.zip`.

## GitHub release

Create a non-draft, non-prerelease tag matching the version, then attach only installable and portable packages. GitHub adds the source archives automatically.

The Windows updater reads the latest stable GitHub release, checks the published asset size and SHA-256 digest, downloads it into the local application data folder and asks before starting the installer.
