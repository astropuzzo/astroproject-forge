# Cross-platform parity gate

This document is a release gate, not a wishlist. Linux and macOS artifacts must not be
published until every **P0 parity** row passes on the native operating system. The WPF
application remains the Windows reference build; the Avalonia frontend uses the same
`MainViewModel`, Core assembly, parsers, matching engine, persistence and export engine.

Status values: `implemented` means the code is present and builds; `native QA` means it
still requires an interaction test on the named OS; `blocked` prevents publication.

| Capability | Windows WPF | Avalonia code | Linux QA | macOS QA | Release class |
|---|---:|---:|---:|---:|---:|
| Generic FITS/XISF files and recursive folders | pass | implemented | native QA | native QA | P0 |
| Multiple calibration libraries, priority and offline state | pass | implemented | native QA | native QA | P0 |
| Header-first metadata and project fallback values | pass | implemented | native QA | native QA | P0 |
| Astronomical-night boundary across midnight | pass | shared Core | automated | automated | P0 |
| Filter → configuration session → nights/calibrations tree | pass | implemented | native QA | native QA | P0 |
| Inspector, batch overrides and provenance | pass | implemented | native QA | native QA | P0 |
| Manual Flat Epoch link for file/night/session/filter | pass | implemented | native QA | native QA | P0 |
| Dark/Bias matching and guided review assignments | pass | implemented | native QA | native QA | P0 |
| Adaptive WBPP grouping-keyword recipe | pass | implemented | native QA | native QA | P0 |
| Project statistics and CSV/JSON export | pass | implemented | native QA | native QA | P0 |
| `.astroforge` open/save/autosave/recovery | pass | implemented | native QA | native QA | P0 |
| Verified project export, preflight, pause/resume/cancel | pass | implemented | native QA | native QA | P0 |
| Quality series separated by filter/configuration session | pass | implemented | native QA | native QA | P0 |
| Quality metrics, robust threshold and sortable table | pass | implemented | native QA | native QA | P0 |
| Quality distribution, clickable frame selection | pass | implemented | native QA | native QA | P0 |
| Debayer/channel-balanced stretch, zoom and Blink | pass | implemented | native QA | native QA | P0 |
| Non-destructive exclusions and reveal in file manager | pass | implemented | native QA | native QA | P0 |
| Standalone Master Library Lab and editable metadata | pass | implemented | native QA | native QA | P0 |
| Camera-first Master Library organization | pass | shared Core | automated | automated | P0 |
| Transactional organization, hash verification, rollback | pass | shared Core | automated | automated | P0 |
| Settings persistence, diagnostics and privacy-safe bundle | pass | implemented | native QA | native QA | P0 |
| First-run onboarding linked to real source/library state | pass | implemented | native QA | native QA | P0 |
| Signed platform-aware application updates | Windows only | pending redesign | blocked | blocked | commercial |
| Responsive panels at 980–2560 px and HiDPI | pass | implemented | native QA | native QA | P0 |
| Keyboard navigation and screen-reader names | partial | partial | blocked | blocked | P0 |
| English UI/localization | partial | partial | blocked | blocked | P0 |
| Linux self-contained x64/ARM64 package | n/a | implemented | native QA | n/a | P0 |
| macOS Intel/Apple Silicon `.app` bundle | n/a | implemented | n/a | native QA | P0 |
| macOS signing, hardened runtime and notarization | n/a | prepared only | n/a | blocked | commercial |
| Linux desktop entry, icon and AppImage/deb packaging | n/a | pending | blocked | n/a | commercial |

## Required native scenarios

Each architecture must run these tests against disposable fixtures and at least one real,
redacted project:

1. Add individual files and folders, analyze, close, reopen and restore state.
2. Confirm session grouping across midnight and Flat Epoch manual relinking.
3. Open every workspace at 100%, 150% and 200% scaling; verify no clipped action.
4. Run one Quality series, sort every metric, click chart outliers, zoom, Blink and exclude.
5. Scan and normalize a copied Master Library; verify camera-first paths and rollback.
6. Build a project, run preflight, interrupt export, resume and compare all SHA-256 hashes.
7. Reveal files in Finder or the Linux file manager and export a privacy-safe support ZIP.
8. Launch the packaged artifact from a clean standard-user account with no .NET SDK.

## Publication rule

CI compilation is necessary but not sufficient. A GitHub release is allowed only after the
four architecture jobs pass and the two native QA columns contain no `blocked` P0 row.
