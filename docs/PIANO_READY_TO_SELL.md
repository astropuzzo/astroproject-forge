# AstroProject Forge — piano di intervento “Ready to Sell”

> Stato 21 luglio 2026: completato il contratto di soglia del Quality Lab. Score, grafico, lista e conteggi condividono una sola regola; sospetto ed escluso sono separati esplicitamente e la curva gaussiana fuorviante è stata rimossa.

## Scopo

Portare AstroProject Forge dall’attuale build funzionante a un prodotto Windows
affidabile, installabile, supportabile e vendibile. Il criterio non è avere molte
funzioni, ma poter promettere che il programma organizza un progetto senza
perdere file, assegna le calibrazioni corrette, spiega ogni decisione e permette
di recuperare qualunque errore.

Questo documento è un backlog operativo. Le attività **P0** bloccano la vendita;
le **P1** rendono il prodotto competitivo; le **P2** possono arrivare dopo il
lancio.

## Stato di partenza

La build attuale possiede già:

- lettura degli header FITS e XISF;
- classificazione Light, Flat, Dark e Bias;
- sessione astronomica configurabile oltre la mezzanotte;
- albero gerarchico, Inspector e override batch persistenti;
- libreria Master selezionabile e profilo Offset comune;
- matching Flat/Dark/Bias con diagnostica;
- ricetta adattiva WBPP Grouping Keywords;
- anteprima dell’albero finale;
- copia verificata SHA-256, staging riprendibile, manifest e report;
- eseguibile Windows autonomo.

La build è una buona base tecnica, ma non deve essere commercializzata finché
non sono chiusi i punti P0 seguenti.

## Come usare questo piano

La prima versione del documento elencava correttamente molte necessità, ma non
distingueva abbastanza tra lavoro già fatto, rischio residuo e valore immediato
per l’utente. Da questo punto in avanti ogni intervento deve essere valutato con
quattro numeri da 1 a 5:

- **Impatto utente**: minuti/ore risparmiati o errori evitati;
- **Riduzione rischio**: protezione da calibrazioni sbagliate o perdita dati;
- **Frequenza**: quante volte la funzione serve in un normale progetto;
- **Costo**: sviluppo, test e supporto futuro.

Priorità indicativa: `(Impatto + Rischio + Frequenza) / Costo`. Sicurezza dati e
correttezza scientifica restano comunque gate assoluti, anche quando il punteggio
economico è basso.

## Audit della build attuale — luglio 2026

| Area | Stato | Valutazione operativa |
|---|---|---|
| Parser FITS/XISF | 🟡 Parziale | Funziona sui dataset reali correnti; mancano matrice multiproduttore e fuzz test |
| Notte astronomica | ✅ Implementato | Confine locale configurabile e cambio data gestito |
| Flat Epoch | ✅ Implementato | Timeline automatica, sessioni ottiche e link manuale su una/più notti |
| Albero progetto | ✅ Implementato | Filtro → sessione configurazione → notti/Flat/Master; sessioni sensore separate |
| Matching Dark/Bias | ✅ Implementato | Fallback progetto, libreria prioritaria e diagnostica |
| Ricetta WBPP | ✅ Implementato | `FLATSET/DARKSET/BIASSET/TARGET` adattivi con Pre/Post |
| Statistiche progetto | ✅ Implementato | Ore per filtro, sessione e notte; copertura e stato calibrazioni |
| Override e persistenza preferenze | ✅ Implementato | Singolo, batch, undo e provenienza valori |
| File progetto `.astroforge` | ✅ Implementato | Documento portabile v1, apertura/salvataggio atomico e autosalvataggio dopo le modifiche |
| Coda di revisione | ✅ Implementato | Workflow candidato-per-candidato, confronto tecnico e assegnazione per Light/notte/firma |
| Gestore multi-libreria | 🟡 Implementato v1 | Più radici, priorità e stato online/offline; profili per ruolo ancora da completare |
| Export verificato | ✅ Implementato v1 | Controlli automatici disco/percorsi, pausa/annulla/riprendi, ETA, staging SHA-256 e report atomici; anteprima diagnostica facoltativa, restano prove hardware/VM distruttive prima della vendita |
| Quality Lab | 🟡 Implementato v1 | Analisi FITS opzionale con FWHM, eccentricità, noise/SNR, stelle, outlier robusti, Blink ed esclusioni non distruttive; XISF e cache metriche restano da completare |
| Cache incrementale | 🟡 Implementata v1 | Header indicizzati per path, dimensione e data modifica; invalidazione e pulizia manuale operative. Da migrare a SQLite per dataset estremi |
| Installer/updates/firma | 🟡 Implementato per QA | ZIP portabile, installer Inno Setup per utente, manifest/feed/SBOM/hash e runtime WPF completo; firma Authenticode e licenza commerciale Inno restano gate di vendita |
| Diagnostica e support bundle | ✅ Implementato | Log ruotati e redatti, Centro diagnostica, correlazione operazioni, recovery journal e ZIP locale privacy-safe |
| Privacy/licensing | 🟡 Parziale | Privacy tecnica applicata; documenti legali e licensing commerciale restano in P0.15/P0.16 |

## Roadmap ottimizzata per release

### Release 0.4 — Project Intelligence

**Obiettivo:** trasformare l’inventario in uno strumento decisionale quotidiano.

- dashboard ore per filtro/sessione/notte;
- copertura Flat/Dark/Bias;
- intervalli data, temperature, Gain ed esposizione media;
- sessioni di configurazione e Flat Epoch;
- link manuale multi-sessione;
- export CSV/JSON delle statistiche;

**Avanzamento 11 luglio 2026:** dashboard ed export statistico completati; Gain e temperatura ora sono presentati con etichette leggibili e le sessioni mostrano numero progressivo e intervallo date, mantenendo l'ID tecnico nel tooltip. Implementato inoltre il documento progetto `.astroforge` v1 con sorgenti, libreria Master, destinazione, confine della notte, default e override. Il salvataggio è atomico e, dopo il primo “Salva progetto”, viene aggiornato automaticamente.

**Igiene repository completata:** rimossi dal progetto i frame FITS/XISF di esempio, gli artefatti `bin/obj`, i pacchetti PyInstaller e il prototipo Python superato. I formati astronomici e i file progetto personali sono ora esclusi da Git. I test generano autonomamente i propri fixture temporanei.
- obiettivo ore per filtro e indicatore “mancano X ore”.

**Stato:** nucleo implementato; export e obiettivi sono il prossimo incremento.

### Release 0.5 — Progetti veri e recuperabili

**Obiettivo:** nessun lavoro perso e progetto trasferibile tra PC/dischi.

