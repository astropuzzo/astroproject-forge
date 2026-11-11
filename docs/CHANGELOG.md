# AstroProject Forge — note di rilascio

## 1.14.0 — 11 novembre 2026

Revisione dell’algoritmo e dell’interfaccia: tutto il codice è stato riletto, ogni difetto trovato è stato corretto e, dove possibile, fissato con un test.

**Calibrazioni**

- **Dark e Bias grezzi.** Una cartella con trenta Dark da 300 s e nessun Master (il caso tipico di un ASIAIR) chiedeva una scelta per ogni Light, e il sub scelto veniva esportato da solo con il nome MasterDark. Ora i sub con la stessa camera, gain, offset, esposizione e temperatura formano un set: l’abbinamento avviene sul set e il set viene esportato intero, perché sia WBPP a integrarlo.
- **Dark Flat.** Venivano riconosciuti e poi ignorati, e senza Bias il progetto non era mai pronto. Ora si abbinano al set di Flat per camera, gain ed esposizione, arrivano a WBPP come Dark dei Flat, e un progetto con Dark, Flat e Dark Flat è completo anche senza Bias. Se c’è anche il Bias vengono esportati entrambi.
- Tra due Dark o due Bias che andrebbero bene vince il più vicino per esposizione e temperatura: un Light a −10 °C con Master a −10 e a −8 °C non chiede più di scegliere. Un Master completo passa davanti a uno senza offset che aveva solo più punti per la libreria.
- Camere a colori senza ruota: Light e Flat senza FILTER ora si abbinano (prima il filtro risultava un dato mancante e i Flat non si assegnavano mai).
- Un Bias a qualche grado dal Light viene accettato, e tra due si preferisce il più vicino: prima era “incompatibile” e bloccava il progetto.
- Un Flat Set assegnato a una parte della notte resta a quella parte: prima, secondo l’ordine dei file, veniva ignorato o esteso a tutta la notte.
- Un vecchio stack rimasto in una cartella (un master Light) non viene più calibrato e integrato di nuovo, e i master Flat non si mescolano ai Flat grezzi.
- Assegnare a mano un Master senza gain o offset chiede di completare i metadati, invece di far fallire l’esportazione.

**Date, notti e statistiche**

- **DATE-OBS è UTC.** Lo standard FITS lo vuole in tempo universale, ma veniva letto come ora locale: i frame con il solo DATE-OBS (ASIAIR) finivano una o due ore prima sulla linea delle notti, come si vedeva anche nel progetto demo.
- **Siti remoti.** Quando il file porta sia DATE-LOC sia DATE-OBS (N.I.N.A., SGP, APT) la differenza dà il fuso del sito, e la notte si taglia sul mezzogiorno del sito: un osservatorio in un altro fuso non viene più spezzato in due notti.
- Le notti del progetto si contano una volta: due notti con tre filtri risultavano sei notti. “Ha” e “HA” sono lo stesso filtro.
- DATE-OBS con la sola data e l’ora in TIME-OBS (software più vecchi) vengono uniti.

**Lettura dei file**

- N.I.N.A. senza ruota portafiltri: nei nomi file la temperatura, il bersaglio o “FlatWizard” venivano presi per il filtro, e Light e Flat non si incontravano più.
- “Dark_Flat”, “Flat-Dark”, “FlatDark” e IMAGETYP “FLATDARK” sono Dark Flat (prima diventavano Flat).
- I Master con nome attaccato (“masterDark_300s”, “MasterFlat”) vengono riconosciuti anche senza IMAGETYP.
- Libreria Master: la cartella “Temp-10” vale −10 °C (veniva letta +10), “Temp-10C” viene letta, e una cartella con la temperatura sotto “GAIN_100” non richiede più una cartella chiamata “dark”.
- Una sottocartella illeggibile (per esempio “System Volume Information” sulla radice di un disco) non interrompe più l’intera analisi; i file “._” lasciati da macOS vengono saltati.
- FITS: le stringhe lunghe spezzate in card CONTINUE vengono ricomposte. XISF: l’esposizione si legge anche dalla proprietà standard. Un FOCALLEN a zero lascia la focale del catalogo.

**Quality Lab**

- **FWHM.** La misura usava una finestra fissa di 11 pixel e una soglia: oltre circa 4 pixel smetteva di crescere, e una stella più debole risultava più stretta, quindi un frame velato poteva sembrare più nitido. Ora ogni stella fissa la propria scala e la larghezza viene da un fit gaussiano ellittico. Su stelle sintetiche da 2,4 a 11,8 pixel, con rapporto segnale/rumore 10, 30 e 300, l’errore resta entro il 10% e non dipende dalla luminosità.
- I Light esclusi nel Quality Lab non bloccano più l’esportazione e non entrano nelle statistiche esportate.

