# AstroProject Forge — note di rilascio

## 1.1.0 — 28 luglio 2026

- Completata la traduzione inglese delle interfacce Windows, Linux e macOS.
- Aggiunti nomi e descrizioni accessibili per screen reader e un focus tastiera più visibile.
- Nuove scorciatoie per apertura, salvataggio, analisi, menu, guida e cambio area di lavoro.
- `Ctrl+S` salva direttamente il progetto aperto; `Ctrl+Shift+S` apre **Salva con nome**.
- La QA ora blocca stringhe UI non tradotte e regressioni dei contratti di accessibilità.
- Ripristinato il piano ready-to-sell come roadmap verificabile e aggiornata.

## 1.0.3 — 28 luglio 2026

- Le nuove installazioni Windows usano `C:\Program Files\AstroProject Forge`.
- L'installer mantiene lo stesso identificatore e aggiorna direttamente la copia esistente.
- Le vecchie installazioni per utente vengono migrate dalla cartella `%LOCALAPPDATA%\Programs` senza toccare impostazioni, progetti o immagini.
- Gli aggiornamenti richiedono soltanto la conferma UAC di Windows; installazione e riavvio proseguono automaticamente.

## 1.0.2 — 28 luglio 2026

- Mostra una conferma discreta dopo il riavvio automatico, così è immediatamente visibile che l'aggiornamento è riuscito.

## 1.0.1 — 28 luglio 2026

- Il pulsante **Aggiorna e riavvia** scarica e verifica l'installer, aggiorna l'app in modalità silenziosa e riapre automaticamente la nuova versione.
- Il riavvio usa il percorso effettivo scelto durante l'installazione, anche quando non coincide con quello predefinito.
- Aggiunto **Download manuale** nella finestra Informazioni per aprire sempre l'ultima release GitHub.
- In caso di errore prima dell'avvio dell'installer, l'app resta aperta e mostra la diagnostica senza perdere il progetto.

## 1.0.0 — 28 luglio 2026

- Prima release stabile per Windows, Linux e macOS.
- Gli aggiornamenti Windows vengono rilevati all’avvio e scaricati direttamente nell’app.
- I pacchetti GitHub vengono verificati tramite dimensione e SHA-256 anche senza firma Authenticode.
- Interfaccia Informazioni e messaggi di aggiornamento semplificati.
- README, guida e documentazione ripuliti dai riferimenti alle vecchie beta.

## 0.9.0-beta.5 — 27 luglio 2026

- Corretto il controllo aggiornamenti quando il feed Stable/Beta non è ancora stato pubblicato.
- Aggiunto il fallback sulle release GitHub con selezione SemVer del canale più recente.
- Le beta non firmate aprono la pagina release nel browser e non entrano nel downloader verificato.
- I download interni continuano a richiedere dimensione, SHA-256 e firma Authenticode validi.
- Messaggi di aggiornamento e pulsanti corretti in italiano e inglese.
- Aggiunto un comando di pubblicazione che blocca feed non firmati e una guida pratica per ottenere firme Windows e macOS.

## 0.9.0-beta.4 — 26 luglio 2026

- Menu superiore unificato: lingua, dimensione UI, progetto, guida, diagnostica e informazioni sono raccolti in un solo punto.
- Testi WBPP riscritti come istruzioni operative brevi; la tabella mostra soltanto le keyword realmente necessarie e spiega in una riga cosa separano.
- Aggiunti autore, copyright, collegamento GitHub e dichiarazione trasparente sullo sviluppo tramite vibe coding assistito da AI.
- Nuovi collegamenti interni a guida rapida, modulo guidato per i bug e repository.
- README italiano/inglese, screenshot reali, guide rapide e moduli GitHub aggiornati per utenti senza competenze tecniche.
- Preferenze di lingua, densità e animazioni applicate e salvate direttamente dal menu.
- Stessa organizzazione funzionale nella build WPF Windows e nella build Avalonia per Linux/macOS.

## 0.9.0-beta.3 — 22 luglio 2026

- Aggiunto il selettore lingua in `Impostazioni → Lingua / Language` su Windows, Linux e macOS.
- Il passaggio italiano/inglese è immediato, persistente e comprende navigazione, pannelli, azioni, stati numerici, messaggi e tooltip.
- I valori interni stabili, come densità dell’interfaccia e canale release, non dipendono più dalla lingua visualizzata.
- La localizzazione viene riapplicata quando cambiano dati o workspace, evitando pannelli misti dopo analisi e navigazione.
- Le release pubbliche espongono soltanto installer e pacchetti portable; sorgenti ZIP/TAR.GZ sono generati automaticamente da GitHub, mentre report QA, manifest e SBOM restano artefatti interni.

## 0.9.0-beta.2 — 22 luglio 2026

- Linea di soglia, punti arancioni, conteggio e lista dei sospetti usano ora lo stesso score aggregato.
- Rimossa la curva gaussiana fuorviante: il grafico mostra zone operative accettata/da verificare e un istogramma coerente con la soglia.
- Aggiunti click sui punti, conteggi separati per sospetti ed esclusi, filtro tabella `Solo sospetti` e azione di esclusione con conteggio esplicito.
- Il grafico conserva sempre l'intera serie filtro/sessione anche quando la tabella viene filtrata.
- Terminologia e microcopy riscritte su Windows, Linux e macOS: schede e azioni usano nomi brevi e descrittivi.
- Tooltip aggiunti alle operazioni non immediate o con conseguenze sui file; rimossi slogan e nomi sperimentali dalla UI.
- L’Inspector viene nascosto nelle aree che non lo usano, inclusa Libreria Master.
- Contratto di parità aggiornato per bloccare divergenze future tra le interfacce native.

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
