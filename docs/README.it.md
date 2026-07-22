# AstroProject Forge

**Italiano** · [English](../README.md)

**Trasforma acquisizioni FITS/XISF multisessione in un progetto PixInsight WBPP verificabile.**

AstroProject Forge è un workspace desktop per la calibrazione astrofotografica. Legge gli header, ricostruisce notti astronomiche e sessioni di configurazione ottica, abbina Flat/Dark/Bias, rende visibili le ambiguità ed esporta una struttura verificata con le Grouping Keywords WBPP realmente necessarie.

> Software beta. Prima di elaborare dati importanti verifica sempre le assegnazioni di calibrazione. Le immagini sorgenti restano in sola lettura.

![Mappa gerarchica di AstroProject Forge](images/project-map.png)

## Perché serve

Una lunga integrazione non è una semplice cartella. Lo stesso target può attraversare filtri diversi, notti oltre la mezzanotte, rotazioni della camera, pulizie delle ottiche, variazioni di Gain o temperatura e più generazioni di Flat. Raggruppare soltanto per data o filtro può calibrare silenziosamente i Light sbagliati.

Forge rappresenta il progetto in modo esplicito:

```text
Filtro
└── Sessioni di configurazione
    └── Sessione
        ├── Notti astronomiche
        ├── Flat Set / Flat Epoch
        ├── Master Dark
        └── Master Bias
```

L’automazione viene usata quando le prove sono solide. I file ambigui restano visibili e modificabili. Un Flat Set può essere collegato manualmente a un Light, a più notti oppure a un’intera sessione di configurazione.

## Funzioni principali

- FITS e XISF prodotti da N.I.N.A. o da qualunque software che scriva header utilizzabili;
- confine della notte astronomica configurabile, così i file dopo mezzanotte restano con la sera precedente;
- metadati header-first con fallback da nome/percorso e provenienza sempre visibile;
- matching motivato di Flat, Dark e Bias su più Master Library con priorità;
- inventario Master camera-first, completamento metadati, normalizzazione e copia verificata;
- override non distruttivi sul singolo file o su gruppi;
- integrazione per filtro, sessione di configurazione e notte;
- Grouping Keywords PixInsight WBPP adattive (`FLATSET`, `DARKSET`, `BIASSET`, `TARGET`);
- analisi Qualità opzionale con FWHM, eccentricità, rumore, SNR, stelle, outlier robusti, Blink ed esclusione sicura;
- export riprendibile con preflight, verifica SHA-256, manifest e report;
- diagnostica locale e support bundle senza pixel delle immagini.

## Dati di acquisizione

![Dashboard con 58 ore su due filtri](images/acquisition-dashboard.png)

La dashboard rende il dataset misurabile: integrazione totale, copertura per filtro, sessioni, notti, Gain, temperatura e stato delle calibrazioni sono visibili prima di aprire PixInsight.

## Libreria Master

![Libreria Master con inventario organizzato per camera](images/master-library-lab.png)

La Libreria Master è indipendente dall’analisi del progetto. Può scansionare Dark e Bias senza caricare Light, chiedere soltanto i metadati non dimostrabili, mostrare una struttura normalizzata che parte dalla camera e creare copie verificate senza toccare gli originali.

Struttura consigliata:

```text
MasterLibrary/
└── Camera-ZWO-ASI2600MC/
    └── Gain-100/
        └── Offset-51/
            └── Temp--10C/
                ├── Dark/
                │   ├── masterDark_300s.xisf
                │   └── masterDark_600s.xisf
                └── Bias/
                    └── masterBias.xisf
```

I nomi esatti non sono obbligatori. Gli header sono autorevoli; cartelle e nomi file sono prove di fallback. I Master affidabili dovrebbero indicare camera/sensore, geometria e binning, Gain, Offset, setpoint, esposizione Dark, readout mode, tipo di frame e stato Master.

## Download

La prima beta pubblica è distribuita tramite [GitHub Releases](https://github.com/astropuzzo/astroproject-forge/releases).

| Piattaforma | Pacchetto | Note |
|---|---|---|
| Windows 10/11 x64 | Installer `.exe` o portable `.zip` | Build primaria di QA |
| Linux x64 / ARM64 | `.deb` o portable `.tar.gz` | X11/XWayland; il `.deb` dichiara le dipendenze native |
| macOS 13+ Intel / Apple Silicon | `.dmg` o `.zip` | Beta firmata ad-hoc; notarizzazione pianificata |

I pacchetti pubblicati sono self-contained: non serve installare .NET. PixInsight non è necessario per inventariare o organizzare il progetto.

L’interfaccia dell’app è disponibile in italiano e inglese. La lingua si cambia in qualsiasi momento da **Impostazioni → Lingua / Language**; la modifica è immediata e viene ricordata al riavvio.

Ogni release GitHub contiene soltanto i pacchetti installabili e portable elencati sopra. GitHub aggiunge automaticamente gli archivi ZIP e TAR.GZ del `Source code`. Report QA, manifest, checksum e SBOM restano artefatti interni della build e non affollano i download pubblici.

## Avvio rapido

1. Aggiungi file o cartelle contenenti Light e Flat.
2. Aggiungi una o più Master Library Dark/Bias e impostane la priorità.
3. Usa i fallback di progetto soltanto per metadati assenti da header e percorsi.
4. Avvia l’analisi e risolvi avvisi o collegamenti Flat manuali.
5. Controlla Grouping Keywords WBPP e struttura finale.
6. Scegli la destinazione ed esporta il progetto verificato.
7. Carica la struttura generata in PixInsight WeightedBatchPreprocessing.

## Modello di sicurezza

- Gli originali non vengono mai riscritti dall’analisi o dall’export.
- Le esclusioni spostano copie verificate in un’area separata; non cancellano i sorgenti.
- L’export controlla file mancanti, collisioni, spazio libero, sovrapposizioni, path traversal, percorsi lunghi, reparse point e operazioni interrotte.
- La normalizzazione Master scrive i metadati soltanto sulle nuove copie e le verifica prima di completare.

## Compilazione dal sorgente

Requisito: [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet run --project dotnet/AstroForge.Core.Tests/AstroForge.Core.Tests.csproj -c Release
dotnet build dotnet/AstroForge.App/AstroForge.App.csproj -c Release
dotnet build dotnet/AstroForge.CrossPlatform/AstroForge.CrossPlatform.csproj -c Release
```

Windows usa il workspace WPF. Linux e macOS usano la shell Avalonia sopra lo stesso Core e lo stesso modello applicativo condiviso. La parità multipiattaforma è tracciata in [CROSS_PLATFORM_PARITY.md](CROSS_PLATFORM_PARITY.md).

## Stato del progetto

Il repository è pubblico per un beta test trasparente. Il software è ancora in sviluppo: firma/notarizzazione, matrice WBPP end-to-end e supporto commerciale restano gate di rilascio. Vedi [piano ready-to-sell](PIANO_READY_TO_SELL.md), [specifica prodotto](PRODUCT_SPEC.md), [changelog](CHANGELOG.md) e [processo di release](RELEASE_PROCESS.md).

## Copyright e contributi

Copyright © 2026 AstroProject Forge. Tutti i diritti riservati. L’accesso pubblico al sorgente non concede il diritto di copiare, redistribuire, modificare o vendere il software. Vedi [LICENSE](../LICENSE) e [CONTRIBUTING](../CONTRIBUTING.md).
