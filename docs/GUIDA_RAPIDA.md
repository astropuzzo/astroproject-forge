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

Gli scatti dopo mezzanotte restano con la sera precedente. Con il valore consigliato
`12`, per esempio, gli scatti del 24 giugno alle 22:30 e del 25 giugno alle 03:10
appartengono entrambi alla notte del 24 giugno.

## 4. Controlla i problemi

Apri **Calibrazioni**. Correggi solo gli elementi segnalati. Puoi assegnare manualmente un Flat Set a un file, a più notti o a un’intera sessione.

## 5. Imposta WBPP

Apri **PixInsight WBPP** nell’app:

1. in PixInsight WBPP attiva **Grouping Keywords**;
2. aggiungi soltanto le keyword mostrate;
3. imposta **Pre = ON** e **Post = OFF**;
4. prima di Run, controlla in **Calibration** che ogni gruppo Light abbia Flat, Dark e Bias previsti.

Non usare `DATE-OBS`: può dividere i file della stessa notte.

## 6. Controlla la qualità, se vuoi

**Controllo qualità** è facoltativo. Confronta separatamente ogni filtro e sessione, mostra FWHM, eccentricità, rumore, SNR e stelle, permette Blink e sposta i file esclusi in un’area separata. Gli originali restano intatti. L’app propone i sospetti; la decisione di escluderli resta sempre manuale.

## 7. Esporta

Apri **Esportazione**, scegli nome e destinazione, seleziona **Crea struttura** e poi **Esporta progetto**. Se la cartella contiene già un progetto gestito, vengono copiati soltanto i file nuovi. Gli invariati non vengono duplicati e i conflitti vengono bloccati.

## Installazione su macOS

Scarica `osx-arm64.dmg` per Apple Silicon oppure `osx-x64.dmg` per Mac Intel. Apri il
DMG e trascina l’app in **Applicazioni**. Le build gratuite non sono notarizzate:
al primo avvio macOS può bloccarle. Vai in **Impostazioni di Sistema → Privacy e
Sicurezza**, trova il messaggio relativo ad AstroProject Forge, seleziona **Apri
comunque** e conferma. Non disattivare Gatekeeper globalmente.

## Serve aiuto?

- Nell’app: **Menu → Guida rapida**
- Per un errore: **Menu → Segnala un problema**
- Per il file di supporto: **Menu → Diagnostica → Esporta ZIP di supporto**
