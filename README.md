# AstroProject Forge

Applicazione Windows nativa (.NET/WPF) per preparare progetti astronomici
multisessione destinati a PixInsight WeightedBatchPreprocessing 3.x.

Legge gli header FITS/XISF senza caricare le immagini, classifica Light/Flat/
Dark/Bias, risolve la notte astronomica oltre mezzanotte, abbina la libreria
Master, permette override singoli o di gruppo e mostra sia l'albero acquisito
sia quello finale. La ricetta Grouping Keywords è adattiva: suggerisce solo le
keyword realmente necessarie e specifica Pre/Post.

La struttura principale è `Filtro → Sessioni di configurazione → Sessione →
Notti/Flat/Master collegati`. Più notti possono condividere la stessa Flat Epoch;
checkbox e linker manuale permettono di collegare un Flat Set a notti singole,
multiple o a un’intera sessione. Dark e Bias sono inoltre raccolti sotto
`Senza filtro → Sessioni sensore`.

L'esportazione usa una cartella di staging riprendibile, copia verificata
SHA-256, rinomina descrittiva dei Master e genera manifest JSON, guida WBPP e
report HTML. Gli originali non vengono modificati.

## Avvio per sviluppo

Richiede .NET SDK 10 per compilare. L'eseguibile pubblicato è autonomo.

```powershell
.\.dotnet\dotnet.exe run --project dotnet\AstroForge.App\AstroForge.App.csproj
```

## Test

```powershell
.\.dotnet\dotnet.exe run --project dotnet\AstroForge.Core.Tests\AstroForge.Core.Tests.csproj -c Release
```

## Build Windows

```powershell
.\.dotnet\dotnet.exe publish dotnet\AstroForge.App\AstroForge.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist-dotnet
```

L'eseguibile autonomo viene creato in `dist-dotnet\AstroForge.App.exe`.

## Principi di sicurezza

- Gli header hanno precedenza sul nome del file.
- Il nome viene usato solo come fallback o per rilevare conflitti.
- Nessun file sorgente viene modificato.
- L'esportazione usa copia, verifica dimensione e hash SHA-256, quindi scrive il
  manifest del progetto.
- Gli abbinamenti ambigui richiedono revisione esplicita; omonimi provenienti da
  sorgenti diverse vengono conservati con suffissi deterministici.
- Le preferenze e le correzioni manuali sono persistenti in `%LocalAppData%`.
