# Processo di release

## Gate automatico

Eseguire da PowerShell:

```powershell
.\qa-gate.ps1
```

Il comando testa parser, matching, migrazioni, update, recovery ed export, compila WPF in Release, pubblica l'EXE self-contained e scrive `artifacts/qa/qa-report.json`. Non usa `E:` né immagini personali.

## Pacchetto di sviluppo

```powershell
.\build-distribution.ps1
```

Produce in `artifacts/distribution` ZIP portabile, SBOM, hash, manifest e — quando Inno Setup 7 è disponibile — installer x64 per utente. Una build non firmata è utile per QA interno, ma `releaseEligible` resta `false`.

## Release commerciale firmata

Installare SignTool tramite Windows SDK e configurare nel certificate store un certificato code-signing autentico. Impostare soltanto nella sessione protetta di release:

```powershell
$env:ASTROFORGE_SIGN_THUMBPRINT = 'THUMBPRINT_DEL_CERTIFICATO'
$env:ASTROFORGE_SIGNTOOL = 'C:\percorso\sicuro\signtool.exe'
.\build-distribution.ps1 -RequireSignature
```

Lo script firma e verifica EXE e installer con SHA-256 e timestamp RFC 3161. Certificati, password e segreti non devono mai entrare nel repository.

## Collaudo manuale obbligatorio prima della RC

- Windows 10 22H2 e Windows 11 supportato, VM pulite e account standard.
- Installazione senza SDK .NET e senza richiesta amministratore.
- Apertura `.astroforge`, import da NTFS, exFAT e percorso di rete.
- Aggiornamento Stable→Stable, Beta→Beta e downgrade di emergenza con installer conservato.
- Disinstallazione scegliendo sia “conserva” sia “rimuovi dati locali”; verificare che progetti e FITS/XISF restino intatti.
- Verifica Authenticode di entrambi gli artefatti e confronto con `SHA256SUMS.txt`.

Conservare il report firmato delle VM insieme agli artefatti della release. Un fallimento P0 blocca la pubblicazione.
