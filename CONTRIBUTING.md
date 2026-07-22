# Contributing to AstroProject Forge

Thank you for testing AstroProject Forge. The most useful contributions during the public beta are reproducible bug reports, metadata edge cases and platform-specific validation.

## Before opening an issue

- remove target names, coordinates and personal paths from logs;
- never upload proprietary or personal FITS/XISF frames without permission;
- include the app version, operating system, camera model and acquisition software;
- describe the expected calibration relationship and what Forge inferred;
- attach the privacy-safe support ZIP when it helps reproduce the issue.

## Code contributions

Please open an issue before starting a substantial change. Pull requests require an explicit contributor agreement from the maintainer because the project currently uses a proprietary source license. Submission of a pull request does not grant rights to the repository or waive the contributor's rights.

Run before submitting:

```powershell
dotnet run --project dotnet/AstroForge.Core.Tests/AstroForge.Core.Tests.csproj -c Release
dotnet build dotnet/AstroForge.App/AstroForge.App.csproj -c Release
dotnet build dotnet/AstroForge.CrossPlatform/AstroForge.CrossPlatform.csproj -c Release
```

Italian and English user-facing documentation should stay synchronized.