**Esportazione e WBPP**

- Un progetto con uno spazio nel nome (“NGC 6960”) viene esportato in “NGC_6960”, ma le novità venivano cercate in “NGC 6960”: dopo un’esportazione ogni notte aggiunta risultava un progetto intero da copiare.
- I nomi delle cartelle usano gli stessi caratteri sicuri su Windows, macOS e Linux, e i due punti non restano più nelle cartelle TARGET (un progetto fatto su macOS doveva aprirsi anche su Windows e su dischi exFAT).
- Aggiornare un progetto su un disco exFAT, FAT o HFS+ segnalava ogni file come modificato, perché quei dischi arrotondano l’ora di modifica. Ora il manifest registra l’ora che la copia ha davvero ricevuto.
- Istanza WBPP: FLATSET, DARKSET e BIASSET vengono dal piano e non dal nome della cartella in cui un file era rimasto da un’esportazione precedente. I Flat di uno stesso set con esposizioni diverse (sky flat, due serie di pannello) restano un solo gruppo.
- Riprendere una prima esportazione interrotta dopo aver cambiato il piano non porta più nel progetto le copie del piano vecchio.
- La radice di un disco contiene le proprie cartelle: con le acquisizioni su “E:\” il controllo di sovrapposizione veniva saltato e si poteva esportare dentro le sorgenti.

**Cielo**

- RA scritta come testo (“22 47 14”) è in ore, non in gradi. Un bersaglio (0, 0), cioè “sconosciuto”, lascia il posto alla posizione della montatura invece di far sparire il frame. Le coordinate non mostrano più “20h 60.0m”.

**Aggiornamenti**

- Se una delle due fonti (feed del canale o GitHub) va in timeout o risponde con dati non validi, l’altra viene comunque usata.

**Uso quotidiano**

- Una sorgente su un disco non collegato all’avvio (un NAS, un disco USB) spariva dal progetto e dal file salvato al primo salvataggio. Ora resta elencata.
- Un’analisi alla volta: si poteva avviarne una seconda mentre la prima era in corso, e un’analisi letta per sorgenti cambiate nel frattempo veniva mostrata come il progetto. Ora le richieste aspettano quella in corso, e un risultato superato viene riletto.
- I Light messi da parte nel Quality Lab escono subito da ciò che è pronto, dalle scelte da fare e dai totali. “Ripristina tutti” riprende anche le esclusioni fatte prima dell’ultima analisi.
- “Annulla” dopo una nuova analisi agiva sui frame vecchi e diceva di aver funzionato: ora la cronologia delle modifiche riparte con l’analisi.
- “Tutta la notte” per un Dark o un Bias raggiungeva anche Light con un’altra esposizione o un altro gain, che diventavano incompatibili.
- Svuotare un campo toglie la correzione e torna al valore del file, invece di registrare “nessun valore”.
- La linea delle notti segue l’orologio del sito, come le notti: i Light di un osservatorio remoto non cadono più fuori dal riquadro.
- Un file di progetto che non si riesce a scrivere non resta più quello su cui ogni azione successiva prova a salvare. Un’azione che fallisce mostra un avviso con il motivo, invece di finire solo nel registro.
- Nelle viste delle calibrazioni i Dark Flat e i set di Bias grezzi sono chiamati con il loro nome, e una calibrazione senza differenze è segnata come identica (prima bastava un campo facoltativo assente, come il rotatore, per farla risultare “entro la tolleranza”).

**Interfaccia**

