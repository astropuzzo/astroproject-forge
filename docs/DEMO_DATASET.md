# Progetto demo: Cygnus Loop

Un piccolo progetto sintetico e realistico per tutorial, screenshot e test end-to-end. Non contiene dati reali: le immagini sono generate in modo deterministico (stesso seme, stessi byte) e pesano circa 6 MB in tutto.

```
dotnet run --project dotnet/AstroForge.DemoData -c Release -- <cartella vuota>
```

Senza argomenti crea `AstroForge-Demo-CygnusLoop` nella cartella corrente. Lo stesso generatore è disponibile in Core (`AstroForge.Core.Demo.DemoDatasetGenerator`), così l'app potrà crearlo al volo per i tutorial senza distribuire file.

## Lo scenario

Velo Orientale (NGC 6992) ripreso in due notti con una ZWO ASI2600MM Pro su Askar FRA400 con riduttore 0,7× (280 mm), gain 100, offset 50, −10 °C, pose da 300 s.

| Notte | Software | Light | Flat | Cosa mostra |
|---|---|---|---|---|
| 14 agosto 2026 | ZWO ASIAIR con ruota portafiltri | 4 Hα, 4 OIII | 3 + 3 all'alba | nomi file ASIAIR, niente `SET-TEMP`, `OBJECT` e `TELESCOP` |
| 20 agosto 2026 | N.I.N.A. | 4 Hα, 4 “Filtro 3” | 3 + 3 all'alba | slot della ruota mai rinominato: “Filtro 3” è l'Antlia SII 3 nm Pro |
| — | Libreria Master | Master Dark 300 s, Master Bias | | Master in stile WBPP con gain, offset e temperatura |

Dettagli pensati per i tutorial:

- **Filtro con nome arbitrario.** “Filtro 3” non dice nulla sul vetro: Forge lo chiede una volta e il profilo della ruota lo associa a `ant3-SII`. Il file `astroforge-demo.json` contiene la risposta attesa.
- **Flat Set diversi per notte.** Tra le due notti la camera è stata smontata: l'ombra della polvere si è spostata e i Flat dell'ASIAIR non valgono per la notte N.I.N.A. Per l'Hα, ripreso in entrambe le notti, WBPP deve usare `FLATSET`.
- **Un Light velato.** Il terzo frame SII passa sotto una nuvola: fondo alto e nessuna stella rilevabile. Il Quality Lab lo deve mettere in fondo alla classifica e l'export lo sposta in `Excluded/Quality`.
- **Riconoscimento attrezzatura.** `INSTRUME` identifica la ASI2600MM Pro mono; `TELESCOP` + `FOCALLEN` 280 identificano FRA400 e riduttore 0,7×.
- **Scala ridotta.** Le immagini sono 384×256, cioè il sensore reale ridotto 1/16; `XPIXSZ` è scalato di conseguenza (60,16 µm) così il campo inquadrato resta coerente.

## Cartelle e nomi file

```
ASIAIR/Autorun/Light/Cygnus Loop/Light_Cygnus Loop_300.0s_Bin1_Ha_gain100_20260814-223104_-10.0C_0001.fit
ASIAIR/Autorun/Flat/Flat_3.0s_Bin1_Ha_gain100_20260815-054821_-9.9C_0001.fit
N.I.N.A./Cygnus Loop/2026-08-20/LIGHT/2026-08-20_22-39-24_Filtro 3_-10.10_300.00s_0004.fits
N.I.N.A./Cygnus Loop/2026-08-20/FLAT/2026-08-21_05-40-02_Ha_-10.10_2.50s_0000.fits
Libreria Master/ASI2600MM_G100_O50_-10C/masterDark_BIN-1_384x256_EXPOSURE-300.00s.fits
Libreria Master/ASI2600MM_G100_O50_-10C/masterBias_BIN-1_384x256.fits
```

## Cosa è verificato e cosa è ipotizzato

Verificato su fonti pubbliche:

- **N.I.N.A.**: le keyword scritte (`IMAGETYP` in maiuscolo, `EXPOSURE`/`EXPTIME`, `DATE-LOC`, `SET-TEMP`/`CCD-TEMP`, `FWHEEL`, `FILTER`, `TELESCOP`, `FOCALLEN`, `OBJECT`, `ROWORDER`, `SWCREATE`…) seguono la [documentazione ufficiale dei FITS di N.I.N.A.](https://nighttime-imaging.eu/docs/master/site/advanced/file_formats/fits/).
- **ASIAIR**: senza ruota portafiltri motorizzata l'ASIAIR lascia vuoto il filtro nell'header ([Cloudy Nights](https://www.cloudynights.com/forums/topic/1007454-whats-the-best-way-to-update-fits-file-filter-information-with-manual-filter-drawer-using-an-asiair)); per questo il demo usa una ruota ZWO, con il filtro sia in `FILTER` sia nel nome file. Il nome file ha la forma `Tipo_Esposizione_Bin_…_data-ora_numero.fit` con campi configurabili ([Cloudy Nights](https://www.cloudynights.com/topic/869664-asiair-file-naming-issue-and-questions/)).

Ipotizzato, da confrontare con un file ASIAIR reale:

- l'ordine esatto dei campi del nome file (`Light_<target>_300.0s_Bin1_<filtro>_gain100_<AAAAMMGG-hhmmss>_-10.0C_0001.fit`), l'ora locale nel nome e UTC in `DATE-OBS`;
- la presenza di `OFFSET`, `FOCALLEN` e `CCD-TEMP` e l'assenza di `SET-TEMP`, `OBJECT` e `TELESCOP` nell'header ASIAIR (per `TELESCOP` ci sono [utenti che ne chiedono l'aggiunta](https://www.cloudynights.com/topic/874214-possible-to-get-asiair-pro-to-put-telescope-in-fits-headers/), ma non l'ho verificato su un file);
- la numerazione dei frame N.I.N.A. da `0000` e il percorso `<target>/<notte>/<TIPO>/`, che dipende dal modello impostato dall'utente.

Un paio di file reali dell'ASIAIR (anche solo l'header) basterebbero a chiudere le ipotesi.
