# AstroProject Forge — note di rilascio

## 0.9.0-beta.1 — 17 luglio 2026

- Visual system v5: title bar scura integrata, navigazione orizzontale, canvas più ampio, pannelli responsivi e micro-transizioni riducibili.
- Inspector contestuale con stato vuoto utile e controlli nascosti finché non esiste una selezione.
- Dashboard ridisegnata: Gain e temperatura sono campi leggibili con etichette dedicate, non abbreviazioni concatenate.
- Master Library Lab ripensato come workbench indipendente con inventario, preflight, destinazione e anteprima tabellare.
- Scrollbar coerenti con il tema e pannello Sorgenti interamente scorrevole sulle finestre basse.
- Stati vuoti collegati al conteggio reale delle collezioni, senza overlay residui dopo analisi o scansione.
- QA gate riproducibile e indipendente da cartelle o librerie personali.
- Matrice sintetica multiproduttore per N.I.N.A./ZWO, ASIAIR, QHY/Voyager, Player One/SharpCap e FITS generici.
- Fuzz controllato FITS/XISF, test dei confini della notte astronomica, percorsi Unicode lunghi e dataset da 10.000 frame.
- Ripresa verificata dopo interruzione dell'export.
- Versione SemVer e canale Stable/Beta visibili nell'app.
- Controllo aggiornamenti disattivato di default, manifest solo HTTPS e download verificato con dimensione, SHA-256 e Authenticode.
- Pacchetto self-contained, ZIP portabile, SBOM e installer Windows per utente.

La firma Authenticode è obbligatoria per una release commerciale. Le build locali senza certificato sono marcate esplicitamente come non eleggibili alla vendita.
