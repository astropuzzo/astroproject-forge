# Cross-platform parity gate

This document is a release gate, not a wishlist. Windows, Linux and macOS artifacts must not be
published until every **P0 parity** row passes on the native operating system. The Avalonia
application (`dotnet/AstroForge.CrossPlatform`, executable `AstroProjectForge`) is the only
UI on every platform, Windows included: the WPF app `AstroForge.App` was retired and its
shared `MainViewModel` and services now live in `dotnet/AstroForge.CrossPlatform/Shared`.
Every platform uses the same Core assembly, parsers, matching engine, persistence and
export engine.

The former "Windows WPF" column recorded results of the retired WPF build. Those results do
not carry over automatically, so the Windows column now tracks native QA of the Avalonia app.

Status values: `implemented` means the code is present and builds; `native QA` means it
still requires an interaction test on the named OS; `blocked` prevents publication.

| Capability | Avalonia code | Windows QA | Linux QA | macOS QA | Release class |
|---|---:|---:|---:|---:|---:|
| Generic FITS/XISF files and recursive folders | implemented | native QA | native QA | native QA | P0 |
| Multiple calibration libraries, priority and offline state | implemented | native QA | native QA | native QA | P0 |
| Header-first metadata and project fallback values | implemented | native QA | native QA | native QA | P0 |
| Astronomical-night boundary across midnight | shared Core | automated | automated | automated | P0 |
| Filter → configuration session → nights/calibrations tree | implemented | native QA | native QA | native QA | P0 |
| Inspector, batch overrides and provenance | implemented | native QA | native QA | native QA | P0 |
| Manual Flat Epoch link for file/night/session/filter | implemented | native QA | native QA | native QA | P0 |
| Dark/Bias matching and guided review assignments | implemented | native QA | native QA | native QA | P0 |
| Adaptive WBPP grouping-keyword recipe | implemented | native QA | native QA | native QA | P0 |
| Project statistics and CSV/JSON export | implemented | native QA | native QA | native QA | P0 |
| `.astroforge` open/save/autosave/recovery | implemented | native QA | native QA | native QA | P0 |
| Verified project export, preflight, pause/resume/cancel | implemented | native QA | native QA | native QA | P0 |
| Quality series separated by filter/configuration session | implemented | native QA | native QA | native QA | P0 |
| Quality metrics, robust threshold and sortable table | implemented | native QA | native QA | native QA | P0 |
| Quality distribution, clickable frame selection | implemented | native QA | native QA | native QA | P0 |
| Debayer/channel-balanced stretch, zoom and Blink | implemented | native QA | native QA | native QA | P0 |
| Non-destructive exclusions and reveal in file manager | implemented | native QA | native QA | native QA | P0 |
| Standalone Master Library Lab and editable metadata | implemented | native QA | native QA | native QA | P0 |
| Camera-first Master Library organization | shared Core | automated | automated | automated | P0 |
| Transactional organization, hash verification, rollback | shared Core | automated | automated | automated | P0 |
| Settings persistence, diagnostics and support bundle | implemented | native QA | native QA | native QA | P0 |
| First-run onboarding linked to real source/library state | implemented | native QA | native QA | native QA | P0 |
| Signed platform-aware application updates | implemented | native QA | blocked | blocked | commercial |
| Responsive panels at 980–2560 px and HiDPI | implemented | native QA | native QA | native QA | P0 |
| Keyboard navigation and screen-reader names | implemented | native QA | native QA | native QA | P0 |
| English UI/localization | implemented | native QA | native QA | native QA | P0 |
| Windows x64 self-contained single-file build and Inno Setup installer | implemented | native QA | n/a | n/a | P0 |
| `.astroforge` file association opens the project passed as `%1` | implemented | native QA | n/a | n/a | P0 |
| Upgrade over a WPF-era install (`AstroForge.App.exe` removed, shortcuts repointed) | implemented | native QA | n/a | n/a | P0 |
| Linux self-contained x64/ARM64 package | implemented | n/a | native QA | n/a | P0 |
| macOS Intel/Apple Silicon `.app` bundle | implemented | n/a | n/a | native QA | P0 |
| macOS signing, hardened runtime and notarization | prepared only | n/a | n/a | blocked | commercial |
| Linux desktop entry, icon and AppImage/deb packaging | pending | n/a | blocked | n/a | commercial |

## Required native scenarios

Each architecture must run these tests against disposable fixtures and at least one real,
redacted project:

1. Add individual files and folders, analyze, close, reopen and restore state.
2. Confirm session grouping across midnight and Flat Epoch manual relinking.
3. Open every workspace at 100%, 150% and 200% scaling; verify no clipped action.
4. Run one Quality series, sort every metric, click chart outliers, zoom, Blink and exclude.
5. Scan and normalize a copied Master Library; verify camera-first paths and rollback.
6. Build a project, run preflight, interrupt export, resume and compare all SHA-256 hashes.
7. Reveal files in Explorer, Finder or the Linux file manager and export a support ZIP.
8. Launch the packaged artifact from a clean standard-user account with no .NET SDK.

## Publication rule

CI compilation is necessary but not sufficient. A GitHub release is allowed only after the
four cross-platform architecture jobs and the Windows QA job pass and the three native QA
columns contain no `blocked` P0 row.
# Stato aggiornamenti

Windows scarica e avvia il setup con avanzamento visibile. macOS e Linux selezionano
automaticamente il pacchetto corretto dalla release GitHub, ne verificano lo SHA-256,
mostrano il download e aprono l’installer di sistema. La sostituzione dell’app richiede
la conferma prevista dal sistema operativo; non viene simulata come installazione silenziosa.
