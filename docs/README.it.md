# AstroProject Forge

[Download](https://github.com/astropuzzo/astroproject-forge/releases/latest) · [Guida](https://github.com/astropuzzo/astroproject-forge/wiki) · [Segnala un problema](https://github.com/astropuzzo/astroproject-forge/issues/new?template=bug_report.yml)

AstroProject Forge organizza acquisizioni FITS/XISF multisessione per PixInsight WeightedBatchPreprocessing. Ricostruisce le notti oltre la mezzanotte, separa filtri e sessioni ottiche, abbina Flat/Dark/Bias e genera la struttura e le Grouping Keywords richieste dal progetto.

![Mappa del progetto](images/project-map.png)

## Flusso essenziale

1. Aggiungi cartelle o file Light/Flat.
2. Collega una o più Master Library Dark/Bias.
3. Premi **Analizza**.
4. Risolvi soltanto gli elementi indicati come da rivedere.
5. Controlla la pagina **WBPP**.
6. Esporta il progetto.

Sono disponibili statistiche per filtro, sessione e notte, collegamenti manuali dei Flat Set, gestione indipendente della Master Library e analisi qualità opzionale con Blink.

![Dati di acquisizione](images/acquisition-dashboard.png)

I pacchetti Windows, Linux e macOS sono disponibili nella pagina [Releases](https://github.com/astropuzzo/astroproject-forge/releases/latest). Non è necessario installare .NET separatamente.

Copyright © 2026 Gianmarco Spagnoli. Software gratuito per uso personale e non commerciale.
