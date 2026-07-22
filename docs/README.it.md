# AstroProject Forge

**Italiano** · [English](../README.md)

**Trasforma acquisizioni FITS/XISF multisessione prodotte da qualunque software in un progetto pronto
per PixInsight WBPP, senza organizzare centinaia di file a mano.**

AstroProject Forge è un'applicazione desktop in sviluppo per Windows, Linux e macOS che legge i metadati
FITS/XISF, ricostruisce sessioni osservative e configurazioni del treno ottico,
abbina Flat, Dark e Bias e genera una struttura verificabile per
WeightedBatchPreprocessing.

> Il progetto è in sviluppo attivo e non è ancora una release commerciale.
> Correttezza delle calibrazioni e sicurezza degli originali hanno precedenza
> sulla quantità di funzioni.

> Windows resta la build privata di riferimento. Linux e macOS usano lo stesso
> Core e lo stesso modello applicativo, ma non saranno pubblicati finché non
> superano la [matrice di parità](CROSS_PLATFORM_PARITY.md); non esisterà una
> versione ridotta del programma.

![Mappa progetto di AstroProject Forge](images/project-map.jpg)

## Il problema che risolve

Un target debole può richiedere settimane di acquisizione. Nello stesso progetto
possono esserci più filtri, notti che attraversano la mezzanotte, cambi del treno
ottico e diversi set di Flat. Una semplice divisione per data non basta:

- sei notti HOO possono condividere lo stesso Flat Set;
- dopo un cambio filtro o rotazione serve una nuova configurazione;
- tornando a HOO, i nuovi Flat non devono calibrare le vecchie sessioni;
- pulizia di specchio o filtro può creare un'altra Flat Epoch anche senza
  cambiare il nome del filtro;
- Dark e Bias devono corrispondere a camera, geometria, Gain, Offset,
  temperatura, esposizione e readout mode.

AstroProject Forge costruisce una mappa esplicita del progetto e segnala ciò che
non può dimostrare, invece di inventare un abbinamento.

## Come funziona

```mermaid
flowchart LR
    A["File o cartelle<br/>FITS / XISF"] --> B["Lettura prioritaria header<br/>e validazione"]
    L["Librerie Master<br/>Dark / Bias"] --> B
    B --> C["Filtro → sessione ottica<br/>→ notte astronomica"]
    C --> D["Matching Flat / Dark / Bias<br/>con motivazioni"]
    D --> E["Revisione e link manuali<br/>non distruttivi"]
    E --> F["Struttura progetto<br/>+ ricetta WBPP"]
    F --> G["Copia verificata SHA-256<br/>+ manifest e report"]
```

1. Selezioni una o più cartelle di acquisizione e le librerie Master.
2. L'app legge solo gli header: i pixel non vengono caricati durante l'analisi.
3. Le immagini vengono organizzate come
   `Filtro → Sessioni di configurazione → Notti / Flat / Master`.
4. Il motore propone le calibrazioni compatibili e spiega errori o ambiguità.
5. Puoi correggere metadati e collegare manualmente un Flat Set a una notte, a
   più notti o a un'intera sessione.
6. L'app suggerisce le Grouping Keywords WBPP necessarie, inclusi valori
   `Pre/Post` per `FLATSET`, `DARKSET`, `BIASSET` e `TARGET`.
7. `Esporta progetto` costruisce il piano quando serve ed esegue automaticamente
   i controlli di sicurezza prima della copia riprendibile e verificata SHA-256.
   L'anteprima della struttura resta disponibile, ma non è un passaggio imposto.
8. Il progetto completato include manifest versionato, report del preflight,
   statistiche, ricetta WBPP e report di validazione leggibile.

## Dentro l'app

La dashboard trasforma il progetto in dati osservativi utili: integrazione
totale, ore per filtro, sessioni di configurazione, notti astronomiche, Gain,
temperatura e copertura delle calibrazioni sono leggibili senza aprire un foglio
di calcolo.

Quality Lab è un ambiente opzionale di analisi dei pixel. Misura FWHM,
eccentricità, rumore di fondo, rapporto segnale/rumore e stelle rilevate, quindi
confronta soltanto frame della stessa sessione di configurazione e della stessa esposizione. La soglia in σ è
regolabile e la distribuzione mostra curva di riferimento, soglia e singoli
frame cliccabili. Ogni filtro e sessione di configurazione/Flat Set ha un'analisi
separata; le metriche sono ordinabili in entrambi i versi. Selezione multipla,
Blink, stretch asinh e debayer temporaneo con bilanciamento automatico dei canali
servono all'ispezione senza modificare gli originali.
Sorgenti e Inspector sono ridimensionabili e ricordano la larghezza scelta; l'Inspector
compare soltanto nelle aree che possono applicare override. Il
Quality Lab ha uno splitter tabella/preview, zoom al cursore, pan trascinabile,
adattamento alla finestra e dettaglio temporaneo fino a 2400 px.