- Via la riga “PASSO N DI 4” sopra ogni titolo, che ripeteva la barra dei passi, e via le etichette tutte in maiuscolo: titoli di scheda, intestazioni e stati sono scritti normalmente, in italiano e in inglese.
- Un solo formato per numeri, percentuali, durate e dimensioni, nella lingua dell’interfaccia. Prima convivevano “0.3 h” e “0,3 h”, “6.14 MB” e “6 MB”, “20 min” e “40m”, “100 %” e “100%”.
- Una parola per ogni cosa: i filtri sono “da riconoscere”, la libreria è “Libreria Master”, il passo è “Strumento”, i file sono “acquisizioni”, il filtro è Hα. I conteggi hanno singolare e plurale veri (“1 filtro/i” non c’è più). Su un computer si “clicca”, non si “tocca”.
- Testi che venivano tagliati: la nota sotto il cielo, la scritta del trascinamento, le intestazioni della Libreria Master. “Campo inquadrato · Filtro Filtro 3” ripeteva la parola.
- Il pulsante del catalogo accanto a Camera e Ottica era un puntino quasi invisibile: ora è un pulsante “Catalogo”. Il nome del riduttore non ripete più il fattore.
- Passo Importa: le due schede non hanno più un’altezza minima che spingeva “Opzioni avanzate” fuori dallo schermo.
- Passo Calibrazioni: un solo anello (il secondo ripeteva il primo), una legenda per i segni delle celle, e il titolo “Da risolvere” compare solo quando c’è qualcosa da risolvere.
- Passo Esporta: la scheda di WBPP sta sotto le notti, così le due colonne hanno altezze simili; nome del progetto e destinazione hanno un’etichetta; i riquadri del riepilogo compaiono quando hanno un dato e “Controlla prima” sparisce a esportazione finita.
- Aspetto: il pulsante principale è in tinta unita (via il gradiente azzurro-lilla), via bagliori e ombre da schede e pulsanti, tre soli raggi per gli angoli. Il diagramma di WBPP è fatto di corsie dritte, senza nastri ondulati che non dicevano nulla. Via la stellina dalla barra delle novità e i puntini decorativi. Nessuna animazione gira in continuo tranne le stelle dello sfondo, che “Riduci animazioni” ferma.
- Caratteri: ciascuno ha un compito solo. Il testo e i titoli in Inter, Unbounded per il nome e i numeri grandi, il corsivo con grazie per il sottotitolo della pagina, il monospazio per keyword e valori misurati.
- Leggibilità: nessun testo sotto gli 11 pixel, e il contrasto dei testi secondari supera 4,5:1 anche sul punto più chiaro dello sfondo.
- La mappa delle calibrazioni si usa anche da tastiera, con le frecce e Invio.
- “Dimensione interfaccia” veniva salvata e mai applicata: ora ridimensiona l’interfaccia.
- I messaggi che comparivano solo in italiano anche con l’interfaccia in inglese (avvisi, riepiloghi, albero dei metadati, errori) hanno la loro versione inglese. Dal dizionario sono uscite 447 voci che nessuna schermata usava più.

**Controlli QA**

- Nuovo gruppo di test del Core per ogni correzione: stelle sintetiche per la FWHM, ora di scatto e siti remoti, spareggi tra Dark, nomi file, cartelle della libreria, card FITS e XISF, regole di abbinamento, coordinate, e un’esportazione vera con Dark e Bias grezzi fino all’istanza WBPP.
- Le immagini dei quattro passi nella documentazione sono state rifatte sull’interfaccia nuova.

## 1.13.1 — 1 ottobre 2026

- **Passo Esporta: la colonna di sinistra non si stira più.** Con l’esportazione finita la colonna di destra (mappa dell’export e WBPP) diventa molto più alta, e aprendo “Grouping Keywords da inserire a mano” cresceva ancora: la colonna di sinistra si allungava insieme, così si muoveva e si ingrandiva soltanto l’immagine dell’integrazione, e sotto restava un riquadro delle notti quasi vuoto. Ora le due colonne hanno ciascuna la propria altezza: l’anteprima dell’integrazione e le notti restano com’erano, qualunque cosa succeda a destra.
- Aprendo la tabella delle Grouping Keywords la pagina scorre fino a mostrarla per intero, e le sue righe si leggono anche in inglese (prima restavano in italiano finché non si cambiava passo).
- Il diagramma di WBPP non sovrappone più le etichette dei passaggi: “LIGHT / dal progetto” non finisce sotto “CALIBRAZIONE”.
- Sotto 1320 px di larghezza il passo Esporta mette le due colonne una sopra l’altra, con l’esportazione per prima, così il diagramma e la tabella hanno lo spazio per essere letti.
- Controlli QA: lo smoke test misura che, aprendo la tabella delle keyword, l’anteprima dell’integrazione e la card delle notti mantengano la propria altezza; la cattura delle schermate salva anche la pagina con la tabella aperta.

## 1.13.0 — 1 ottobre 2026