- formato `.astroforge` versionato;
- Nuovo/Apri/Salva con nome;
- autosave e crash recovery;
- percorsi relativi e rilocalizzazione sorgenti;
- snapshot decisioni, statistiche e piano export;
- cronologia modifiche e redo.

**Gate:** chiusura forzata durante ogni fase e recupero completo al riavvio.

### Release 0.6 — Revisione guidata

**Obiettivo:** rendere risolvibile ogni ambiguità senza conoscere gli header.

- inbox Error/Warning/Info;
- confronto visuale candidati Flat/Dark/Bias;
- motivazione campo per campo;
- assegna candidato a file, notte, sessione o filtro;
- regole riutilizzabili generate da una decisione;
- simulazione delle conseguenze prima dell’applicazione.

### Release 0.7 — Calibration Library Manager

**Obiettivo:** gestire più camere e librerie senza configurazioni fragili.

- più radici Dark/Bias/Dark-flat;
- profili camera/sensore;
- indice SQLite e scansione incrementale;
- dashboard copertura gain/offset/temperatura/esposizione;
- rilevamento duplicati e Master obsoleti;
- volumi offline e percorsi di rete.

### Release 0.8 — Export antifragile e WBPP certification

**Obiettivo:** produrre sempre un progetto verificabile e riprendibile.

- preflight spazio/permessi/path lunghi;
- pausa, resume e recovery formalizzati;
- test di interruzione e disco pieno;
- matrice WBPP per versione;
- report HTML/PDF professionale;
- dry-run e support bundle.

**Avanzamento 21 luglio 2026:** completato il nucleo di export antifragile. L'utente può generare un'anteprima diagnostica facoltativa, mentre `Esporta progetto` esegue direttamente i controlli e procede soltanto se sono superati. Il motore verifica sorgenti mancanti o non leggibili, progetto già esistente, duplicati, path traversal, percorsi lunghi, staging parziali o alterati, spazio libero con margine/riserva configurabili, rete/unità rimovibili, junction e sovrapposizione con cartelle di acquisizione o Master Library. La copia può essere messa in pausa, ripresa o annullata e mostra byte, velocità ed ETA; i file già presenti nello staging vengono riutilizzati solo dopo confronto SHA-256. Manifest schema 2, ricetta, statistiche, guida e `export-preflight.json` sono pubblicati atomicamente. Il motore è stato inoltre verificato su un piano reale da 415 file / 20,51 GB senza scritture durante la sola analisi. Restano la certificazione WBPP per versione, il report PDF e la matrice hardware con disco pieno/cavo rimosso/process kill.

**Semplificazione UX 21 luglio 2026:** il preflight non è più un passaggio imposto all'utente. Anteprima e diagnostica dettagliata restano disponibili, ma il comando `Esporta progetto` costruisce il piano se necessario, esegue automaticamente i controlli e procede quando sono superati. La sicurezza rimane nel motore senza appesantire il flusso normale.

### Release 0.9 — Commercial Beta

**Obiettivo:** installazione, aggiornamento e supporto su PC non di sviluppo.

- installer firmato;
- updater con rollback;
- onboarding italiano/inglese;
- telemetria assente di default e crash report opt-in;
- trial/licenza offline-friendly;
- manuale, video, FAQ e canale ticket;
- beta chiusa con metriche di uscita.

## Funzioni ad alto valore da inserire dopo il cruscotto base

1. **Obiettivi di integrazione**: impostare, per esempio, HOO 20 h e SIOIII 15 h;
   mostrare completamento e ore mancanti.
2. **Confronto sessioni**: matrice filtro × sessione con ore, temperatura, Flat
   Epoch e anomalie.
3. **Timeline eventi ottici**: annotare pulizia, rotazione, smontaggio camera,
   sostituzione filtro e cambio readout.
4. **Fingerprint dei Flat**: confrontare vignettatura e polvere per suggerire
   discontinuità reali tra Flat Set.
5. **Import multiproduttore**: confrontare file FITS/XISF, sequenza pianificata, eventuali log e header acquisiti; l'integrazione N.I.N.A. resta un arricchimento opzionale.
6. **Export dati**: CSV/JSON per fogli di calcolo, archivio personale e supporto.
7. **Target intelligence**: ore totali per target e filtro, non soltanto per
   progetto.
8. **Quality module opzionale**: FWHM, eccentricità e background senza rallentare
   la scansione header-only.

Funzioni come cloud obbligatorio, social sharing o elaborazione cosmetica non
entrano nella roadmap iniziale: aumentano costi e privacy risk senza migliorare
il compito centrale dell’app.

## Definizione di “Ready to Sell”

Il prodotto è vendibile soltanto quando sono vere tutte queste condizioni:

1. Nessun dataset supportato può produrre in silenzio una calibrazione errata.
2. Ogni scelta automatica mostra motivazione, confidenza e dati utilizzati.
3. Ogni ambiguità può essere risolta dalla GUI senza rinominare manualmente i
   file sorgente.
4. Interruzione, disco pieno, file bloccato e arresto del PC non danneggiano gli
   originali e non richiedono di ricominciare da zero.
5. Il progetto generato supera una prova reale in PixInsight WBPP sulle versioni
   dichiarate compatibili.
6. Installer, firma digitale, aggiornamento e disinstallazione sono verificati.
7. Esiste un sistema per raccogliere un pacchetto diagnostico senza dati privati.
8. Manuale, onboarding, licenza, privacy e canale di supporto sono pronti.
9. Tutte le prove P0 sono automatizzate o descritte da una checklist ripetibile.

---

# P0 — interventi indispensabili prima della vendita

## P0.1 — Progetto salvabile e ripristinabile

### Intervento

Introdurre un file progetto versionato, ad esempio `nome.astroforge`, contenente:

- sorgenti e librerie utilizzate;
- impostazioni di sessione e fuso orario;
- snapshot degli header normalizzati;
- override e assegnazioni manuali;
- regole di matching e tolleranze;
- piano di esportazione;
- stato dell’ultima esportazione;
- versione dello schema e dell’applicazione.

Implementare `Nuovo`, `Apri`, `Salva`, `Salva con nome`, autosave e recupero dopo
crash. Le modifiche devono rendere visibile lo stato “non salvato”.

### Criteri di accettazione

- Chiusura forzata e riapertura recuperano il lavoro fino all’ultimo autosave.
- Un progetto vecchio viene migrato oppure aperto in sola lettura con spiegazione.
- Percorsi mancanti possono essere rilocalizzati senza perdere gli override.
- Nessun salvataggio contiene hash o header appartenenti a un altro progetto.

## P0.2 — Gestione completa delle librerie di calibrazione

### Intervento

Superare il singolo percorso “Libreria Master” e creare un vero gestore librerie:

- più librerie contemporanee;
- percorsi separati per Dark, Bias e Dark-flat;
- librerie locali, dischi esterni e percorsi di rete;
- priorità delle librerie;
- profili per camera, gain, offset, temperatura e readout mode;
- scansione e indicizzazione incrementale;
- stato online/offline del volume;
- validazione della struttura e anteprima dei Master trovati;
- comando “Riscansiona libreria” e “Ricostruisci indice”.

Il valore `E:\immagini\MSTE` deve restare una normale preferenza utente, mai una
dipendenza del programma.

### Criteri di accettazione

- La build funziona su un PC che non possiede alcuna unità `E:`.
- L’utente può scegliere librerie diverse per due camere nello stesso progetto.
- Un volume temporaneamente scollegato non elimina configurazioni o override.
- Master duplicati vengono segnalati e confrontati, non scelti casualmente.

**Avanzamento 12 luglio 2026:** implementato il gestore multi-libreria v1. Il progetto e il documento `.astroforge` conservano più radici Master con nome, abilitazione e priorità. La UI permette aggiunta, rimozione non distruttiva, riordino e aggiornamento dello stato online/offline. La scansione unifica le radici attive senza duplicare file; tra Master scientificamente equivalenti vince la libreria con priorità più alta. I vecchi progetti con il solo `LibraryPath` vengono migrati automaticamente. Restano da aggiungere profili separati Dark/Bias/Dark-flat, editor del nome e indice per-libreria con conteggi e diagnostica.

**Master Library Lab — 12 luglio 2026:** aggiunto il flusso di normalizzazione non distruttivo. I metadati dimostrabili vengono precompilati; per i Master incompleti la tabella richiede Camera, Gain, Offset, temperatura ed esposizione. L'esecuzione crea una nuova libreria per `Camera/Gain/Offset/Temperatura/Tipo`, copia in streaming, imprime le keyword sulle sole copie FITS/XISF quando l'header può essere aggiornato senza spostare i dati, verifica SHA-256 e genera `astroforge-master-library.json`. L'originale viene ricontrollato dopo ogni copia. Restano da aggiungere anteprima dei conflitti, gestione CHECKSUM/DATASUM FITS e rollback guidato dell'intero batch.

**Master Library Lab safety v2 — 14 luglio 2026:** introdotto un preflight obbligatorio che calcola tutte le destinazioni prima di scrivere e blocca sia collisioni interne al batch sia file già presenti. Ogni Master mostra il proprio stato preflight nella tabella. L'esecuzione è transazionale a livello di batch: se copia, stamping o verifica falliscono, vengono rimosse soltanto le nuove copie prodotte dal tentativo e le cartelle rimaste vuote. Il manifest viene pubblicato atomicamente. Il rollback guidato dell'ultimo batch verifica preventivamente percorso e SHA-256 di ogni copia; se anche un solo file è mancante o modificato, non elimina nulla. Testate collisioni, destinazioni esistenti, rollback riuscito e blocco su copia alterata. Restano CHECKSUM/DATASUM FITS e una cronologia multi-batch per chiudere completamente P0.2.

## P0.3 — Risoluzione guidata di ambiguità e assegnazioni manuali

### Intervento

Creare una vera “Coda di revisione” ordinabile per gravità. Per ogni Light o
gruppo ambiguo mostrare:

- candidato Flat/Dark/Bias selezionato;
- candidati alternativi con punteggio;
- campi compatibili, mancanti o incompatibili;
- motivo del rifiuto;
- anteprima dell’effetto di una correzione;
- comando `Assegna questo Master`;
- comando `Applica al gruppo`;
- comando `Non usare calibrazione` solo nei casi consentiti e con avviso;
- possibilità di annullare e ripristinare ogni decisione.

Un’assegnazione manuale deve avere precedenza sulle regole automatiche e deve
essere registrata nel manifest.

### Criteri di accettazione

- Ogni stato `ambiguous`, `missing` e `insufficient metadata` è risolvibile dalla
  GUI.
- Cambiare un valore ricalcola immediatamente tutti gli abbinamenti dipendenti.
- L’app non passa allo stato “Pronto” se resta una scelta non dimostrata.

**Avanzamento 12 luglio 2026:** implementata la Coda di revisione v1 come quinta area dell'app. Le assegnazioni Flat/Dark/Bias non risolte sono ordinate per priorità e mostrano stato, frame, filtro/notte, numero di candidati, causa e azione suggerita. Selezionare una voce porta il relativo frame nell’Inspector. Dark e Bias possono ora essere assegnati esplicitamente al singolo Light o all'intera notte; la scelta è persistente, annullabile e prevale sull'automatismo. Restano da completare una comparazione tabellare campo-per-campo e l'assegnazione batch con firma di configurazione, necessarie per chiudere P0.3.

**Avanzamento 14 luglio 2026 — revisione batch v2:** aggiunto il confronto espandibile dei Master candidati con Camera, Gain, Offset, temperatura, esposizione, binning, readout e score. Dark e Bias possono essere assegnati al singolo Light, alla notte astronomica oppure a tutti i Light che condividono la stessa firma tecnica, anche attraverso filtri e notti differenti. La firma è una regola testata del Core: Camera, Gain, Offset, temperatura entro tolleranza, dimensioni, binning e readout devono coincidere; per il Dark coincide anche l'esposizione, mentre per il Bias viene ignorata. L'operazione resta persistente e annullabile. P0.3 è funzionalmente chiuso; resta l'audit visuale su dataset con decine di candidati, incluso uso da tastiera e schermi stretti.

## P0.4 — Motore di regole e profili riutilizzabili

### Intervento

Creare un editor di regole con scope esplicito:

- singolo file;
- cartella/sessione;
- filtro;
- camera/configurazione sensore;
- libreria;
- regola globale.

Esempi: “tutti i Master di questa libreria hanno Offset 51”, alias filtro
`Ha/H-alpha`, parsing di una struttura cartelle personalizzata, temperatura
dedotta dal percorso. Ogni regola deve mostrare quanti file influenzerà prima di
essere salvata.

### Criteri di accettazione

- Le regole possono essere abilitate, disabilitate, ordinate, esportate e
  importate.
- I conflitti tra regole sono visibili e risolti per precedenza dichiarata.
- Nessuna regola modifica fisicamente gli header originali.

## P0.5 — Copertura reale dei metadati e dei formati

### Intervento

Rendere il parser tollerante ma rigoroso:

- FITS standard, estensioni `.fit`, `.fits`, `.fts` e maiuscole;
- XISF con proprietà e keyword FITS;
- FITS multi-HDU e immagini compresse usate dai software supportati;
- stringhe quotate, commenti, unità, valori scientifici e header non ordinati;
- date UTC/locali con timezone e passaggi ora legale;
- keyword alternative documentate dai software di acquisizione;
- distinzione certa tra gain di acquisizione ed `EGAIN` in elettroni/ADU;
- checksum e dimensione coerente quando presenti;
- parser XML XISF protetto da riferimenti esterni e input patologici.

