# Fase 0 — Guida ai test passo passo

Obiettivo unico: **l'iPhone accetta il PC come mouse/tastiera Bluetooth LE?**

Per ogni passo trovi i comandi esatti, dove cliccare e cosa annotare. Annota i risultati in
[`fase0-risultati.md`](fase0-risultati.md) (copialo, compilalo e rimandamelo insieme ai log).

> **Attenzione ai percorsi dei menu iOS.** I percorsi qui sotto sono quelli di iOS 18/26 in italiano.
> Non ho potuto verificarli su iOS 27: se una voce ha un nome diverso, usa la ricerca in cima all'app
> Impostazioni (es. scrivi «AssistiveTouch» o «Tastiere hardware») e annota il percorso reale.

---

## Passo 0 — Preparazione (una volta sola)

### 0.1 Strumenti sul PC

Apri **PowerShell** (non serve come amministratore) ed esegui:

```powershell
winget install Microsoft.DotNet.SDK.8
winget install Git.Git          # solo se non hai già git
```

Chiudi e riapri PowerShell, poi verifica:

```powershell
dotnet --list-sdks              # deve comparire una riga 8.0.xxx
```

### 0.2 Modalità sviluppatore di Windows

Serve per registrare l'app di test come pacchetto (Package Identity) senza firmarla.

```powershell
start ms-settings:developers
```

Il comando apre direttamente la pagina giusta delle Impostazioni: attiva **Modalità sviluppatore**
e conferma l'avviso.

### 0.3 Scarica il progetto

```powershell
cd $HOME\Documents
git clone https://github.com/alestormbringer/Screen-Mirroring-iPhone-Windows.git
cd Screen-Mirroring-iPhone-Windows
git checkout claude/hopeful-pasteur-ix87en
```

Tutti i comandi successivi partono da questa cartella.

### 0.4 Stato Bluetooth di partenza

1. Chiudi **Collegamento al telefono (Phone Link)** se è aperto, e la pagina *Impostazioni > Bluetooth
   e dispositivi* di Windows (quando è aperta il PC diventa rilevabile in Bluetooth classico e falsa il test).
2. Se l'iPhone è **già abbinato al PC** (per esempio tramite Phone Link), annotalo, poi per un test pulito:
   - sul PC: *Impostazioni > Bluetooth e dispositivi > Dispositivi* → «…» accanto all'iPhone → **Rimuovi dispositivo**;
   - sull'iPhone: *Impostazioni > Bluetooth* → (i) accanto al PC → **Dissocia questo dispositivo**.

   Phone Link si potrà ri-abbinare dopo i test.
3. *(Facoltativo ma utile)* installa sull'iPhone l'app gratuita **nRF Connect** (Nordic Semiconductor):
   serve solo a vedere se il PC sta trasmettendo l'advertising Bluetooth LE.

---

## Passo 1 — Check dell'adattatore Bluetooth

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Check-BluetoothAdapter.ps1
```

Lo script non modifica nulla. Stampa e salva un report in `%TEMP%\iPhoneMirror\adapter-check-*.txt`.

**Cosa annotare**
- Edizione e build di Windows.
- `IsPeripheralRoleSupported` (la riga in verde/rosso): **è il dato decisivo**.
- Produttore del chip (Intel / MediaTek / Realtek / …), VID/PID, versione e data del driver.
- `LE Secure Connections`, `Extended advertising`.
- Stato BR/EDR (discoverable/connectable).

**Esito**
- `PERIPHERAL ROLE SUPPORTED` → vai al passo 2.
- `NOT SUPPORTED` → aggiorna il driver Bluetooth (sito Asus per il Vivobook M1607KA, oppure sito del
  produttore del chip) e ripeti. Se resta *NOT SUPPORTED*, la strada «zero hardware» è chiusa: piano B (ESP32).

---

## Passo 2 — Compila e avvia HidProbe

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-HidProbe.ps1 -Launch
```

