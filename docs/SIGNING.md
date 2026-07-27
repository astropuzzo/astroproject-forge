# Firma delle release / Release signing

AstroProject Forge distingue due livelli:

- una beta non firmata può essere rilevata dall’app, ma il download viene aperto nella pagina GitHub Release;
- il download interno è disponibile soltanto quando EXE e installer superano SHA-256 e verifica Authenticode.

Una firma auto-generata non è adatta alla distribuzione pubblica: sui PC degli utenti non avrebbe una catena di attendibilità riconosciuta.

## Windows — percorso consigliato per un autore individuale in Italia

### 1. Acquistare un certificato pubblico IV Code Signing

Serve una CA inclusa nel Microsoft Trusted Root Program. Per un autore senza società, scegliere un certificato **Individual Validation (IV)**. Il nome verificato della persona diventa il publisher mostrato da Windows.

Dal 2023 la chiave privata deve risiedere in hardware protetto: token USB FIPS oppure servizio cloud HSM. Non viene normalmente consegnato un semplice file PFX esportabile.

Opzioni pratiche:

- **cloud HSM**: più semplice per GitHub Actions e build automatizzate;
- **token USB**: adatto alla firma manuale su un PC controllato, ma meno comodo in CI.

Esempio attualmente disponibile per sviluppatori individuali: [SSL.com IV Code Signing](https://www.ssl.com/products/software-integrity/code-signing/). Confrontare comunque prezzo, rinnovo, numero di firme, IVA e compatibilità con SignTool prima dell’acquisto.

Azure Artifact Signing è economicamente interessante, ma la [disponibilità Public Trust](https://learn.microsoft.com/azure/artifact-signing/quickstart) è attualmente limitata: le organizzazioni UE sono ammesse, mentre gli sviluppatori individuali sono indicati soltanto per Stati Uniti e Canada.

### 2. Completare la verifica d’identità

La CA richiede normalmente:

- documento d’identità;
- indirizzo verificabile;
- email e telefono;
- controllo tramite video, provider di identità o chiamata;
- accettazione del subscriber agreement.

Non acquistare un certificato EV soltanto per SmartScreen: per una normale app desktop senza driver non è necessario e non garantisce reputazione immediata.

### 3. Collegare il certificato alla build

Se il provider espone il certificato nel Windows Certificate Store:

```powershell
$env:ASTROFORGE_SIGN_THUMBPRINT = 'THUMBPRINT_SENZA_SPAZI'
$env:ASTROFORGE_SIGNTOOL = 'C:\Program Files (x86)\Windows Kits\10\bin\<SDK>\x64\signtool.exe'
.\build-distribution.ps1 -Channel Beta -Version 0.9.0-beta.5 -RequireSignature
```

Lo script firma prima l’eseguibile e poi l’installer, aggiunge timestamp RFC 3161 SHA-256 e verifica entrambi con SignTool.

Per un cloud HSM potrebbe servire il client/KSP o lo strumento CLI del provider. Credenziali, PIN, certificati e token non devono entrare nel repository; in GitHub Actions vanno salvati come repository secrets.

### 4. Pubblicare il canale automatico

Dopo aver caricato gli artefatti nella release versionata:

```powershell
.\scripts\publish-update-channel.ps1 -Channel Beta
```

Il comando rifiuta manifest non firmati e aggiorna `channel-beta/beta.json`. Il client potrà quindi scaricare l’installer e verificarne dimensione, SHA-256 e firma.

## macOS

Per eliminare gli avvisi Gatekeeper servono:

1. iscrizione all’[Apple Developer Program](https://developer.apple.com/programs/) — 99 USD/anno o prezzo locale;
2. certificato **Developer ID Application**;
3. Hardened Runtime e firma con timestamp;
4. invio al servizio notarile Apple con `notarytool`;
5. `stapler` sul DMG prima della pubblicazione.

Apple richiede un Developer ID valido per la notarizzazione; una firma ad-hoc (`codesign -`) è utile soltanto per test tecnici.

## Linux

Linux non usa un’unica firma applicativa equivalente ad Authenticode o Developer ID. Per le release correnti:

- i tag e i commit GitHub devono essere verificati;
- gli asset espongono digest SHA-256;
- un futuro repository APT dovrà firmare metadati e pacchetti con una chiave dedicata.

---

## English summary

Unsigned beta releases are discoverable, but the app opens their GitHub Release page instead of downloading them internally. Verified in-app downloads require a public Windows code-signing certificate, SHA-256, and a valid Authenticode signature.

For an individual developer in Italy, obtain an IV Code Signing certificate from a trusted CA and choose either a FIPS hardware token or a cloud-HSM signing service. For macOS, join the Apple Developer Program, sign with Developer ID Application, enable hardened runtime, notarize, and staple the distributed DMG. Never commit certificates, PINs, private keys, or signing credentials.