- **Più setup in un progetto.** Prima la pagina Strumento descriveva solo il treno principale (la camera con più Light) e nascondeva il resto in silenzio. Ora un progetto che mescola camere o focali (due camere, o la stessa camera con e senza riduttore) offre una scheda per ogni setup: camera, ottica, focale, Light, ore e notti. Scegliendone una la pagina descrive quel setup, con la sua camera, la sua ottica, il suo riduttore e la sua ruota portafiltri (anche con otto filtri e più).
- Le correzioni restano di chi le riceve: la camera e la dimensione del pixel valgono per la camera, ottica e riduttore per il setup. Il setup principale di una camera tiene la chiave di sempre, quindi ciò che avevi già detto continua a valere per lui; l’ottica detta per il primo setup non viene presa per quella del secondo. Un solo setup resta un solo setup, e nulla cambia per i progetti di sempre.
- Sul cielo reale, accanto al riquadro del setup scelto, gli altri setup compaiono tratteggiati, nel colore della loro scheda e con la propria focale e il proprio campo (un obiettivo corto accanto a un telescopio, per esempio). Un setup che ha puntato un altro bersaglio non viene disegnato.
- I Flat seguono il setup di cui portano la focale; un Flat senza focale appartiene a tutti i setup della sua camera.
- Controlli QA: test del Core con tre setup sintetici (stessa camera su 800 e 560 mm, un’altra camera a 250 mm) e con le correzioni dell’utente per camera e per setup; lo smoke test aggiunge una seconda camera con otto filtri, sceglie ciascun setup, controlla ruota, focale, cielo con l’altro setup e che ciò che si dice su uno non finisca nell’altro.

## 1.12.1 — 1 ottobre 2026

- Il riquadro del **Campo inquadrato** ora sta dove sono state riprese le stelle. Sui Light veri di una Nebulosa Mago (N.I.N.A., montatura con ribaltamento del meridiano) le coordinate RA/DEC della montatura sbagliavano fino a mezzo grado dopo il ribaltamento, mentre il Light inquadrava lo stesso campo: confrontando le stelle del Light con il DSS2 il bersaglio (OBJCTRA/OBJCTDEC) cadeva entro un primo. Ora la posizione si legge, in ordine: WCS risolto, **bersaglio**, RA/DEC della montatura; se la montatura si scostava di più di un decimo di grado la scheda lo dice.
- La rotazione viene dal **rotatore** (ROTATOR, poi ROTATANG) quando c’è, non da OBJCTROT: N.I.N.A. ci scrive sempre 0, e un OBJCTROT esattamente 0 ora vuol dire “non impostato”. Il riquadro è lo stesso girato di mezzo giro, quindi il lato del meridiano non cambia l’inclinazione. La scheda dice da dove vengono posizione e angolo (WCS, rotatore, bersaglio, montatura).
- Il segno del bersaglio (cerchio con croce) compare quando i frame non sono centrati su di esso.
- La **Luna** (0,5°) è disegnata in scala accanto al riquadro, per capire a colpo d’occhio quanto cielo si sta riprendendo; con un campo stretto diventa un anello tratteggiato sopra il riquadro.
- Controlli QA: il test del Core usa gli header veri del caso sopra (bersaglio contro montatura, rotatore a 181,57°) e verifica che un angolo e il suo opposto siano la stessa orientazione.

## 1.12.0 — 1 ottobre 2026

- Il **Campo inquadrato** mostra il cielo vero. Dietro il riquadro del sensore c’è ora la foto DSS2 del tuo bersaglio (dal CDS di Strasburgo), centrata dove puntavano i Light, e il riquadro sta al suo posto, in scala, ruotato come la camera (angolo dal WCS risolto o dalla rotazione dichiarata; se non c’è, resta dritto e lo dice). Al posto della nebulosa finta di prima: con una scala in gradi, la bussola (nord in alto, est a sinistra), il nome del bersaglio e le coordinate del centro.
- Le coordinate si leggono dagli header, in ordine di fiducia: WCS risolto (CRVAL), RA/DEC della montatura, OBJCTRA/OBJCTDEC; gradi o sessagesimali. I Light che puntano allo stesso posto sono un pannello; un mosaico mostra un riquadro per pannello (P1, P2…); un altro bersaglio nello stesso progetto non entra.
- Privacy e rete: per scaricare la foto partono solo la posizione e la dimensione del campo, mai file, nomi o dati del progetto. Le foto restano in una cache su disco (le ultime 60), quindi un campo già visto si apre subito e senza rete. Dal Menu si spegne (**Mostra il cielo reale nel campo inquadrato**). Senza coordinate, senza rete o con il cielo spento il riquadro resta sul posto su un cielo vuoto e una riga dice perché.
- Controlli QA: test del Core per coordinate, angolo di posizione, proiezione, pannelli del mosaico, richiesta, cache e modalità offline (`ASTROFORGE_OFFLINE=1`); lo smoke test non usa mai la rete e serve un’immagine finta.

