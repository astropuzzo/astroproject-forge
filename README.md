# AstroProject Forge

**Prepare multi-night astrophotography data for PixInsight WBPP.**

**Prepara acquisizioni astrofotografiche multisessione per PixInsight WBPP.**

[Download the latest version](https://github.com/astropuzzo/astroproject-forge/releases/latest) · [User guide](https://github.com/astropuzzo/astroproject-forge/wiki) · [Report a problem](https://github.com/astropuzzo/astroproject-forge/issues/new?template=bug_report.yml)

![Project map](docs/images/project-map.png)

## English

AstroProject Forge reads FITS and XISF metadata, reconstructs observing nights across midnight, identifies optical configuration sessions and assigns the appropriate Flat, Dark and Bias frames. The resulting project is organized for PixInsight WeightedBatchPreprocessing without rewriting the source images.

### Main features

- FITS/XISF import from N.I.N.A. or any acquisition software with usable headers;
- Filter → configuration session → observing night project structure;
- automatic and manual Flat, Dark and Bias assignment;
- multiple camera-aware Master Libraries;
- integration statistics by filter, session and night;
- project-specific WBPP Grouping Keywords;
- optional frame-quality analysis with FWHM, eccentricity, noise, SNR, Blink and non-destructive exclusions;
- instant incremental updates: registered files are not reopened, and only new files are copied;
- one-click PixInsight WBPP instance with files, masters, grouping keywords and output directory preloaded;
- Italian and English interface;
- one-click Windows updates with verified download, installation and automatic restart.

![Acquisition statistics](docs/images/acquisition-dashboard.png)

### Quick start

The main workflow has four steps:

| Step | Page | Action |
| --- | --- | --- |
| 1 | Project | Import FITS/XISF, then select **Analyze**. |
| 2 | Calibrations | Resolve only the items reported by the app. Skip this page when nothing is reported. |
| 3 | Export | Choose the destination, build the structure and export. Repeating the export adds only new files. |
| 4 | PixInsight WBPP | Create and open the ready-to-run WBPP instance. |

Statistics and Quality are optional. Master Library is an independent tool and is not a required final step.

After export, **Create and open in PixInsight** opens the populated WBPP window; it does not start processing. Check **Calibration**, then press **Run** when ready. The project also contains a `.xpsm` process icon and a `.js` launcher. To use the XPSM manually, load its process icon and apply the Script instance globally. Direct launch requires PixInsight 1.9.4 or newer in its standard installation location; tested with PixInsight 1.9.5 / WBPP 3.1.0 on Windows.

Master Libraries are saved in the application settings, not inside a project. Opening
or creating a project never removes the configured libraries.

### Keyboard

`Ctrl+O` opens a project, `Ctrl+S` saves it, `Ctrl+Shift+S` saves a copy,
`Ctrl+Enter` analyzes it, `Ctrl+,` opens the menu and `F1` opens the guide.
Use `Alt+1` through `Alt+8` to move directly between workspaces.

The suggested Master Library layout starts with the camera:

```text
MasterLibrary/
└── Camera-ZWO-ASI2600MC/
    └── Gain-100/
        └── Offset-51/
            └── Temp--10C/
                ├── Dark/
                └── Bias/
```

Folder names are not mandatory. Headers remain the primary metadata source.

## Italiano

AstroProject Forge legge i metadati FITS e XISF, ricostruisce le notti osservative oltre la mezzanotte, identifica le sessioni di configurazione ottica e assegna i corretti Flat, Dark e Bias. Il progetto risultante è pronto per PixInsight WeightedBatchPreprocessing senza modificare le immagini sorgenti.

### Funzioni principali

- importazione FITS/XISF da N.I.N.A. o qualsiasi software con header utilizzabili;
- struttura Filtro → sessione di configurazione → notte osservativa;
- assegnazione automatica e manuale di Flat, Dark e Bias;
- più Master Library organizzate per camera;
- statistiche di integrazione per filtro, sessione e notte;
- Grouping Keywords WBPP calcolate sul progetto;
- analisi qualità opzionale con FWHM, eccentricità, rumore, SNR, Blink ed esclusioni non distruttive;
- aggiornamenti incrementali immediati: i file registrati non vengono riaperti e si copiano solo quelli nuovi;
- istanza PixInsight WBPP con file, Master, keyword e cartella risultati già caricati;
- interfaccia italiana e inglese;
- aggiornamenti Windows in un clic con download verificato, installazione silenziosa e riavvio automatico.

![Master Library](docs/images/master-library-lab.png)

### Avvio rapido

Il percorso principale ha quattro passaggi:

| Passo | Pagina | Azione |
| --- | --- | --- |
| 1 | Progetto | Importa FITS/XISF e seleziona **Analizza**. |
| 2 | Calibrazioni | Risolvi soltanto ciò che viene segnalato. Se non ci sono avvisi, passa oltre. |
| 3 | Esportazione | Scegli la destinazione, crea la struttura ed esporta. Le esportazioni successive aggiungono solo i file nuovi. |
| 4 | PixInsight WBPP | Crea e apri l’istanza WBPP già configurata. |

Statistiche e Qualità sono facoltative. Master Library è uno strumento indipendente, non un passaggio finale obbligatorio.

Dopo l’esportazione, **Crea e apri in PixInsight** apre WBPP già compilato, senza avviare l’elaborazione. Controlla **Calibration**, poi premi **Run** quando sei pronto. Nel progetto trovi anche l’icona `.xpsm` e lo script di apertura `.js`. Per usare l’XPSM manualmente, carica l’icona e applica globalmente l’istanza Script. L’apertura diretta richiede PixInsight 1.9.4 o successivo nel percorso d’installazione standard; verificata su Windows con PixInsight 1.9.5 / WBPP 3.1.0.

Le Master Library sono salvate nelle impostazioni dell'app, non nel progetto. Aprire
o creare un progetto non rimuove le librerie configurate.

### Tastiera

`Ctrl+O` apre un progetto, `Ctrl+S` lo salva, `Ctrl+Shift+S` salva una copia,
`Ctrl+Invio` avvia l'analisi, `Ctrl+,` apre il menu e `F1` apre la guida.
Usa `Alt+1`–`Alt+8` per passare direttamente da un'area di lavoro all'altra.

## Downloads

| Platform | Packages |
| --- | --- |
| Windows 10/11 x64 | Installer `.exe`, portable `.zip` |
| Linux x64 / ARM64 | Package `.deb`, portable `.tar.gz` |
| macOS 13+ Intel / Apple Silicon | Disk image `.dmg`, portable `.zip` |

Packages are self-contained; .NET does not need to be installed separately. PixInsight is only required after export.

### Installing on macOS

1. Download the DMG matching your Mac: `osx-arm64` for Apple Silicon or `osx-x64` for Intel.
2. Open the DMG and drag **AstroProject Forge** to **Applications**.
3. Because the current free builds are not Apple-notarized, the first launch may be blocked. Open **System Settings → Privacy & Security**, find the AstroProject Forge message and select **Open Anyway**. Confirm once.

Do not disable Gatekeeper globally. Future updates are downloaded and hash-checked by the app, but macOS still asks for installation confirmation.

### Installazione su macOS

1. Scarica il DMG corretto: `osx-arm64` per Apple Silicon oppure `osx-x64` per Mac Intel.
2. Apri il DMG e trascina **AstroProject Forge** in **Applicazioni**.
3. Le build gratuite attuali non sono notarizzate da Apple. Se il primo avvio viene bloccato, apri **Impostazioni di Sistema → Privacy e Sicurezza**, individua il messaggio relativo ad AstroProject Forge e seleziona **Apri comunque**. Conferma una sola volta.

Non disattivare globalmente Gatekeeper. Gli aggiornamenti vengono scaricati e verificati dall’app, ma macOS richiede comunque la conferma d’installazione.

## Build

Requires [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet run --project dotnet/AstroForge.Core.Tests/AstroForge.Core.Tests.csproj -c Release
dotnet build dotnet/AstroForge.App/AstroForge.App.csproj -c Release
dotnet build dotnet/AstroForge.CrossPlatform/AstroForge.CrossPlatform.csproj -c Release
```

Windows uses WPF. Linux and macOS use Avalonia over the same Core and application model.

## Project

Created by [Gianmarco Spagnoli (@astropuzzo)](https://github.com/astropuzzo).

Copyright © 2026 Gianmarco Spagnoli. Official binaries are free for personal, non-commercial use. See [LICENSE](LICENSE).