Lo script compila, registra il pacchetto di sviluppo e avvia l'app **con Package Identity**.
In seguito puoi riaprirla dal menu Start («iPhone Mirror HID Probe») o scrivendo `hidprobe` nel terminale.

> **Non avviare mai** `src\HidProbe\bin\...\HidProbe.exe` con doppio clic: partirebbe senza
> Package Identity e il servizio HID non verrebbe pubblicato davvero (le API dicono «OK» lo stesso).

**Cosa controllare nella finestra (sezione «Stato»)**
- `Package Identity: OK (...)` in verde.
- `Adattatore: ... | Peripheral: SÌ` in verde.

**Se qualcosa va storto**: annota il messaggio d'errore esatto di PowerShell o della finestra.
Il log completo è in `%TEMP%\iPhoneMirror\hidprobe-*.log` (pulsante «Apri cartella log», oppure
`powershell -ExecutionPolicy Bypass -File .\tools\Open-Logs.ps1`).

---

## Passo 3 — Accoppiamento e test base (puntatore relativo)

### 3.1 Avvia l'advertising

Nella finestra HidProbe:
1. **Puntatore relativo** selezionato, **Servizio Batteria** spuntato, **Sopprimi Bluetooth classico** NON spuntato.
2. Clic su **Avvia advertising**.
3. Nel log deve comparire `HID advertisement status: Started`. Se compare `Aborted` o un errore, annotalo.

*(Facoltativo)* Sull'iPhone apri **nRF Connect** → Scanner: cerca un dispositivo con servizio
`Human Interface Device` / `0x1812`. Annota **il nome** con cui compare (servirà al punto 3.3).

### 3.2 Prepara l'iPhone

1. *Impostazioni > Accessibilità > Tocco > AssistiveTouch* → **attiva**. (Senza AssistiveTouch
   iOS non mostra il puntatore del mouse.)
2. Tieni l'iPhone sbloccato e vicino al PC.

### 3.3 Abbina

Prova in quest'ordine e annota quale strada funziona:

- **A.** *Impostazioni > Bluetooth* → sezione **Altri dispositivi** → tocca il nome del PC.
- **B.** *Impostazioni > Accessibilità > Tocco > AssistiveTouch > Dispositivi (o «Dispositivi di
  puntamento») > Dispositivi Bluetooth…* → tocca il nome del PC.

Durante l'abbinamento:
- sull'iPhone può comparire **«Richiesta di abbinamento Bluetooth»** → **Abbina** (se mostra un codice, annotalo);
- su **Windows** compare una notifica tipo **«Aggiungi un dispositivo»**: cliccala e conferma
  (**Consenti/Abbina**). Fallo in fretta: scade dopo circa 30 secondi. Se compare un codice, deve essere
  uguale a quello dell'iPhone.

**Il segnale che conta**: nella finestra HidProbe la riga
`Iscritti alle notifiche: tastiera 1 | mouse 1 | consumer 1` (il mouse diventa verde).
Significa che l'iPhone ha letto il descrittore HID e si è iscritto ai report: da qui in poi l'input arriva.

### 3.4 Test del puntatore

Muovi il mouse **dentro il riquadro scuro a sinistra** della finestra HidProbe.
Quando arrivi al bordo, esci dal riquadro e rientra dall'altra parte (come sollevare un mouse).
La sensibilità si regola con «Sensibilità (relativo)».

| Test | Come | Cosa annotare |
|---|---|---|
| Puntatore visibile | dopo l'abbinamento | compare il cerchio grigio di AssistiveTouch? |
| Movimento | muovi il mouse nel riquadro | si muove? fluido o a scatti? direzione corretta? |
| Click sinistro | clic su un'icona | l'app si apre? |
| Click destro | clic destro | cosa succede (di default apre il menu AssistiveTouch)? |
| Trascinamento | tieni premuto il sinistro e muovi: sposta uno slider del Centro di Controllo, scorri una lista trascinando, sposta un'icona dopo una pressione lunga | funziona? **(fonte non ufficiale: su iOS 18+ non funzionerebbe in modalità relativa: va verificato)** |
| Rotella | rotella su una lista in Impostazioni | scorre? in che verso? |
| Angolo | pulsante «Puntatore → angolo in alto a sinistra» | il puntatore finisce nell'angolo? |