## 1.11.2 — 1 ottobre 2026

- Il Menu torna ben visibile: un pulsante con il suo nome nell’intestazione, accanto a Strumenti e alla ricerca (Ctrl K), con lingua, aggiornamenti, aiuto e progetto. Nella 1.11.0 e nella 1.11.1 si apriva solo cliccando sul logo, che non sembra un pulsante, e Strumenti usava proprio l’icona a tre righe del menu. Ora Strumenti ha la sua icona; a finestra stretta Menu e Strumenti restano come icone e il tour li presenta.

## 1.11.1 — 1 ottobre 2026

- Filtri del catalogo: il colore non è più nero. Per ogni filtro scelto dal catalogo (per esempio l’Anti-Halo PRO Dual-Band) lo slot della ruota, la striscia dello spettro e le schede prendevano la trasmissione di picco (0,85) per una lunghezza d’onda e ne ricavavano un nero; ora il colore viene dal centro delle bande, e un dual-band mostra un colore per riga (OIII e Hα).
- Le bande di un filtro si vedono per quanto sono larghe: nella striscia dello spettro hanno la larghezza vera e sotto ognuna delle bande strette compare un riquadro ingrandito di pochi nanometri, con la riga di emissione che contiene, la larghezza (per esempio 3,2 e 3,7 nm), la trasmissione e le tacche di un nanometro. Le etichette dicono la lunghezza d’onda giusta (500,7 e 656,3 nm) invece di 0,9.
- Camera e ottica: il pulsante ▾ apriva un elenco di una sola riga, perché il campo filtrava per il nome già scritto e non c’era niente da scorrere. Ora apre il catalogo intero (792 ottiche, 299 camere) in un menu con ricerca (ogni parola scritta deve essere nel nome, in qualsiasi ordine), apertura, lunghezza focale e barra di scorrimento; scegliere una voce la applica e chiude il menu.

## 1.11.0 — 1 ottobre 2026

- Un solo percorso, quattro pagine in ordine: Importa, Strumento, Calibrazioni, Esporta. Il passo corrente è sempre in vista nella barra in alto, ogni passo finito si segna con una spunta e uno che lascia qualcosa di aperto diventa ambra.
- Un solo pulsante in basso a destra, sempre nello stesso punto, fa la prossima cosa che serve (Analizza, Continua, Risolvi, Esporta, Apri in PixInsight); la riga accanto spiega perché. Indietro è sempre disponibile.
- Dopo l’analisi il progetto prosegue da solo allo Strumento; i pannelli che nascondevano i passi sono spariti. Statistiche, Qualità, Frame e metadati e verifica della Libreria Master sono strumenti in alto a destra.
- La Libreria Master è la prima scheda della pagina Importa, grande quanto le acquisizioni, con elenco, priorità e stato; resta salvata per ogni progetto. Anche le acquisizioni si rimuovono con un clic.
- Strumento: camera, ottica, riduttore e pixel si possono sempre correggere (**Cambia** o un tocco sul treno ottico), anche quando Forge li ha riconosciuti. Nuovo: puoi dire quale **camera** ha scattato davvero; tutti i frame di quella camera (Light, Flat e Master) usano il nome scelto, così Dark e Bias si abbinano anche con un nome di driver inutile negli header.
- Anche i filtri già riconosciuti si possono cambiare scegliendo quello giusto dal catalogo; la scelta resta salvata per la camera e si può annullare.
- Calibrazioni: anello e quota per filtro, scelte aperte una alla volta con il candidato consigliato, mappa completa e dettagli su richiesta.
- Esporta: l’integrazione simulata notte per notte, nome, destinazione e un clic; a esportazione finita la scheda PixInsight WBPP viene in primo piano con il pulsante per aprire l’istanza.
- Nuovi dati in un progetto già letto o esportato: aggiungere o togliere una cartella, un file o una libreria rilegge il progetto sul posto, senza svuotare i passi né riportarti all’inizio. Una barra segnala le novità (notti nuove, Light in più su notti già presenti, filtri nuovi, Flat, ore in più) rispetto all’ultima esportazione, anche tra una sessione e l’altra; la riconosce dal manifest del progetto.
- Aggiornamento del progetto: la pagina Esporta diventa “Aggiungi le novità al progetto”, il pulsante dice quanti dati nuovi si copiano (non l’intero progetto), i rami dell’albero che portano file nuovi si illuminano con “+N nuovi” e gli altri restano in secondo piano; nella timeline la notte nuova è evidenziata.
- Timeline delle notti: la luna è segnata sulla notte, con una banda dove sta sopra l’orizzonte (più marcata quanto più è piena), glifo di fase più grande e orari di levata e tramonto nel tooltip. Usa SITELAT/SITELONG degli header; senza posizione stima un sito tipico dal fuso orario e lo dice.
- Catalogo: camere (299) e telescopi (792) del catalogo si sfogliano con il pulsante ▾ oltre che scrivendo; i filtri (105) restano nel menu a tendina.
- Trascina e rilascia: cartelle e file FITS/XISF si possono rilasciare su qualsiasi punto della finestra, in qualunque passo. Compaiono due grandi zone, Libreria Master a sinistra e acquisizioni a destra, e si illumina quella sotto il puntatore. Un Master trascinato sulla Libreria collega la sua cartella; doppioni e sottocartelle di una cartella già trascinata non si aggiungono due volte; ciò che non è FITS o XISF è lasciato fuori e il messaggio lo dice. Vale anche sul benvenuto del primo avvio.
- Benvenuto ridotto a due schermate (lingua e come funziona). Finestre strette: ogni pagina scorre come un blocco e l’intestazione rinuncia alle parole, non ai passi.
- Controlli QA: lo smoke test verifica avanzamento automatico, cambio di un filtro riconosciuto e della camera; test del Core per la camera reale; `ASTROFORGE_DATA_DIR` sposta la cartella dati (test e schermate non toccano più i dati dell’utente).