Creare profili verificati almeno per:

- N.I.N.A.;
- SharpCap;
- ASIAIR;
- Voyager o Sequence Generator Pro;
- PixInsight XISF Master;
- almeno una camera OSC e una mono di produttori differenti.

**Avanzamento 14 luglio 2026 — import generico v1:** l'import accetta sia cartelle ricorsive sia singoli file `.fit`, `.fits`, `.fts` e `.xisf`, persistendoli correttamente nel progetto e tra i riavvii. N.I.N.A. non è più presentato né trattato come requisito. La classificazione è ora header-first: `IMAGETYP`, `FRAMETYP`, `OBSTYPE`, `FRAME`, `PICTTYPE` e `IMAGE-TYP` prevalgono sul nome file; quest'ultimo viene usato soltanto se il tipo non è dimostrabile dall'header. Aggiunti alias multiproduttore per camera, filtro, esposizione, Gain, Offset, temperature, binning, readout, Bayer, focale, rotatore e data. Una fixture non-N.I.N.A. verifica anche che un nome file contraddittorio non possa sovrascrivere un header SCIENCE valido. Restano multi-HDU, FITS compressi, golden dataset reali per produttore e fuzzing per chiudere P0.5.

### Criteri di accettazione

- Dataset “golden” anonimizzati per ogni produttore.
- Nessun crash su file troncato o header non valido.
- Un campo non dimostrabile resta “Mancante”; non viene inventato.

## P0.6 — Calibrazione scientificamente corretta

### Intervento

Completare e documentare le firme di compatibilità:

- camera/sensore, geometria, ROI e binning;
- gain, offset, temperatura e readout mode;
- esposizione Dark con tolleranza configurabile;
- filtro, Bayer, focale e rotatore per i Flat;
- Dark-flat associati ai Flat;
- Master grezzi rispetto a Master già integrati;
- comportamento specifico per OSC e mono;
- sensori con amp glow, overscan o modalità ad alto guadagno;
- divieto di interpolare o “ottimizzare” un Dark senza consenso esplicito;
- soglie di temperatura e tolleranze visibili nel report.

La compatibilità deve essere separata dalla preferenza: prima si escludono i
candidati non validi, poi si sceglie il migliore tra quelli validi.

### Criteri di accettazione

- Test di verità con abbinamenti attesi definiti da un astrofotografo esperto.
- Nessun candidato incompatibile può vincere grazie a un punteggio elevato.
- Il report spiega ogni tolleranza applicata.

## P0.7 — Integrazione WBPP verificata end-to-end

### Intervento

Costruire una matrice di compatibilità per le versioni WBPP dichiarate. Per ogni
scenario verificare realmente dentro PixInsight:

- progetto singolo e multisessione;
- mono multifiltro e OSC;
- più temperature/gain/offset;
- più set Flat per lo stesso filtro;
- più target nello stesso progetto;
- Dark-flat;
- keyword `FLATSET`, `DARKSET`, `BIASSET`, `TARGET` con Pre/Post corretti;
- tabella Grouping Keywords vuota quando bastano i gruppi nativi;
- nessun uso di `DATE-OBS` per separare la notte;
- nomi e valori compatibili con il parser dei percorsi WBPP.

Il report finale deve contenere una checklist WBPP con i gruppi attesi e i nomi
esatti dei Master che l’utente deve vedere nella scheda Calibration.

### Criteri di accettazione

- Tutti gli scenari P0 completano `Diagnostics` di WBPP senza assegnazioni errate.
- I risultati attesi sono documentati con screenshot e manifest di riferimento.
- Una nuova versione WBPP non viene dichiarata compatibile senza regression test.

## P0.8 — Esportazione realmente antifragile

### Intervento

Completare la macchina a stati dell’esportazione:

- preflight dello spazio libero con margine configurabile;
- supporto a percorsi lunghi, Unicode, rete e dischi rimovibili;
- gestione file bloccati, permessi insufficienti e sorgenti scomparse;
- pausa, annulla e riprendi;
- verifica SHA-256 senza ricopiare file già validi;
- manifest scritto in modo atomico e versionato;
- controllo che destinazione e sorgente non si sovrappongano;
- protezione da path traversal, symlink e junction inattese;
- rilevamento duplicati tramite dimensione/hash, non solo nome;
- stima tempo, byte copiati e velocità;
- modalità dry-run;
- pulizia guidata degli staging incompleti.

Lo spostamento degli originali resta fuori dalla prima release commerciale. Può
essere introdotto solo come operazione separata dopo copia e verifica completa,
con conferma esplicita e log recuperabile.

### Stato implementazione — 21 luglio 2026

Completati nel prodotto e coperti da test automatici:

- macchina a stati `Non verificato → Preflight → Pronto/Bloccato → Copia/In pausa → Completato/Riprendibile`;
- dry-run senza creazione di cartelle o file;
- spazio libero, margine percentuale, riserva fissa e stima del tempo;
- blocco di sorgenti mancanti, destinazioni già esistenti, collisioni, traversal, overlap e staging non coerenti;
- avvisi per rete, dischi rimovibili, percorsi lunghi e copie `.partial`;
- pausa e annullamento anche durante hashing/verifica, con staging mantenuto;
- ripresa solo per file della stessa dimensione e con SHA-256 identico;
- ricontrollo atomico delle condizioni subito prima dell'esecuzione;
- manifest schema 2 e report preflight conservato nel progetto esportato.

Non ancora certificati e quindi ancora gate commerciali:

- prove fisiche con rimozione improvvisa di USB/rete e simulazione controllata di disco pieno;
- terminazione forzata su più punti della pipeline in VM pulite;
- pulizia guidata e inventario multi-staging (spostato in P0.9);
- matrice WBPP e report PDF firmabile.

### Criteri di accettazione

- Test con disco pieno, cavo rimosso, processo terminato e file bloccato.
- Dopo ogni test gli originali risultano invariati e lo staging è riprendibile.
- Nessun file esistente viene sovrascritto senza decisione esplicita.

## P0.9 — Centro di pulizia sicura

### Intervento

Distinguere chiaramente:

- `Svuota progetto`: scarica dati dalla memoria;
- `Rimuovi sorgente`: toglie un percorso dal progetto;
- `Pulisci cache`: elimina solo cache ricostruibili;
- `Pulisci staging`: mostra esportazioni incomplete e consente di rimuoverle;
- `Pulisci log`: elimina log locali secondo retention;
- `Elimina progetto esportato`: operazione separata, mai predefinita.

Ogni pulizia deve mostrare percorso, numero di file, dimensione e conseguenze.
Nessun comando generico “Pulisci” deve poter cancellare FITS/XISF sorgente.

