# Flat Epoch — progettazione dell’assegnazione Flat multisessione

## Problema

`FILTER=HOO` non dimostra che due Light debbano usare lo stesso Flat. Lo stato
reale dipende dall’intera catena ottica: camera, orientamento, fuoco, filtro,
polvere, smontaggi e pulizie. Alcuni cambiamenti sono presenti negli header;
altri, come la pulizia del vetro o dello specchio, sono completamente invisibili.

Per questo AstroProject Forge non deve associare i Flat usando soltanto filtro o
data. Introduce il concetto di **Flat Epoch**: intervallo durante il quale la
risposta ottica è considerata invariata.

## I tre livelli di decisione

### 1. Firma ottica — esclusione certa

Prima di considerare la data, il programma esclude i Flat incompatibili usando:

- camera e geometria/ROI;
- binning;
- filtro;
- gain, offset e readout quando rilevanti;
- schema Bayer;
- focale e rotatore quando disponibili;
- eventuale identificatore Flat Epoch esplicito.

Un Flat fisicamente incompatibile non può vincere grazie alla vicinanza
temporale.

### 2. Timeline automatica — proposta motivata

Quando esistono più Flat Set compatibili per lo stesso filtro, il programma:

1. raggruppa i Flat catturati insieme in set distinti;
2. raggruppa i Light per notte astronomica e firma ottica;
3. ordina Light e Flat Set cronologicamente;
4. calcola la distanza del gruppo Light da ogni Flat Set;
5. seleziona automaticamente solo se il candidato migliore possiede un margine
   temporale sufficiente sul secondo;
6. mostra distanza, margine e motivo nell’Inspector;
7. lascia il caso ambiguo se la prova temporale è debole.

La data è quindi una prova di contesto, non un’assunzione assoluta.

### 3. Link manuale — autorità massima

L’utente può selezionare nell’albero:

- una notte;
- più notti già raggruppate;
- un filtro;
- una serie di Light;

e collegarle esplicitamente a un Flat Set. Il link:

- ha precedenza sull’inferenza automatica;
- viene applicato in batch ai Light selezionati;
- è persistente nelle impostazioni/progetto;
- è annullabile;
- viene registrato nel manifest come decisione utente;
- forza il ricalcolo del progetto;
- produce automaticamente i percorsi `FLATSET_<id>` richiesti da WBPP.

Il comando `Torna automatico` rimuove il link dai Light selezionati e riesegue
l’inferenza.

## Applicazione all’esempio reale

| Periodo | Filtro | Evento | Flat Epoch attesa |
|---|---|---|---|
| Giorni 1–6 | HOO | configurazione invariata | `HOO-A` |
| Giorni 7–9 | SIIOIII | cambio filtro | `SIIOIII-A` |
| Giorni 10–12 | HOO | filtro rimontato e nuovi Flat | `HOO-B` |
| Giorni 13–14 | SIIOIII | filtro rimontato e nuovi Flat | `SIIOIII-B` |
| Giorno 15+ | SIIOIII | pulizia specchio/vetro filtro | `SIIOIII-C` |

La timeline può proporre correttamente `HOO-A/HOO-B` e
`SIIOIII-A/SIIOIII-B/SIIOIII-C` se i set sono temporalmente separati. La pulizia
resta però un evento non dimostrabile dagli header: l’app deve evidenziare la
proposta e consentire il link manuale certo delle sessioni successive a
`SIIOIII-C`.

## Evoluzione “geniale”: impronta ottica del Flat

La fase successiva può aggiungere una prova indipendente dalla data:

1. normalizzare una rappresentazione combinata di ciascun Flat Set;
2. rimuovere il gradiente su larga scala;
3. estrarre vignettatura e ombre di polvere;
4. calcolare una fingerprint multiscala;
5. confrontare fingerprint tra set dello stesso filtro;
6. rilevare discontinuità compatibili con rotazione, polvere spostata o pulizia;
7. combinare firma header, timeline e fingerprint in un livello di confidenza.

Questa analisi non deve dichiarare certezza assoluta. Nebulose, gradienti e
stelle rendono molto più difficile estrarre la stessa impronta dai Light; perciò
la fingerprint serve soprattutto a dimostrare che due **Flat Set** appartengono
a stati ottici differenti. Il link manuale resta sempre disponibile.

## Modello di confidenza

- **Confermato**: link manuale o identificatore Flat Epoch esplicito.
- **Forte**: firma ottica compatibile e timeline con margine ampio.
- **Probabile**: timeline coerente ma margine limitato; richiede revisione.
- **Ambiguo**: più candidati equivalenti.
- **Incompatibile**: firma ottica differente.
- **Mancante**: nessun Flat Set utilizzabile.

Lo stato “Pronto per WBPP” è consentito soltanto per `Confermato` o `Forte`.

## Struttura UI

L’albero Flat deve mostrare:

```text
Flat
  HOO
    Flat Epoch · HOO-A
      2026-06-07
    Flat Epoch · HOO-B
      2026-06-13
  SIIOIII
    Flat Epoch · SIIOIII-A
    Flat Epoch · SIIOIII-B
    Flat Epoch · SIIOIII-C
```

L’albero Light deve mostrare sull’etichetta della sessione il Flat Epoch manuale
quando presente. L’Inspector deve mostrare il motivo dell’assegnazione automatica
oppure `Assegnazione manuale`.

## Regole di sicurezza

- Mai scegliere in silenzio tra candidati con confidenza insufficiente.
- Mai usare il filtro come unica prova quando esistono più Flat Set.
- Mai dedurre una pulizia inesistente o ignorare una pulizia dichiarata.
- Mai modificare gli header originali.
- Ogni cambio manuale deve essere annullabile e tracciato.
- La generazione WBPP deve aggiungere `FLATSET` Pre solo quando più set dello
  stesso gruppo nativo devono restare distinti.

## Test di accettazione indispensabili

1. Sei notti HOO condividono lo stesso Flat Set.
2. Il ritorno a HOO dopo SIIOIII usa un nuovo Flat Set.
3. Il ritorno a SIIOIII usa il secondo set SIIOIII.
4. Una pulizia durante una sequenza SIIOIII crea un confine manuale.
5. Un link su un nodo sessione modifica tutti e soli i Light contenuti.
6. `Torna automatico` ripristina la proposta temporale.
7. Un Flat con geometria incompatibile non compare tra i collegamenti validi.
8. Il piano finale contiene cartelle `FLATSET` distinte e WBPP le associa
   correttamente.