## 1.10.0 — 1 ottobre 2026

- Finestra ridisegnata: a sinistra i quattro passi (Progetto, Risolvi, Esporta, PixInsight WBPP), in alto un solo pulsante con la prossima azione.
- Panoramica: anteprima d’integrazione con palette HOO/SHO e riproduzione notte per notte, ore per filtro, anello delle calibrazioni, notti con luna e crepuscolo, prima scelta da risolvere.
- Strumento: campo inquadrato in scala con e senza riduttore, ruota portafiltri, treno ottico e profilo strumento modificabile (ottica, riduttore, pixel), salvato per camera.
- Progetto, Risolvi, Esporta, WBPP, Statistiche, Qualità, Libreria Master e Log si aprono come pannelli; Ctrl K apre tutti i comandi.
- Frame ricampionati letti con il passo pixel nativo della camera.

## 1.9.0 — 30 settembre 2026

- Nuova interfaccia “Cielo profondo”, identica su Windows, macOS e Linux.
- Nuove schermate Panoramica e Strumento; i filtri si confermano una sola volta per progetto.
- Catalogo filtri, camere e telescopi con riconoscimento dai nomi AstroBin e dalle intestazioni FITS/XISF; i frame sono archiviati sotto il filtro fisico.
- Lettura corretta del filtro ASIAIR quando segue la temperatura nel nome file.
- Mappa delle calibrazioni in Risolvi, flusso dei Light in WBPP, avanzamento dell’esportazione sull’albero del progetto.
- Qualità mostra ogni serie come un piccolo cielo; la libreria Master mostra la copertura Dark rispetto ai Light.
- Tour guidato sui comandi reali e progetto demo (Cygnus Loop) con un clic.
- Testi dell’interfaccia riscritti: più brevi e diretti.
- Scansione delle cartelle e analisi delle calibrazioni più veloci.
- Cartella dati dell’app sempre assoluta e scrivibile per utente.
- Windows usa ora la stessa app Avalonia di macOS e Linux (`AstroProjectForge.exe`); l’app WPF `AstroForge.App` è stata ritirata.
- Il setup Windows rimuove `AstroForge.App.exe` e le DLL WPF durante l’aggiornamento e ripunta collegamenti e associazione `.astroforge` al nuovo eseguibile; le installazioni precedenti restano riconosciute.
- Aprire un file `.astroforge` da Esplora file apre direttamente il progetto.
- Su Windows l’aggiornamento dall’app installa il setup con avanzamento visibile e riavvia l’app, come nella versione WPF.

## 1.8.2 — 20 settembre 2026