### Criteri di accettazione

- I pulsanti non distruttivi usano un linguaggio diverso da quelli distruttivi.
- Ogni eliminazione fisica richiede conferma con percorso esatto.
- Test automatici verificano che le radici sorgente non siano mai target di
  cancellazione.

## P0.10 — Prestazioni e scalabilità

### Intervento

- Cache indicizzata degli header con invalidazione per path, dimensione e data.
- Database locale transazionale, preferibilmente SQLite.
- Scansione incrementale e concorrenza limitata.

**Avanzamento 11 luglio 2026:** completata la cache incrementale v1. La seconda analisi riutilizza gli header invariati, mentre una variazione di dimensione o data di modifica forza la rilettura del singolo file. La barra di stato distingue file letti e recuperati dalla cache; aggiunto il comando sicuro `Pulisci cache`. Test automatici coprono sia cache hit sia invalidazione. Resta pianificato il backend SQLite prima della beta per evitare la riscrittura dell'indice JSON su cataloghi molto grandi.
- Cancellazione immediata delle operazioni lunghe.
- TreeView realmente virtualizzato anche con decine di migliaia di file.
- Aggiornamenti UI aggregati per evitare freeze.
- Nessun caricamento dei pixel durante inventario e matching.
- Profilazione di memoria e handle aperti.

### Target minimi

- 10.000 frame indicizzati senza superare 1 GB di RAM.
- Riapertura di un progetto invariato in pochi secondi grazie alla cache.
- UI responsiva durante scansione, hashing ed esportazione.

## P0.11 — UX commerciale e onboarding

### Intervento

Creare un flusso guidato al primo avvio:

**Avanzamento 12 luglio 2026 — visual system v2:** introdotto un design system WPF premium condiviso con superfici glass semi-trasparenti, profondità blu notte, gradienti controllati, glow sugli elementi attivi, ombre morbide, card e raggi aggiornati. Pulsanti e tab hanno micro-interazioni animate; input, ComboBox, TreeView, DataGrid, scrollbar, header, pannelli laterali e barra di stato condividono ora la stessa grammatica visiva. La build self-contained è stata verificata visivamente nelle viste Analisi e Master Library Lab. Restano da implementare selettore densità, tema chiaro e preferenza utente `Movimento ridotto` prima di chiudere P0.11.

**Correzioni workflow 12 luglio 2026:** `Analizza ora` è stato spostato nel banner centrale dello stato progetto. Master Library Lab è presentato come strumento indipendente e può analizzare/organizzare librerie anche senza cartelle Light. La mappa non mostra più l'intero inventario Dark/Bias sotto `Senza filtro`: espone soltanto i Master realmente assegnati, mentre gli inutilizzati restano nel gestore librerie. Doppio click su albero, Coda di revisione e Master Lab apre l'origine in Esplora file.

**Shell responsive v3 — 12 luglio 2026:** sostituita la fila di tab con una navigazione verticale persistente. Sorgenti e Inspector sono pannelli collassabili, ridimensionati automaticamente sotto 1250 px e chiusi progressivamente sugli schermi più stretti; restano richiamabili dall'header. Master Library Lab chiude l'Inspector non pertinente e utilizza tutta l'area di lavoro. La scansione Lab usa un percorso indipendente che legge esclusivamente le librerie abilitate e non modifica `_frames`, analisi WBPP, albero progetto o Coda di revisione.

**Preferenze UX v1 — 12 luglio 2026:** aggiunto pannello glass indipendente nell'header con densità `Compatta`, `Comoda` e `Ampia`, applicata in tempo reale ad altezze e padding dei controlli. Aggiunta preferenza persistente `Movimento ridotto`, che elimina la transizione di ingresso. Entrambe vengono conservate localmente in `state.json`. La vista Analisi ora possiede uno stato vuoto guidato invece di una superficie senza indicazioni. Build, avvio self-contained e popup sono stati verificati a schermo. Restano tema chiaro, localizzazione e audit DPI 125–250% per chiudere P0.11.

**Onboarding v1 — 12 luglio 2026:** implementato flusso glass in quattro passaggi al primo avvio: promessa e limiti del prodotto, collegamento Master Library multi-camera, impostazioni della notte e fallback Gain/Offset, import cartelle N.I.N.A. e consegna alla prima analisi. Le azioni usano gli stessi selettori del workspace, quindi non creano una configurazione parallela. Il completamento è persistente, è possibile saltare il flusso e riaprirlo dalle Preferenze. Verificati visivamente tutti i passaggi e provata la persistenza con chiusura e riavvio dell'EXE self-contained.

**Command bar responsive v4 — 12 luglio 2026:** riprogettato l'header per mantenere allineamento e priorità tra 900 e 2560 px. Apri, Salva, pannelli e Preferenze restano immediatamente raggiungibili; Annulla, pulizia cache e svuotamento progetto sono raccolti in un menu dedicato con descrizioni esplicite e trattamento visivo distinto per l'azione distruttiva. Sotto 1080 px marchio esteso e label dei pannelli si comprimono, mentre lo stato documento scompare sotto 1380 px senza provocare sovrapposizioni. Popup, tooltip, stato disabilitato e nomi accessibili fanno parte dello stesso controllo. Restano da completare audit DPI 125–250%, navigazione completa da tastiera e tema chiaro.

**Visual system v5 — 18 luglio 2026:** sostituito l'effetto “glass/neon” uniforme con un workspace editoriale più sobrio: chrome Windows scuro integrato, navigazione orizzontale che restituisce spazio alla mappa, superfici ink con profondità selettiva, accento riservato ad azioni e stato, micro-transizione tra aree disattivabile con `Movimento ridotto`. L'Inspector ora mostra istruzioni contestuali quando non esiste una selezione e carica gli editor soltanto quando servono. La vista vuota Analisi offre accessi diretti a cartelle, Master Library e prima scansione. Dashboard Dati e Master Library Lab sono stati ricomposti; Gain e temperatura hanno etichette/valori separati, mentre il Lab espone chiaramente inventario, preflight, destinazione, organizzazione e rollback. Aggiunte scrollbar scure verticali/orizzontali e scorrimento dell'intera colonna Sorgenti per finestre basse. Verificati 1540×940, 982×702 e 2560×1440; corretto inoltre il binding degli stati vuoti al `Count`, che prima poteva lasciare l'onboarding sopra contenuti già caricati. Nuovi screenshot reali e privi di percorsi locali sono pubblicati nel README italiano e inglese. Restano audit DPI 125–250%, tastiera/screen reader, localizzazione delle stringhe e tema chiaro opzionale.

1. scegli lingua e cartella progetto;
2. registra una o più librerie Master;
3. scegli il confine della notte astronomica e timezone;
4. importa una cartella di prova;
5. risolvi un’ambiguità dimostrativa;
6. genera l’anteprima WBPP.

