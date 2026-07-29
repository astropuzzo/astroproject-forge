# Piano ready to sell

Aggiornato al 28 luglio 2026. Questo documento raccoglie soltanto lavoro concreto e
verificabile. Le idee già realizzate non restano mischiate alle attività aperte.

## Stato del prodotto

AstroProject Forge dispone già del flusso completo: import FITS/XISF generico,
ricostruzione delle notti oltre la mezzanotte, sessioni di configurazione, associazione
Flat/Dark/Bias, librerie multiple per camera, Quality Lab, progetto WBPP, esportazione
verificata, salvataggio `.astroforge`, installer e aggiornamento automatico Windows.
La stessa logica Core è usata dall'interfaccia Avalonia per Linux e macOS.

## Milestone corrente — affidabilità dell'interfaccia

- [x] Traduzione inglese completa delle stringhe statiche WPF e Avalonia.
- [x] Controllo automatico che blocca nuove stringhe prive di traduzione.
- [x] Scorciatoie coerenti: apri, salva, salva con nome, analizza, menu, guida e workspace.
- [x] Salvataggio diretto del progetto già aperto; `Salva con nome` resta esplicito.
- [x] Nomi e descrizioni per screen reader sui controlli principali.
- [x] Focus tastiera ad alto contrasto.
- [x] Avanzamento visibile per download e installazione degli aggiornamenti Windows.
- [x] Setup rapido operativo al primo avvio, riapribile dal menu.
- [x] Gestione Master Library evidenziata nel pannello Sorgenti.
- [ ] Test manuale completo con Narrator su Windows.
- [ ] Test manuale tastiera, VoiceOver e Orca sui pacchetti nativi.

## P0 — prima di una vendita commerciale

- [ ] Validare su dati reali la matrice di associazione WBPP: mono, OSC, binning,
      esposizioni miste, Dark Flat e sensori senza Bias.
- [ ] Aggiungere test end-to-end con fixture FITS/XISF pubblicabili e risultati attesi.
- [ ] Collaudare installer e aggiornamento Windows su account standard, percorso
      personalizzato, spazio insufficiente, rete interrotta e rollback.
- [ ] Eseguire la matrice HiDPI 100/150/200% e finestre 980–2560 px su tutti i sistemi.
- [ ] Completare una beta hardware con almeno tre camere e più software di acquisizione.
- [ ] Definire licenza commerciale, privacy, termini di supporto e politica di rimborso.
- [ ] Ottenere firma Windows e Apple notarization quando il budget lo consente.

## P1 — upgrade ad alto valore

- [ ] Dashboard copertura Master: combinazioni camera/gain/offset/temperatura/esposizione
      mancanti, obsolete o duplicate.
- [ ] Profili strumento: camera, ruota filtri, treno ottico e intervalli di validità
      dichiarati dall'utente.
- [ ] Timeline degli eventi ottici: rotazione camera, pulizia, cambio filtro o adattatore;
      ogni evento propone automaticamente un nuovo Flat Set.
- [ ] Aggiornamento incrementale: importare una nuova notte in un progetto esistente senza
      ricalcolare file invariati.
- [ ] Obiettivi di integrazione per filtro con avanzamento, deficit e previsione notti.
- [ ] Cache Quality Lab invalidata da hash, per non rimisurare frame invariati.
- [ ] Regole qualità personalizzabili per serie e preset per focale/campionamento.
- [ ] Confronto visuale sincronizzato di due frame con stesso zoom e stretch.

## P2 — maturità operativa

- [ ] Telemetria solo opt-in, anonima e documentata per crash e prestazioni.
- [ ] Import/export di profili e impostazioni.
- [ ] Migrazione versionata dei progetti con backup automatico.
- [ ] Guida contestuale collegata allo stato reale del progetto.
- [ ] Localizzazione aggiuntiva tramite file di risorse validati dalla CI.
- [ ] Benchmark pubblici su librerie da 1.000, 10.000 e 50.000 frame.

## Regola di rilascio

Una voce è completata soltanto quando possiede codice, test automatico dove possibile e
una verifica sul sistema operativo reale. La compilazione non equivale a parità funzionale:
la matrice aggiornata è in [CROSS_PLATFORM_PARITY.md](CROSS_PLATFORM_PARITY.md).
