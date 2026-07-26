# Guida rapida

AstroProject Forge prepara un progetto ordinato per PixInsight WeightedBatchPreprocessing senza modificare i file originali.

## 1. Aggiungi i file

Apri **Sorgenti** e aggiungi cartelle o singoli file FITS/XISF. I file possono provenire da N.I.N.A., ASIAIR, SGP, Voyager, SharpCap o qualsiasi software che scriva metadati utili negli header.

## 2. Aggiungi Dark e Bias

In **Librerie di calibrazione** aggiungi una o più cartelle contenenti Master Dark e Master Bias. L’app cerca la combinazione corretta usando camera, dimensioni, binning, Gain, Offset, temperatura, readout ed esposizione.

I valori predefiniti vengono usati soltanto quando header e percorso non contengono l’informazione.

## 3. Analizza

Premi **Analizza**. La mappa viene ordinata così:

```text
Filtro
└── Sessione di configurazione
    ├── Notti osservative
    ├── Flat della sessione
    ├── Dark assegnato
    └── Bias assegnato
```

Una notte può iniziare la sera e terminare dopo mezzanotte. L’ora di cambio notte è configurabile.

## 4. Controlla i problemi

Apri **Problemi**. Correggi solo gli elementi segnalati. Puoi assegnare manualmente un Flat Set a un file, a più notti o a un’intera sessione.

## 5. Imposta WBPP

Apri **WBPP** nell’app:

1. in PixInsight WBPP attiva **Grouping Keywords**;
2. aggiungi soltanto le keyword mostrate;
3. imposta **Pre = ON** e **Post = OFF**;
4. prima di Run, controlla in **Calibration** che ogni gruppo Light abbia Flat, Dark e Bias previsti.

Non usare `DATE-OBS`: può dividere i file della stessa notte.

## 6. Controlla la qualità, se vuoi

**Qualità** è facoltativo. Analizza separatamente ogni filtro e sessione, mostra FWHM, eccentricità, rumore, SNR e stelle, permette Blink e sposta i file esclusi in un’area separata. Gli originali restano intatti.

## 7. Esporta

Apri **Esporta**, scegli nome e destinazione, genera l’anteprima e avvia l’esportazione. L’app controlla spazio, collisioni e file mancanti prima di copiare.

## Serve aiuto?

- Nell’app: **Menu → Guida rapida**
- Per un errore: **Menu → Segnala un problema**
- Per il file di supporto: **Menu → Diagnostica → Esporta ZIP di supporto**