- “Calibrazioni” diventa “Risolvi”: la pagina ora permette di correggere direttamente assegnazioni mancanti o ambigue.
- Le segnalazioni ripetute sono raggruppate per filtro, notte, configurazione e candidati compatibili.
- I Flat Set ambigui possono essere assegnati al gruppo, all’intera notte oppure alle acquisizioni con la stessa configurazione e filtro.
- Dark e Bias possono essere assegnati dalla stessa pagina; importazione, libreria Master e modifica metadati compaiono soltanto quando pertinenti.
- Il collegamento Flat manuale resta disponibile nell’Inspector come strumento avanzato, chiuso per impostazione predefinita.
- Eliminata la ritraduzione completa dell’interfaccia a ogni variazione dei dati, che rallentava navigazione e analisi dei progetti grandi.
- Aggiornate le traduzioni e mantenuta la stessa esperienza su Windows, macOS e Linux.

## 1.8.1 — 19 settembre 2026

- Corrette le istanze WBPP vuote: il file XPSM contiene ora l’icona collegata e i gruppi nel formato richiesto da PixInsight.
- Il pulsante WBPP apre direttamente la finestra con file, calibrazioni, keyword e destinazione. L’elaborazione parte soltanto premendo Run in PixInsight.
- Dopo un aggiornamento incrementale vengono usati i percorsi effettivi dei file esportati, anche se il raggruppamento è cambiato.
- I file mancanti vengono segnalati prima di generare l’istanza; i Light esclusi non vengono aggiunti.
- Lo stesso comando di apertura è disponibile nelle versioni Windows, macOS e Linux. Apertura verificata su Windows con PixInsight 1.9.5 / WBPP 3.1.0.

## 1.8.0 — 17 settembre 2026

- Aggiornamenti incrementali immediati: i file già registrati non vengono più riletti o ricalcolati.
- I nuovi file vengono copiati e indicizzati in un solo passaggio.
- Cartella risultati PixInsight opzionale e configurabile per ogni progetto.
- Generazione di un’istanza WBPP `.xpsm` con file, Master, gruppi, keyword e output già impostati.
- Apertura diretta dell’istanza in PixInsight dalla pagina WBPP.

## 1.7.2 — 9 settembre 2026

- Corretto l’arresto con recovery aprendo Dati, Qualità o Master dalla barra superiore.
- Rimossi dall’interfaccia i parametri tecnici di spazio e velocità e i comandi pausa/riprendi dell’esportazione.

## 1.7.1 — 7 settembre 2026

- La navigazione resta su una sola riga e non cambia più ordine quando lo spazio si riduce.
- I quattro passaggi principali hanno etichette brevi e numerate.
- Dati, Qualità e Master restano sempre visibili nella barra superiore senza comprimere il workflow.
- L’Inspector resta chiuso finché non viene selezionato un elemento modificabile.
- Rimossi i testi grigi che apparivano come intestazioni scollegate sopra il workflow.

## 1.7.0 — 6 settembre 2026

- Navigazione riorganizzata in quattro passaggi: Progetto, Calibrazioni, Esportazione e PixInsight WBPP.
- Statistiche, Qualità e Master Library sono separate visivamente dal percorso principale e marcate come strumenti opzionali.
- Lo stato superiore indica la prossima azione concreta invece di riepiloghi generici.
- La pagina Dati non mostra più schede a zero prima dell’analisi: presenta una sola azione utile.
- Titoli e testi dell’esportazione ridotti; parametri tecnici spiegati con tooltip.
- README e Wiki riscritti attorno allo stesso percorso operativo.

## 1.6.1 — 6 settembre 2026

- All’avvio, Windows mostra un avviso quando è disponibile una versione più recente e può aprire direttamente il pannello di aggiornamento.
- macOS e Linux aprono automaticamente il menu sull’aggiornamento disponibile e portano il comando di installazione in primo piano.
- Nessun avviso viene mostrato quando la versione installata è già aggiornata.
- Esportazione ripulita da termini tecnici e testi ripetitivi; le opzioni di spazio e velocità sono spiegate tramite tooltip.
- Documentata l’installazione delle build macOS non notarizzate senza disattivare Gatekeeper.

## 1.6.0 — 6 settembre 2026

- L’aggiornamento di un progetto riconosce i file già esportati tramite origine e SHA-256, anche quando una nuova sessione cambia la struttura calcolata.
- I file invariati restano nella posizione esistente e non vengono duplicati; i conflitti con file modificati vengono bloccati prima della copia.
- Quality Lab usa fondo locale, massimi a otto vicini e controlli contro hot pixel, raggi cosmici e stelle sature.
- Le misure con copertura insufficiente non vengono più etichettate automaticamente come frame cattivi.
- La segnalazione automatica richiede una seconda metrica coerente, salvo anomalie estreme.
- L’updater multipiattaforma seleziona DMG Apple Silicon/Intel e pacchetti DEB Linux x64/ARM64 dalla stessa release GitHub.
- macOS e Linux mostrano avanzamento e aprono il pacchetto verificato; l’installazione resta soggetta alla conferma prevista dal sistema operativo.
- Navigazione Windows rinominata con termini operativi più chiari: Progetto, Calibrazioni, Controllo qualità, Esportazione e PixInsight WBPP.