### 3.5 Test della tastiera

1. Apri **Note** sull'iPhone e tocca (o clicca col puntatore) dentro una nota.
2. Imposta il layout: *Impostazioni > Generali > Tastiera > Tastiere hardware* (compare solo con una
   tastiera collegata) → **Tedesco (Svizzera)**. Annota il nome esatto della voce.
3. Torna in Note. In HidProbe **clicca nel riquadro scuro** (il bordo diventa verde: la tastiera ora va
   all'iPhone; per smettere clicca fuori dal riquadro).
4. Premi il pulsante **Scrivi «Zürich 123»**:
   - `Zürich 123` → tastiera OK e layout svizzero corretto;
   - `Y[rich 123` → l'iPhone usa il layout USA; `Yèrich 123` → layout italiano.
5. Scrivi a mano dalla tastiera del PC (con il riquadro attivo) e verifica:

| Tasti | Atteso (layout Tedesco Svizzera) | Annota |
|---|---|---|
| lettere, `z`/`y`, numeri | come sul PC | ok? |
| `ü ö ä`, `è é à` (con Maiusc) | come sul PC | ok? |
| tasto `§` (in alto a sinistra) e tasto `<` (accanto a Maiusc sinistro) | `§` e `<` | **scambiati?** Se sì, riprova con «Scambia tasti ISO § e <» |
| tasto `$` (accanto a Invio) | `$` | ok? |
| `AltGr+2`, `AltGr+3`, `AltGr+7` (sul PC: `@ # \|`) | sull'iPhone vale il layout Apple (Option+…): **probabilmente diverso** | cosa esce? |
| Invio, Backspace, frecce, Tab, Esc | | ok? |
| Blocco maiuscole | | ok? nel log compare `Keyboard LEDs written`? |
| tasto Windows + Spazio (= Cmd+Spazio) | apre la ricerca Spotlight | ok? |
| pulsanti «Cmd+Spazio» e «Cmd+H» | Spotlight / torna alla Home? | cosa succede? |

6. **Tasti consumer**: scegli una voce nel menu a tendina e premi «Invia tasto consumer». Annota
   l'effetto di ciascuna (es. «AC Home» torna alla Home? «Eject» mostra/nasconde la tastiera
   virtuale? Volume +/− cambia il volume?). Ci serve per le scorciatoie della Fase 1.

### 3.6 Stabilità

- Lascia l'iPhone in standby 1 minuto, poi sbloccalo: il puntatore torna da solo? Quanto ci mette?
- Clicca «Ferma», poi «Avvia advertising»: l'iPhone si ricollega da solo?

---

## Passo 4 — Solo se il passo 3 fallisce: soppressione del Bluetooth classico

Sintomi tipici: l'iPhone vede il PC ma l'abbinamento fallisce, oppure «Connesso» ma
`Iscritti alle notifiche` resta a 0 (nel log: `no subscribed clients`).

1. In HidProbe: **Ferma**.
2. Rimuovi l'abbinamento da entrambi i lati (vedi 0.4, punto 2).
3. Spunta **Sopprimi Bluetooth classico (BR/EDR) durante il test** → **Avvia advertising**.
4. Controlla la riga `BR/EDR ...: rilevabile=False connettibile=False | SOPPRESSIONE ATTIVA`.
5. Ripeti il passo 3 da 3.3.