Completare inoltre:

- dashboard Recenti;
- drag and drop di cartelle;
- coda diagnostica con filtri e ricerca;
- severità Error/Warning/Info ben distinguibili;
- undo e redo multipli;
- scorciatoie da tastiera;
- tooltip contestuali e link al manuale;
- stato vuoto utile per ogni tab;
- conferme non invasive e notifiche persistenti;
- tema chiaro/scuro, scaling 100–250% e finestre piccole;
- accessibilità da tastiera, screen reader e contrasto;
- italiano e inglese separati dalle stringhe del codice.

### Criteri di accettazione

- Un nuovo utente completa il primo progetto senza assistenza esterna.
- Tutte le funzioni principali sono utilizzabili senza mouse.
- Nessun testo viene tagliato a 125%, 150%, 200% e 250% DPI.

## P0.12 — Diagnostica, log e support bundle

### Intervento

Implementare logging strutturato con livelli e rotazione. Aggiungere `Esporta
pacchetto diagnostico` contenente, previa anteprima:

- versione app, Windows e runtime;
- impostazioni non sensibili;
- log recenti;
- manifest senza contenuto immagine;
- elenco degli errori;
- header selezionati con possibilità di oscurare target, coordinate e percorsi;
- hash dei file, mai i pixel astronomici.

Il pacchetto deve essere creato localmente. L’invio al supporto è sempre una
scelta separata dell’utente.

**Avanzamento 16 luglio 2026 — diagnostica v1:** introdotto un registro JSONL locale con timestamp UTC, livello, codice evento, messaggio controllato e tipo eccezione. I log ruotano automaticamente e oscurano percorsi Windows e nomi FITS/XISF prima della scrittura. Tutti gli errori intercettati dai flussi principali mostrano ora un codice `AF-*` ricercabile e lo registrano localmente; anche le eccezioni UI impreviste hanno recovery e codice dedicato. Dal menu progetto è disponibile `Esporta diagnostica`: prima del salvataggio mostra l'inventario esatto, poi crea localmente uno ZIP con versioni di app/Windows/runtime, impostazioni tecniche non sensibili, soli conteggi diagnostici, codici/severità degli errori e log recenti. Sono esclusi immagini, pixel, target, coordinate, nomi file, percorsi, header grezzi e dettagli dei Master. Testate rotazione, inventario ZIP e redazione di percorsi/nomi astronomici. Restano una vista log interna, correlazione per singola operazione e recovery journal del progetto per chiudere P0.12.

**Diagnostica e recovery v2 — 17 luglio 2026:** P0.12 è funzionalmente chiuso. Il nuovo Centro diagnostica mostra gli eventi recenti già redatti, con livello, codice ricercabile, operazione e ID breve di correlazione. Analisi progetto, export verificato, scansione/organizzazione/rollback delle Master Library e creazione del support bundle registrano inizio ed esito con lo stesso ID. Prima delle operazioni lunghe o mutative viene scritto atomicamente un recovery journal locale contenente una fotografia del documento progetto; viene eliminato soltanto alla conclusione e, dopo un'interruzione reale, compare una barra che permette di ripristinare o ignorare consapevolmente la fotografia. Finché la scelta non viene risolta, le operazioni che potrebbero sovrascriverla restano bloccate. Test automatici coprono persistenza, protezione da ID estranei, completamento, lettura degli eventi correlati e redazione del support bundle. Resta l'audit finale DPI/tastiera previsto da P0.11/P0.13, non lavoro funzionale su P0.12.

### Criteri di accettazione

- Ogni errore mostrato nella GUI possiede un codice ricercabile.
- Nessuna eccezione non gestita chiude l’app senza recovery e log.
- Il bundle mostra esattamente cosa contiene prima del salvataggio.

## P0.13 — Test, QA e dataset di regressione

### Intervento

Costruire una suite multilivello:

- unit test del parser e del matching;
- property-based test per date, numeri e nomi file;
- fuzz test per FITS/XISF malformati;
- snapshot test dei manifest e degli alberi finali;
- test UI dei flussi principali;
- test di migrazione delle impostazioni;
- test su dataset grandi;
- test di interruzione esportazione;
- test su Windows 10 e 11 supportati;
- test su account standard, non amministratore;
- test su NTFS, exFAT e percorsi di rete;
- test puliti senza dipendere da `E:` o dalla libreria dello sviluppatore.

Conservare dataset piccoli sintetici nel repository e dataset reali anonimizzati
in uno storage di test con manifest degli hash.

### Gate

- Zero test P0 falliti.
- Zero crash riproducibili aperti.
- Zero difetti noti che possono causare perdita o calibrazione errata.
- Copertura elevata sulle decisioni critiche, non soltanto percentuale globale.

## P0.14 — Installer, firma e aggiornamenti

### Intervento

- Installer per x64 con percorso per utente, senza privilegi se non necessari.
- Firma digitale dell’eseguibile e dell’installer.
- Versionamento SemVer visibile in app, manifest e log.
- Canale Stable e Beta separati.
- Controllo aggiornamenti configurabile.
- Download verificato e aggiornamento con rollback.
- Disinstallazione che chiede se conservare progetti e impostazioni.
- Release notes e pagina “Informazioni”.
- Software Bill of Materials e inventario dipendenze.

### Criteri di accettazione

- Installazione, aggiornamento e downgrade di emergenza provati su VM pulite.
- La disinstallazione non elimina progetti o FITS.
- L’app non richiede l’SDK .NET sul PC dell’utente.

**Avanzamento 17 luglio 2026 — P0.13:** implementato un gate QA locale e una workflow GitHub Actions Windows senza dipendenze da `E:` o da dati personali. La suite usa una fixture JSON multiproduttore versionata, verifica 1.000 casi property-based attorno al cambio della notte astronomica, 10.000 classificazioni sintetiche, percorsi Unicode lunghi, fuzz bounded di FITS/XISF, migrazione impostazioni, SemVer/update e un'interruzione reale dell'export seguita da ripresa. Il gate compila inoltre WPF Release e pubblica l'EXE self-contained, producendo un report JSON. Il collaudo visivo del flusso Informazioni/aggiornamenti è passato sulla build pubblicata. Restano come gate di Release Candidate — non come dipendenze del test locale — la matrice su VM Windows 10/11, account standard, exFAT e share di rete.

