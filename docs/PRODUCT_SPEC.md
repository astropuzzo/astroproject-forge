# AstroProject Forge — specifica di prodotto

## Obiettivo

Ridurre un insieme eterogeneo di acquisizioni astronomiche multisessione a un
progetto controllabile, ripetibile e pronto per PixInsight WBPP. Il programma
deve funzionare con FITS standard prodotti da N.I.N.A. e da altre applicazioni,
senza dipendere da una specifica marca di camera.

## Flusso utente

1. L'utente seleziona una o più cartelle di acquisizione e la libreria Master.
2. Il programma legge solo gli header e presenta l'inventario per target,
   sessione, filtro, esposizione e configurazione strumentale.
3. Un pannello diagnostico evidenzia errori, conflitti e dati insufficienti.
4. Il programma propone gli abbinamenti Light/Flat/Dark/Bias con motivazione e
   livello di confidenza. L'utente risolve solo i casi ambigui.
5. Una simulazione mostra cartelle, file, spazio richiesto e configurazione WBPP.
6. L'esportazione copia e verifica i file, quindi crea manifest, report e guida
   WBPP specifica per quel progetto.

## Fonti dei metadati

Ordine di autorità:

1. header FITS;
2. regole esplicite definite dall'utente;
3. percorso e nome file;
4. valore inferito, sempre marcato come tale.

Una divergenza tra header e nome non viene corretta in silenzio. Diventa una
diagnostica verificabile.

N.I.N.A. documenta, tra gli altri, `IMAGETYP`, `EXPTIME`/`EXPOSURE`, `DATE-LOC`,
`DATE-UTC`, `OBJECT`, `INSTRUME`, `XBINNING`, `YBINNING`, `GAIN`, `OFFSET`,
`SET-TEMP`, `CCD-TEMP`, `READOUTM`, `BAYERPAT`, `FILTER`, `FOCALLEN` e
`ROTATOR`/`ROTATANG`.

## Identità e raggruppamenti

### Sessione

La sessione predefinita deriva dall'istante di acquisizione traslato di 12 ore:
le riprese prima e dopo mezzanotte appartengono alla stessa notte osservativa.
L'utente può unire o dividere sessioni senza alterare i FITS.

### Configurazione sensore

Camera, dimensioni immagine, binning, gain, offset, temperatura, readout mode e
schema Bayer. Le tolleranze numeriche sono configurabili e mai applicate senza
essere riportate.

### Configurazione ottica per i Flat

Camera, dimensioni, binning, filtro, readout mode, schema Bayer, angolo rotatore
e focale quando disponibili. Una sessione non basta a provare che un Flat sia
compatibile: l'abbinamento usa la firma ottica e un gruppo Flat esplicito.

## Regole di calibrazione iniziali

- Dark → Light: camera, geometria, binning, gain, offset e readout identici;
  esposizione e temperatura entro tolleranza.
- Bias → Light/Flat: camera, geometria, binning, gain, offset e readout identici.
- Flat → Light: filtro, camera, geometria, binning, readout e Bayer identici;
  rotatore/focale coerenti se presenti; gruppo ottico approvato.
- Dark-flat → Flat: stessa regola sensore del Dark, con esposizione del Flat.
- Un Master deve essere riconosciuto come tale dall'header o da una regola
  esplicita. Il solo prefisso del nome produce confidenza ridotta.

Ogni regola può produrre: `exact`, `within_tolerance`, `ambiguous`, `missing`,
`incompatible`.

## Diagnostiche minime

- FITS illeggibile o header non terminato;
- tipo frame sconosciuto;
- Light senza target, filtro, esposizione, gain o binning;
- Flat senza filtro;
- temperatura reale lontana dal setpoint;
- valori X/Y del binning discordanti;
- filtri che differiscono solo per maiuscole, spazi o alias;
- header e nome file in conflitto;
- più Master equivalenti o nessun Master compatibile;
- possibile cambio di rotazione, camera, readout, geometria o focale;
- file duplicati e collisioni di destinazione.

## Struttura progetto proposta

```text
Target/
  _AstroForge/
    manifest.json
    validation-report.html
    wbpp-guide.md
  Light/
    FILTER_Ha/FLATSET_<id>/DARKSET_<id>/BIASSET_<id>/SESSION_2026-06-24/...
  Flat/
    FILTER_Ha/FLATSET_<id>/BIASSET_<id>/...
  Dark/Masters/DARKSET_<id>/...
  Bias/Masters/BIASSET_<id>/...
  DarkFlat/FLATSET_<id>/...
```

I nomi sono sanificati per Windows e conservano il nome originale nel manifest.
Il manifest registra provenienza, destinazione, hash, header normalizzati,
decisioni automatiche e override dell'utente.

## Integrazione WBPP

La guida generata deve elencare i gruppi trovati e indicare esattamente le
keyword `FLATSET`, `DARKSET` e `BIASSET`, tutte in modalità PRE. Il concetto
chiave è separare:

- raggruppamento di pre-calibrazione, per associare ogni Light ai propri Flat;
- raggruppamento post-calibrazione, normalmente disattivato per la sessione in
  modo da integrare insieme tutte le notti dello stesso filtro;
- raggruppamento post-calibrazione mantenuto per pannelli mosaico o serie che
  devono restare separate.

L'applicazione non deve proporre una stringa WBPP finché non ha verificato che i
nomi prodotti siano univoci e rappresentino tutti i gruppi del manifest.

## Project Intelligence e statistiche

Il progetto deve mostrare e poter esportare:

- tempo totale integrato;
- tempo, Light, notti e sessioni per filtro;
- esposizione media per notte;
- intervallo temporale del progetto;
- Gain e intervallo temperature;
- numero di Flat e Master effettivamente collegati;
- calibrazioni irrisolte per filtro e sessione;
- dettaglio di ogni notte osservativa;
- file CSV e JSON inclusi nel progetto esportato.

I conteggi derivano dagli header effettivi dopo gli override. Le notti non sono
Grouping Keywords WBPP: restano sotto la sessione di configurazione e vengono
integrate insieme quando filtro e configurazione lo consentono.

## Sicurezza operativa

La prima release supporta solo copia verificata. La procedura scrive in una
directory temporanea, verifica dimensione e SHA-256, rinomina atomicamente e
solo dopo aggiorna il manifest. Un'esportazione interrotta è riprendibile.
Lo spostamento potrà essere abilitato come operazione separata soltanto dopo la
verifica completa della copia e con conferma esplicita.

## Criteri di completamento

- scansione ricorsiva reattiva su librerie reali;
- compatibilità verificata su FITS N.I.N.A. e almeno un secondo produttore;
- abbinamenti corretti su dataset multisessione e multifiltro noto;
- nessuna modifica o perdita degli originali nei test di interruzione;
- report delle ambiguità comprensibile e risolvibile dalla GUI;
- progetto accettato da WBPP con Flat e Master corretti;
- installer Windows firmabile e procedura di aggiornamento definita.