Effetto collaterale: mentre la soppressione è attiva, cuffie e casse Bluetooth del PC non si collegano.
Con «Ferma» o chiudendo l'app lo stato viene ripristinato (e, se l'app va in crash, al successivo avvio).
In emergenza:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Restore-BrEdr.ps1
```

**Cosa annotare**: se con la soppressione l'abbinamento riesce o no; i messaggi d'errore sull'iPhone.
Se fallisce ancora, mandami il log: valuteremo una cattura del traffico Bluetooth (istruzioni a parte)
prima di dichiarare fallita la Fase 0.

---

## Passo 5 — Variante con puntatore assoluto

L'iPhone memorizza il descrittore HID al momento dell'abbinamento, quindi per cambiare variante
**bisogna dissociare e ri-abbinare**.

1. In HidProbe: **Ferma**. Rimuovi l'abbinamento da entrambi i lati (0.4, punto 2).
2. Seleziona **Puntatore assoluto (X/Y 0–32767)** (lascia la soppressione BR/EDR come nel test riuscito).
3. **Avvia advertising** e abbina di nuovo (3.3).
4. Nel riquadro ora c'è un rettangolo con le proporzioni dell'iPhone 16 Pro Max: la posizione del mouse
   nel rettangolo corrisponde a quella sullo schermo dell'iPhone.

| Test | Come | Cosa annotare |
|---|---|---|
| Accettato? | dopo l'abbinamento | l'iPhone si collega? `Iscritti` mouse = 1? compare il puntatore? |
| Posizione | porta il mouse sui 4 angoli e al centro del rettangolo | il puntatore va nel punto corrispondente? di quanto sbaglia? |
| Click preciso | clicca icone in punti diversi | apre l'icona giusta? |
| Trascinamento | sposta un'icona, uno slider, scorri trascinando | funziona? meglio o peggio del relativo? |
| Rotella | come al passo 3 | scorre? |

Se l'assoluto non viene accettato (nessun puntatore, iPhone che si scollega), annotalo e torna al relativo:
la Fase 1 userà allora il relativo con calibrazione.

---

## Passo 6 — Video via AirPlay

1. Scarica l'installer **x64 MSI** dall'ultima release di
   [AirPlay-Windows](https://github.com/Thomas-lab17/AirPlay-Windows/releases/latest) e installalo
   (SmartScreen: «Ulteriori informazioni» → «Esegui comunque»; firewall: **consenti** sulle reti private).
2. Al primo avvio accetta l'installazione di **Bonjour**.
3. PC e iPhone sulla **stessa rete Wi-Fi**.
4. iPhone: *Centro di Controllo → Duplica schermo* → scegli **AirPlay-Windows**.

**Prima prova: solo video** (HidProbe chiuso).
- Annota: si collega? qualità? scatti? blocchi (il README segnala che a volte serve riconnettersi)?
- **Latenza**: avvia il *Cronometro* sull'iPhone e scatta una foto con un altro telefono che inquadri
  insieme l'iPhone e lo schermo del PC; la differenza tra i due tempi è la latenza. Ripeti 3 volte.

**Seconda prova: video + HidProbe insieme** (quello che servirà nella Fase 1).
- Avvia HidProbe (ultima variante funzionante), poi la duplicazione.
- Controlla l'iPhone guardando **solo** lo schermo del PC: è usabile? quanto ritardo percepisci tra
  click e reazione visibile?
- AirPlay-Windows usa anche lui un advertising Bluetooth LE («Enable Bluetooth Discovery»). Se con
  entrambi aperti l'HID smette di funzionare, togli quella spunta in AirPlay-Windows e riprova; annota l'esito.

---

## Cosa mandarmi alla fine

1. `fase0-risultati.md` compilato.
2. I log da `%TEMP%\iPhoneMirror\`: `adapter-check-*.txt` e gli `hidprobe-*.log` dei test (anche quelli falliti).
   Comando: `powershell -ExecutionPolicy Bypass -File .\tools\Open-Logs.ps1`.

## Pulizia (quando hai finito)

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Unregister-HidProbe.ps1   # rimuove l'app di test
```

Sull'iPhone dissocia il PC; su Windows rimuovi l'iPhone se compare tra i dispositivi.
Se avevi Phone Link, ri-abbinalo.
