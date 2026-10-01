# Guida rapida

AstroProject Forge prepara un progetto ordinato per PixInsight WeightedBatchPreprocessing senza modificare i file originali.

**Prima volta?** Nella configurazione iniziale scegli **Prova con il progetto demo** (oppure **Menu → Apri il progetto demo**). Forge crea due notti sintetiche della Cygnus Loop (ASIAIR e N.I.N.A.) con Flat e libreria Master, le analizza e ti accompagna in un tour di tutte le schermate sui controlli reali. Premi **F1** su qualsiasi schermata per rivedere la sua parte del tour, **Maiusc+F1** per la guida online.

La finestra ha quattro passi, sempre in ordine, e un solo pulsante in basso a destra che fa la prossima cosa che serve al progetto. Sopra la barra in basso, una riga dice perché.

## 1. Importa

Nella pagina **Importa** trovi due schede:

- **Libreria Master**: collega una volta sola una o più cartelle con Master Dark e Master Bias. Restano salvate per ogni progetto. L’app cerca la combinazione corretta usando camera, dimensioni, binning, Gain, Offset, temperatura, readout ed esposizione. Se non hai Dark o Bias puoi saltare questo passaggio.
- **Acquisizioni**: aggiungi cartelle o singoli file FITS/XISF. I file possono provenire da N.I.N.A., ASIAIR, SGP, Voyager, SharpCap o qualsiasi software che scriva metadati utili negli header.

In **Opzioni avanzate** trovi i valori di riserva, usati soltanto quando header e percorso non contengono l’informazione, e l’ora in cui cambia la notte.

## 2. Analizza

Premi **Analizza**: l’analisi prosegue da sola al passo successivo. La mappa viene ordinata così:

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

## 3. Controlla lo strumento

La pagina **Strumento** mostra camera, ottica, riduttore, pixel e filtri letti dagli header e dai nomi file. Se qualcosa non è giusto lo cambi tu: premi **Cambia** accanto alla voce, oppure tocca una parte del treno ottico. Anche i filtri già riconosciuti si possono cambiare, scegliendo quello giusto dal catalogo. Ciò che scegli resta salvato per quella camera in ogni progetto.

Se indichi una camera diversa da quella degli header, tutti i frame di quella camera (Light, Flat e Master) vengono letti con il nome scelto: così Dark e Bias si abbinano anche quando il software di acquisizione ha scritto un nome inutile.

## 4. Controlla le calibrazioni

Apri **Calibrazioni**. Correggi solo gli elementi segnalati, una scelta alla volta. Puoi assegnare manualmente un Flat Set a un file, a più notti o a un’intera sessione.

## 5. Esporta

Nella pagina **Esporta** dai un nome al progetto, scegli la destinazione e premi **Esporta**. Se la cartella contiene già un progetto gestito, vengono copiati soltanto i file nuovi. Gli invariati non vengono duplicati e i conflitti vengono bloccati.

## 6. Imposta WBPP

Nella pagina **Esporta**, dopo l’esportazione, trovi **PixInsight WBPP**:

Puoi selezionare **Apri in PixInsight**: l’app genera un’istanza `.xpsm` con file, Master, keyword e cartella risultati già impostati. La tabella delle keyword resta disponibile per il controllo manuale.

In alternativa:

1. in PixInsight WBPP attiva **Grouping Keywords**;
2. aggiungi soltanto le keyword mostrate;
3. imposta **Pre = ON** e **Post = OFF**;
4. prima di Run, controlla in **Calibration** che ogni gruppo Light abbia Flat, Dark e Bias previsti.

Non usare `DATE-OBS`: può dividere i file della stessa notte.

## 7. Controlla la qualità, se vuoi

**Qualità dei frame** (in **Strumenti**, in alto a destra) è facoltativa. Confronta separatamente ogni filtro e sessione, mostra FWHM, eccentricità, rumore, SNR e stelle, permette Blink e sposta i file esclusi in un’area separata. Gli originali restano intatti. L’app propone i sospetti; la decisione di escluderli resta sempre manuale.

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