**Avanzamento 17 luglio 2026 — P0.14:** implementati SemVer e canale visibili nell'app, preferenza Stable/Beta, controllo update disattivato di default, feed esclusivamente HTTPS e download in `.partial` verificato per dimensione e SHA-256 prima del rename atomico. La pipeline produce EXE self-contained, ZIP portabile, SBOM, manifest, feed di canale, `SHA256SUMS.txt` e installer Inno Setup 7 x64 per utente senza privilegi amministrativi. Stable e Beta hanno AppId e directory distinti; l'uninstaller chiede se rimuovere i soli dati locali e non include mai progetti o FITS/XISF. Il gate `-RequireSignature` rifiuta EXE o installer privi di firma Authenticode verificabile. La build Beta locale è stata compilata correttamente ma resta deliberatamente `releaseEligible: false`: servono un certificato code-signing autentico, una licenza commerciale del compilatore installer e il collaudo install/update/downgrade su VM pulite prima di chiudere i criteri commerciali.

## P0.15 — Sicurezza e privacy

### Intervento

- Threat model per parser, percorsi, XML, aggiornamenti e licenze.
- Dipendenze bloccate a versioni note e scansione vulnerabilità in CI.
- Nessuna esecuzione automatica di script trovati nelle cartelle sorgente.
- Telemetria disattivata per impostazione predefinita.
- Crash reporting esclusivamente opt-in e con redazione dati.
- Nessun upload di immagini o header senza azione esplicita.
- Segreti di firma e licensing fuori dal repository.
- Informativa privacy comprensibile e retention documentata.

### Criteri di accettazione

- Revisione di sicurezza prima della Release Candidate.
- Tutto il traffico di rete è documentato e disattivabile.
- Il programma funziona offline, salvo attivazione/aggiornamento se previsti.

## P0.16 — Licenza, attivazione e aspetti legali

### Intervento

Definire prima del lancio:

- licenza perpetua o abbonamento;
- numero di dispositivi;
- trial completo e durata;
- attivazione online con modalità offline ragionevole;
- periodo di tolleranza se il server licenze non risponde;
- recupero e disattivazione dispositivi;
- politica rimborsi;
- EULA;
- informativa privacy;
- attribuzioni e licenze delle dipendenze;
- marchio, nome prodotto e dominio;
- disclaimer: il report aiuta il preprocessing ma non sostituisce la verifica
  finale dell’utente in PixInsight.

Il guasto del sistema licenze non deve bloccare l’accesso ai progetti già creati.

---

# P1 — funzioni necessarie per essere competitivo

## P1.1 — Profili strumentali

Profili nominati per telescopio, camera, ruota portafiltri, rotatore, riduttore e
readout. L’app deve riconoscere possibili cambi di configurazione e chiedere se
creare una nuova firma ottica.

## P1.2 — Gestore alias filtri e target

Normalizzazione controllata di `Ha`, `H-alpha`, `HOO`, nomi dual-band e varianti
di target. Mostrare sempre valore originale e canonico. Nessuna fusione
automatica se l’alias potrebbe rappresentare filtri fisicamente diversi.

## P1.3 — Ciclo di vita dei Flat

Consentire di nominare un Flat Set, indicarne validità, note sulla rotazione,
smontaggio camera o polvere, e riutilizzarlo su più sessioni. Evidenziare quando
un Light nuovo non è coperto da alcun Flat Set approvato.

## P1.4 — Import N.I.N.A. avanzato

Import opzionale dei log e dei file di sequenza N.I.N.A. per confrontare header,
target pianificato, filtro, temperatura e cambio meridiano. I log sono una fonte
di conferma, non devono sovrascrivere gli header in silenzio.

## P1.5 — Controllo qualità dei frame

Modulo separato e opzionale che legge i pixel per calcolare statistiche, stelle,
FWHM, eccentricità, background e clipping. Deve rimanere distinto dal matching
di calibrazione e non rallentare la scansione header-only.

**Avanzamento 21 luglio 2026 — Quality Lab v1:** aggiunto un workspace completamente opzionale che legge i pixel solo su richiesta. Per ogni Light FITS misura background robusto, MAD/noise, segnale, SNR, stelle, FWHM ed eccentricità e costruisce una preview dell'intero frame. Gli outlier sono confrontati esclusivamente con frame della stessa coppia filtro/esposizione mediante mediane e deviazione assoluta mediana, evitando soglie assolute fragili. La tabella spiega l'anomalia dominante; Blink scorre prima i sospetti. L'utente può escludere un frame, tutti i sospetti o ripristinarli. Gli originali non vengono spostati: nell'export le copie escluse finiscono in `Excluded/Quality`, separate dai Light caricati in WBPP. La scansione può essere annullata. Restano decoder pixel XISF, persistenza/cache delle metriche, ROI configurabile, mappe tilt e selezione automatica della reference image.

**Avanzamento 21 luglio 2026 — Quality Lab v2:** eliminata la soglia nascosta. L'utente imposta la sensibilità tra 2 e 6 σ e vede immediatamente cambiare sospetti e distribuzione. Il grafico sovrappone istogramma, curva gaussiana di riferimento, soglia, selezione e punti dei singoli frame; un punto è cliccabile e apre la relativa preview. La tabella supporta Ctrl/Shift, esclusione mirata e Blink dei soli frame selezionati. La preview scelta viene rigenerata dal FITS in background con stretch asinh regolabile; per CFA dichiarati negli header è disponibile un debayer temporaneo RGGB/BGGR/GRBG/GBRG. Nessuna di queste operazioni riscrive o demosaicizza gli originali. Verificati su 24 Light reali il calcolo, la curva, la preview selezionata e il debayer. Restano curve separate per metrica/serie, cache persistente, XISF e mappe tilt.

**Avanzamento 21 luglio 2026 — workspace e ispezione v2.1:** Sorgenti e Inspector non hanno più larghezze bloccate: splitter persistenti permettono di assegnare spazio in base al monitor e continuano a integrarsi con i pulsanti mostra/nascondi. Nel Quality Lab un terzo splitter divide tabella e preview. La preview supporta zoom 5–1600%, rotella ancorata al cursore, pan mediante trascinamento, adattamento al viewport e una rigenerazione HD fino a 2400 px. Una sola preview HD viene mantenuta alla volta per evitare crescita incontrollata della memoria durante Blink o selezioni multiple.

**Avanzamento 21 luglio 2026 — Quality Lab v2.2 per serie:** il confronto statistico segue ora la struttura reale del progetto: `Filtro → Sessione di configurazione (Flat Set) → stessa esposizione`. Ogni sessione ha selezione, istogramma, curva, soglia e sospetti separati, quindi due cicli SIOIII con Master differenti non si contaminano. Le colonne metriche ordinano numericamente crescente/decrescente. Il grafico dispone di area più alta, griglia, assi, legenda e rug dei frame separato dalle barre. Il debayer temporaneo calcola black point e white point per ciascun canale, neutralizzando la dominante verde tipica del Bayer non bilanciato. L’Inspector viene rimosso dai workspace che non consumano override.

