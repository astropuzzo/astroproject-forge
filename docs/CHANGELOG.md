# AstroProject Forge — note di rilascio

## Unreleased - contratto soglia Quality Lab

- Linea di soglia, punti arancioni, conteggio e lista dei sospetti usano ora lo stesso score aggregato.
- Rimossa la curva gaussiana fuorviante: il grafico mostra zone operative accettata/da verificare e un istogramma coerente con la soglia.
- Aggiunti click sui punti, conteggi separati per sospetti ed esclusi, filtro tabella `Solo sospetti` e azione di esclusione con conteggio esplicito.
- Il grafico conserva sempre l'intera serie filtro/sessione anche quando la tabella viene filtrata.

## 0.9.0-beta.1 — 17 luglio 2026

- Quality Lab opzionale con lettura pixel FITS, preview Blink, FWHM, eccentricità, rumore, SNR, conteggio stelle e rilevamento robusto degli outlier per filtro/esposizione.
- Quality Lab v2 con soglia σ regolabile in tempo reale, istogramma e curva gaussiana di riferimento, punti-frame cliccabili, selezione multipla, Blink sulla selezione, stretch asinh regolabile e debayer temporaneo RGGB/BGGR/GRBG/GBRG.
- Quality Lab v2.2 separa automaticamente filtro e sessione di configurazione/Flat Set: distribuzione, soglia e outlier non mescolano più ottiche o Master diversi. Aggiunti selettore serie, ordinamento numerico bidirezionale delle metriche, grafico ad alta leggibilità e neutralizzazione RGB per-canale della preview debayerizzata.
- Inspector mostrato soltanto nei workspace che possono realmente usarlo (Analisi, Struttura e Revisione), liberando spazio a Quality Lab, Dati, WBPP e Master Library Lab.
- Corretto il bootstrap del Quality Lab: le serie compaiono immediatamente dopo l’analisi header del progetto, prima dell’analisi pixel. L’azione ora elabora soltanto la serie selezionata, conserva i risultati delle altre serie e popola la tabella progressivamente.
- Workspace ridimensionabile: splitter persistenti per Sorgenti e Inspector e splitter dedicato tra tabella e preview del Quality Lab.
- Preview astronomica navigabile con zoom 5–1600%, rotella centrata sul cursore, pan trascinabile, `Adatta` e rigenerazione temporanea HD fino a 2400 px senza accumulare copie ad alta risoluzione in memoria.
- I frame sospetti possono essere esclusi singolarmente o in blocco senza modificare gli originali; nell’export vengono separati sotto `Excluded/Quality` e non caricati come Light da WBPP.
- Esportazione semplificata: anteprima facoltativa e controlli di sicurezza automatici dietro il solo comando `Esporta progetto`, senza dry-run obbligatorio nella UI.
- Preflight non distruttivo su sorgenti mancanti o illeggibili, collisioni, spazio libero con margine configurabile, destinazioni esistenti, sovrapposizione con sorgenti/librerie, path traversal, junction, rete, unità rimovibili e percorsi lunghi.
- Esportazione controllabile con pausa, ripresa e annullamento; velocità, byte ed ETA sono visibili e i file SHA-256 già validi nello staging non vengono ricopiati.
- Report `export-preflight.json` e manifest export schema 2 scritti atomicamente; un nuovo preflight viene eseguito immediatamente prima della copia per intercettare condizioni cambiate.
- Regression test dedicati a dry-run senza scritture, sorgenti mancanti, overlap, traversal, destinazione esistente, spazio insufficiente, staging alterato, pausa/annulla e ripresa verificata.
- Visual system v5: title bar scura integrata, navigazione orizzontale, canvas più ampio, pannelli responsivi e micro-transizioni riducibili.
- Inspector contestuale con stato vuoto utile e controlli nascosti finché non esiste una selezione.
- Dashboard ridisegnata: Gain e temperatura sono campi leggibili con etichette dedicate, non abbreviazioni concatenate.
- Master Library Lab ripensato come workbench indipendente con inventario, preflight, destinazione e anteprima tabellare.
- Scrollbar coerenti con il tema e pannello Sorgenti interamente scorrevole sulle finestre basse.
- Stati vuoti collegati al conteggio reale delle collezioni, senza overlay residui dopo analisi o scansione.
- Flusso Sorgenti unificato: cartelle e file aggiunti dal pannello laterale aggiornano subito il workspace; una modifica ritira la mappa precedente e richiede una nuova analisi esplicita.
- Stato pre-analisi contestuale con inventario delle sorgenti, ricerca disabilitata finché non esiste una mappa e Master Library Lab separato dal progetto.
- QA gate riproducibile e indipendente da cartelle o librerie personali.
- Matrice sintetica multiproduttore per N.I.N.A./ZWO, ASIAIR, QHY/Voyager, Player One/SharpCap e FITS generici.
- Fuzz controllato FITS/XISF, test dei confini della notte astronomica, percorsi Unicode lunghi e dataset da 10.000 frame.
- Ripresa verificata dopo interruzione dell'export.
- Versione SemVer e canale Stable/Beta visibili nell'app.
- Controllo aggiornamenti disattivato di default, manifest solo HTTPS e download verificato con dimensione, SHA-256 e Authenticode.
- Pacchetto self-contained, ZIP portabile, SBOM e installer Windows per utente; l'installer include l'intero runtime WPF verificato e i manifest usano timestamp ISO 8601.

La firma Authenticode è obbligatoria per una release commerciale. Le build locali senza certificato sono marcate esplicitamente come non eleggibili alla vendita.