![Dashboard con integrazione per filtro](images/acquisition-dashboard.jpg)

Master Library Lab è un ambiente separato, non l'ultimo passaggio obbligatorio
del progetto. Può inventariare le librerie Dark/Bias abilitate anche senza Light,
completare i metadati mancanti e mostrare in anteprima una struttura normalizzata
che parte dalla camera prima di creare qualunque copia verificata.

![Inventario di Master Library Lab](images/master-library-lab.jpg)

## Funzioni implementate

### Project Intelligence

- parser FITS e XISF;
- diagnostica locale privacy-safe con codici errore ricercabili, operazioni correlate, Centro diagnostica interno e pacchetto ZIP;
- classificazione Light, Flat, Dark, Bias e Dark-flat;
- notte astronomica configurabile: i file dopo mezzanotte possono restare nella
  sessione della sera precedente;
- Flat Epoch automatiche e link manuale multisessione;
- albero gerarchico invece di una lista infinita di file;
- override singoli e di gruppo con provenienza del valore;
- dashboard con ore per filtro, sessione e notte;
- intervalli temporali, Gain, temperatura e copertura calibrazioni;
- esportazione statistiche CSV e JSON.
- workspace desktop nativo con pannelli Sorgenti e
  Inspector responsivi, stati vuoti contestuali, movimento sobrio e gerarchia
  visiva progettata per le decisioni di calibrazione;

### PixInsight WBPP

- matching motivato di Flat, Dark e Bias;
- preferenza per Master provenienti dalla libreria configurata;
- ricetta adattiva delle Grouping Keywords;
- anteprima della struttura finale;
- guida WBPP generata insieme al progetto;
- manifest con assegnazioni e decisioni utilizzate.

### Sicurezza e ripresa

- gli originali restano in sola lettura;
- controlli di sicurezza automatici durante l'export, senza un passaggio obbligatorio aggiuntivo;
- preflight di sorgenti mancanti o illeggibili, spazio e riserva configurabile,
  sovrapposizione sorgente/destinazione, progetto esistente, duplicati, path
  traversal, percorsi lunghi, junction, dischi rimovibili e rete;
- staging riprendibile con verifica SHA-256, pausa, riprendi, annulla, velocità ed ETA;
- secondo preflight subito prima dell'esecuzione e report scritti atomicamente;
- file progetto portabile `.astroforge` con salvataggio atomico;
- autosalvataggio dopo il primo salvataggio esplicito;
- recovery journal atomico con scelta esplicita Ripristina/Ignora dopo un'interruzione;
- cache incrementale degli header con invalidazione dei soli file modificati;
- comando `Pulisci cache` che non elimina immagini astronomiche.
- Master Library Lab con compilazione guidata dei metadati mancanti, nuova
  struttura normalizzata, keyword FITS/XISF sulle sole copie, verifica SHA-256 e
  manifest finale.

## Struttura attesa

```text
HOO
└── Sessioni
    ├── Sessione 01 · 15–28 giu
    │   ├── Notti osservative
    │   ├── Flat Set collegato
    │   ├── Master Dark
    │   └── Master Bias
    └── Sessione 02 · 02–05 lug
        └── ...
SIOIII
└── Sessioni
    └── ...
Senza filtro
└── Sessioni sensore
    ├── Dark
    └── Bias
```

Questa gerarchia distingue la **notte di calendario** dalla **sessione
astronomica** e dalla **sessione di configurazione ottica**. Sono concetti
diversi e non devono essere ridotti tutti a `DATE-OBS`.

## Stato della roadmap

| Area | Stato |
|---|---|
| Analisi FITS/XISF e albero multisessione | Operativa |
| Flat Epoch e link manuali | Operativa |
| Dashboard e statistiche | Operativa |
| File progetto `.astroforge` | Operativo, migrazioni da completare |
| Cache incrementale header | v1 operativa, backend SQLite pianificato |
| Coda di revisione guidata | In sviluppo |
| Gestore multi-libreria | v1 operativo: priorità e stato online/offline |
| Export antifragile | v1 operativo: controlli automatici, pausa/annulla/riprendi, SHA-256 e report atomici |
| Quality Lab opzionale | v1 operativo: FWHM, eccentricità, rumore, SNR, stelle, outlier, Blink ed esclusione non distruttiva |
| Installer, firma e aggiornamenti | Pianificato |
| Matrice WBPP end-to-end | Da completare prima della vendita |