## 1.5.0 — 4 settembre 2026

- Le cartelle progetto già esportate possono essere aggiornate senza ricopiare l’intero dataset.
- Prima della copia vengono distinti file nuovi, file invariati e conflitti reali tramite SHA-256.
- I file invariati vengono riutilizzati, quelli nuovi aggiunti e qualsiasi contenuto diverso con lo stesso percorso viene bloccato senza sovrascritture silenziose.
- Ogni aggiunta significativa alimenta una cronologia con data, filtro, sessione, numero di Light, calibrazioni, integrazione aggiunta e totale cumulativo.
- La timeline è visibile nell’app e salvata nel progetto come `export-history.json` e `export-history.md`.
- Il manifest di esportazione passa allo schema 3 e conserva anche i record precedenti non inclusi nell’aggiornamento corrente.
- Windows, Linux e macOS condividono lo stesso flusso di aggiornamento incrementale.

## 1.4.0 — 3 settembre 2026

- Aggiunto un ciclo completo per i progetti: **Nuovo**, **Apri**, **Salva** e
  **Salva con nome**, disponibile dalla barra superiore, dal menu e da tastiera.
- Il nuovo formato progetto v2 conserva sorgenti, destinazione, regole, correzioni ed
  esclusioni, ma non incorpora le Master Library, che restano impostazioni globali.
  I progetti v1 continuano ad aprirsi.
- Quality Lab misura aree distribuite su tutto il sensore invece di un solo ritaglio
  centrale e analizza i sensori Bayer su una luminanza 2×2 temporanea.
- Gestiti i pixel FITS non numerici; ridotti falsi rilevamenti e duplicati di stelle.
- La soglia qualità ora confronta direttamente la peggiore deviazione robusta: grafico,
  contatore, tabella e lista dei sospetti applicano la stessa regola.
- Aggiunta l'analisi di tutte le serie in sequenza. Ogni filtro, Flat Set, esposizione e
  sessione resta un confronto indipendente.
- Rimosso il grafico gaussiano dall'interfaccia multipiattaforma e uniformato il grafico
  operativo a Windows, con regioni accettate/da verificare e punti selezionabili.
- Migliorati adattamento dell'intestazione, larghezza del pannello Sorgenti, conferme
  bilingui e testi autore essenziali.

## 1.3.0 — 29 luglio 2026

- L'inglese è ora la lingua predefinita per le nuove installazioni.
- Il primo passaggio della configurazione iniziale chiede esplicitamente se usare
  English o Italiano e applica subito la scelta.
- Gli utenti provenienti da una versione precedente vedono il nuovo setup una sola
  volta, senza perdere percorsi, librerie o impostazioni.
- Il pannello Sorgenti è più largo, riconoscibile e ordinato: importazione cartelle
  in evidenza, azioni secondarie separate, contatore e percorsi più leggibili.
- La regola della notte osservativa include una spiegazione completa e un esempio
  sera/mattina, con ora limite configurabile.

## 1.2.0 — 29 luglio 2026

- Il primo avvio usa ora un setup rapido in quattro passaggi: introduzione, Master
  Library facoltativa, importazione FITS/XISF e riepilogo.
- Dal passaggio finale è possibile avviare immediatamente l'analisi del progetto.
- La configurazione iniziale può essere riaperta in qualsiasi momento dal menu.
- La Master Library è ora presentata come funzione principale, con descrizione,
  conteggio e un comando di aggiunta ben visibile nel pannello Sorgenti.
- Lo stesso flusso è disponibile nelle interfacce Windows, Linux e macOS.

## 1.1.2 — 28 luglio 2026

- Anche gli aggiornamenti avviati dalle versioni precedenti mostrano ora una finestra
  di avanzamento compatibile, con fase corrente e percentuale.
- Le versioni recenti continuano a usare direttamente la finestra nativa di Inno Setup.

## 1.1.1 — 28 luglio 2026

- Durante l'aggiornamento automatico Windows l'installer mostra ora una finestra con
  avanzamento reale invece di lavorare in modo completamente invisibile.
- Download, verifica, installazione e riavvio sono riconoscibili come fasi distinte.
- Gli errori dell'installer non vengono più nascosti.

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