**Correzione di flusso 21 luglio 2026:** l’inventario delle serie Quality viene costruito dall’analisi degli header e delle assegnazioni di calibrazione, non più come effetto collaterale dell’analisi pixel. Il selettore è quindi disponibile entrando nel Lab e dichiara per ogni serie filtro, Flat Set/sessione, Light e notti. L’analisi è incrementale per singola serie, mostra le righe durante l’elaborazione e conserva i risultati già calcolati per le altre serie.

## P1.6 — Confronto sessioni

Vista a matrice con righe Sessione e colonne Filtro, ore totali, numero frame,
temperature, Flat Set e problemi. È il modo più rapido per capire cosa manca a
un progetto multisessione.

## P1.7 — Templates e preset

Preset esportabili per OSC, mono LRGB, narrowband, dual-band e mosaico. Ogni
preset definisce campi richiesti, tolleranze, struttura cartelle e checklist
WBPP, senza nascondere le decisioni.

## P1.8 — Report professionale

Report HTML/PDF con riepilogo per sessione, integrazione totale, firme sensore e
ottica, calibrazioni assegnate, override, hash, ricetta WBPP e anomalie accettate.

## P1.9 — Dashboard libreria Master

Copertura visuale per combinazione camera/gain/offset/temperatura/esposizione,
con evidenza dei buchi. Suggerire quali Dark/Bias acquisire, senza generare Master
o inventare compatibilità.

## P1.10 — Aggiornamento incrementale del progetto

Riaprire un progetto esistente, aggiungere una nuova notte e copiare soltanto i
file nuovi. Il manifest deve conservare la cronologia delle revisioni.

---

# P2 — evoluzioni successive al lancio

- Generazione opzionale di un processo/script PixInsight supportato e versionato.
- Plugin per nuovi software di acquisizione e schemi di cartelle.
- Librerie su NAS con watcher e sincronizzazione resiliente.
- Confronto qualità e selezione automatica della reference image.
- Pianificazione dei Dark mancanti in base ai progetti recenti.
- Mosaici e pannelli con regole `TARGET/PANEL` dedicate.
- Modalità portable ufficiale.
- CLI per utenti avanzati e automazioni locali.
- API locale documentata, senza servizio cloud obbligatorio.

---

# Piano di esecuzione consigliato

## Fase 1 — Fondazioni affidabili

Durata indicativa: 4–6 settimane.

- P0.1 progetto salvabile;
- P0.2 gestore librerie;
- P0.3 revisione guidata;
- P0.4 regole;
- P0.5 robustezza parser;
- prime fixture multiproduttore.

**Gate:** qualunque ambiguità del dataset di test è risolvibile e persistente.

## Fase 2 — Correttezza WBPP e sicurezza dati

Durata indicativa: 4–6 settimane.

- P0.6 firme scientifiche;
- P0.7 test WBPP end-to-end;
- P0.8 esportazione antifragile;
- P0.9 centro pulizia;
- P0.10 cache e scalabilità.

**Gate:** cinque progetti reali differenti completano WBPP con assegnazioni
corrette e superano i test di interruzione.

## Fase 3 — Esperienza commerciale

Durata indicativa: 3–5 settimane.

- P0.11 onboarding e accessibilità;
- P0.12 support bundle;
- P0.13 automazione QA;
- manuale utente e video introduttivo;
- localizzazione italiana/inglese.

**Gate:** beta privata completata da utenti che non hanno partecipato allo
sviluppo.

## Fase 4 — Distribuzione e beta

Durata indicativa: 3–4 settimane più periodo beta.

- P0.14 installer/firma/update;
- P0.15 sicurezza/privacy;
- P0.16 licensing/legal;
- beta chiusa su hardware e dataset differenti;
- correzione di tutti i difetti P0/P1 critici.

**Gate:** Release Candidate congelata per almeno due settimane senza crash o
regressioni critiche.

---

# Strategia beta consigliata

Reclutare almeno 15–25 tester distribuiti tra:

- camera mono e OSC;
- ZWO, QHY e almeno un altro produttore;
- N.I.N.A., ASIAIR e un secondo software desktop;
- progetti LRGB, narrowband, dual-band e mosaico;
- librerie Master piccole e molto grandi;
- Windows 10/11, dischi interni, USB e NAS.

Ogni tester deve fornire, con consenso, soltanto manifest e support bundle
redatti. Registrare tempo per completare il primo progetto, numero di ambiguità,
errori WBPP, crash e punti in cui è servito supporto umano.

## Metriche di uscita dalla beta

- almeno 50 progetti reali esportati;
- zero perdite o modifiche dei file sorgente;
- zero assegnazioni di calibrazione errate non segnalate;
- almeno 95% delle scansioni completate senza crash;
- almeno 90% dei tester completa il primo progetto senza assistenza diretta;
- tutti i crash P0 riproducibili chiusi;
- tempo medio per organizzare un progetto sensibilmente inferiore al flusso
  manuale dichiarato dai tester.

---

# Packaging commerciale minimo

Prima di aprire le vendite devono esistere:

- sito con descrizione precisa e requisiti;
- pagina di download e verifica firma/versione;
- manuale rapido e manuale completo;
- dataset demo redistribuibile;
- changelog e politica aggiornamenti;
- sistema ticket/email di supporto;
- FAQ su WBPP, Flat Set, librerie e notte astronomica;
- trial senza richiesta di carta, se sostenibile;
- licenza acquistabile e recuperabile;
- procedura pubblica per segnalare bug e vulnerabilità;
- backup del servizio licenze e piano di continuità.

## Possibile struttura dell’offerta

Per il lancio è preferibile una sola edizione completa: riduce supporto e
confusione. Una futura edizione Pro può includere qualità dei frame, CLI,
automazioni e dashboard avanzata. Correttezza del matching, copia verificata,
privacy e recupero progetto non devono mai essere funzioni premium.

---

# Ordine assoluto delle prossime dieci attività

1. Definire e implementare il formato progetto `.astroforge` con autosave.
2. Costruire la Coda di revisione con scelta manuale dei candidati.
3. Separare e rendere multiple le librerie Dark/Bias/Dark-flat.
4. Introdurre editor e persistenza delle regole con scope.
5. Creare dataset golden multiproduttore e test del parser.
6. Completare Dark-flat e firme scientifiche di compatibilità.
7. Automatizzare i test WBPP end-to-end sui casi fondamentali.
8. Rendere l’esportazione una macchina a stati con preflight completo.
9. Aggiungere cache SQLite, support bundle e gestione errori globale.
10. Preparare installer firmato, aggiornamenti, beta, licenze e documentazione.

Finché i primi otto punti non sono chiusi, aggiungere funzioni spettacolari ma
non critiche aumenterebbe il rischio senza rendere davvero vendibile il prodotto.