Il backlog completo, con criteri di accettazione, è in
[PIANO_READY_TO_SELL.md](docs/PIANO_READY_TO_SELL.md).

## Prerequisiti e Master Library

Per usare l'app servono le cartelle contenenti i Light e i relativi Flat. Una
Master Library di Dark e Bias è fortemente consigliata, ma il percorso non è
codificato nel programma e può essere scelto dall'utente.

La struttura ideale rende leggibili almeno Gain, temperatura ed esposizione:

```text
MasterLibrary/
├── Camera-ZWO-ASI2600MC/
│   ├── Gain-100/
│   │   ├── Offset-50/
│   │   │   ├── Temp--10C/
│   │   │   │   ├── Dark/
│   │   │   │   │   ├── masterDark_60s.xisf
│   │   │   │   │   ├── masterDark_300s.xisf
│   │   │   │   │   └── masterDark_600s.xisf
│   │   │   │   └── Bias/
│   │   │   │       └── masterBias.xisf
│   │   │   └── Temp-0C/
│   │   │       └── ...
│   │   └── Offset-51/
│   │       └── ...
│   └── Gain-0/
│       └── ...
└── Camera-Secondaria/
    └── ...
```

Non è necessario usare esattamente questi nomi. L'app prova prima gli header
FITS/XISF e usa cartelle e nome file solo come fallback. Per un matching
affidabile i Master dovrebbero dimostrare:

- camera/sensore e geometria;
- binning e ROI;
- Gain e Offset;
- temperatura di setpoint;
- esposizione per i Dark;
- readout mode, se la camera ne offre più di una;
- stato Master e tipo frame.

Se un Master non contiene Gain o Offset, è possibile impostare un default di
progetto o un profilo della libreria. Un valore predefinito viene applicato solo
quando il metadato manca e non sostituisce mai un header valido. File duplicati,
campi contraddittori o candidati equivalenti vengono inviati alla Coda di
revisione.

## Sviluppo

### Requisiti

- Windows 10/11 per la build di test attuale;
- Linux x64/ARM64 o macOS 13+ Intel/Apple Silicon per le build di parità non ancora pubblicate;
- su Linux: desktop X11/XWayland e `libgbm1`, `libgl1`, `libegl1`, `libinput10` (il pacchetto `.deb` li dichiara);
- gli eseguibili pubblicati saranno self-contained: il runtime .NET non sarà richiesto all'utente;
- .NET SDK 10 per compilare;
- PixInsight non è necessario per analizzare o organizzare i file.

```powershell
dotnet run --project dotnet/AstroForge.App/AstroForge.App.csproj
.\qa-gate.ps1
.\scripts\verify-cross-platform-parity.ps1
```

### Test

I test usano fixture sintetiche e non richiedono scatti astronomici personali.

```powershell
dotnet run --project dotnet/AstroForge.Core.Tests/AstroForge.Core.Tests.csproj -c Release
```

### Build Windows autonoma e distribuzione Beta

```powershell
.\build-release.ps1
.\build-distribution.ps1
.\scripts\package-cross-platform.ps1
```

## Principi del progetto

- Nessun abbinamento scientifico senza una motivazione verificabile.
- Un dato mancante resta mancante finché una regola o l'utente non lo risolve.
- Le correzioni sono overlay: gli header originali non vengono riscritti.
- Le operazioni distruttive non devono essere confuse con pulizia cache o
  rimozione dalla memoria.
- Nessun FITS/XISF personale o artefatto di build viene versionato nel
  repository.

## Licenza e distribuzione

L'installer x64 per utente, l'identità Stable/Beta, il manifest aggiornamenti,
lo SBOM e gli hash di integrità sono implementati. La pubblicazione commerciale
resta bloccata finché EXE e installer non saranno firmati Authenticode, non sarà
completata la matrice su VM Windows 10/11 pulite e non sarà regolarizzata la
licenza commerciale del tool di distribuzione. Il repository resta privato in
pre-release. Il processo completo è in [RELEASE_PROCESS.md](RELEASE_PROCESS.md).
