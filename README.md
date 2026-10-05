# iPhone Mirror per Windows (nome provvisorio)

Tool locale e gratuito per vedere e controllare un iPhone da Windows con mouse e tastiera:
video tramite **AirPlay** (UxPlay) e input tramite il **Bluetooth del PC che si presenta come
mouse/tastiera BLE** (HID over GATT). Niente cloud, niente telemetria.

**Stato: Fase 0, verifica di fattibilità.** L'unica domanda a cui rispondere ora è:
*l'iPhone accetta il PC come mouse/tastiera Bluetooth LE?*

📄 **Brief del progetto:** [`docs/brief.md`](docs/brief.md) ·
➡️ **Guida ai test:** [`docs/fase0-guida-test.md`](docs/fase0-guida-test.md) ·
**Scheda risultati:** [`docs/fase0-risultati.md`](docs/fase0-risultati.md)

## Contenuto

| Percorso | Cosa fa |
|---|---|
| `tools/Check-BluetoothAdapter.ps1` | Passo 1: verifica il ruolo Peripheral dell'adattatore, chip, driver, build di Windows, Modalità sviluppatore. Sola lettura. |
| `src/HidProbe/` | Passi 2–5: app di test .NET 8 WinForms che pubblica un servizio HID BLE (tastiera + mouse + tasti consumer) e inoltra mouse/tastiera all'iPhone. |
| `scripts/Build-HidProbe.ps1` | Compila HidProbe e lo registra come pacchetto MSIX di sviluppo (Package Identity). |
| `scripts/Unregister-HidProbe.ps1` | Rimuove la registrazione del pacchetto. |
| `tools/Restore-BrEdr.ps1` | Ripristino di emergenza del Bluetooth classico dopo il test di soppressione BR/EDR. |
| `tools/Open-Logs.ps1` | Mostra e apre i log (`%TEMP%\iPhoneMirror\`). |

Avvio rapido (dettagli nella guida):

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Check-BluetoothAdapter.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\Build-HidProbe.ps1 -Launch
```

Requisiti: Windows 10 2004+ / Windows 11, .NET 8 SDK, Modalità sviluppatore attiva. Visual Studio non serve.

## Scelte tecniche della Fase 0

**Scritto da zero, non derivato da VirtualBT.** VirtualBT è stato usato come riferimento
(comportamento, flusso dei pacchetti, problema del dual-mode), ma:
- non ha un file di licenza, quindi il suo codice non è riutilizzabile legalmente;
- pubblica tastiera e mouse come **due servizi HID separati** (il telefono vede due richieste di abbinamento),
  mentre qui c'è **un solo dispositivo composito**, come le periferiche BLE reali;
- è un'app WinUI 3 con molte pagine non pertinenti e richiede Visual Studio/MSBuild;
- il `BrEdrHelper` descritto nel suo README non è presente nel repository.

**WinForms + `dotnet build` + manifest MSIX «loose».** Alternativa valutata: WinUI 3 (come VirtualBT).

| | WinForms + manifest a mano (scelta) | WinUI 3 + VS 2022 |
|---|---|---|
| Strumenti | solo .NET 8 SDK | Visual Studio (diversi GB) + Windows App SDK |
| Package Identity | `Add-AppxPackage -Register` della cartella di build (Modalità sviluppatore) | F5 da Visual Studio |
| UI | essenziale, sufficiente per un banco di prova | più moderna |
| Rischio build | basso | il compilatore XAML è noto per dare problemi con `dotnet build` |

Per la Fase 1 la scelta della UI si può rivalutare: la parte Bluetooth (`src/HidProbe/Ble`, `src/HidProbe/Hid`)
è separata dalla UI e riutilizzabile.

**Servizio HID (0x1812).** Un solo servizio con Report Map composita:
- Report ID 1 — tastiera: input 8 byte `[modificatori, riservato, 6 tasti]` + output 1 byte (LED);
- Report ID 2 — mouse: **relativo** 4 byte `[pulsanti, dx, dy, rotella]` oppure **assoluto** 6 byte
  `[pulsanti, X(16 bit), Y(16 bit), rotella]` con X/Y in 0–32767; la variante si sceglie **prima di avviare**,
  perché l'iPhone memorizza il descrittore all'abbinamento (per cambiarla: dissocia e ri-abbina);
- Report ID 3 — tasti «consumer» (Home, volume, Eject…): per scoprire quali scorciatoie iOS accetta (utile in Fase 1).

Più Protocol Mode, HID Information, HID Control Point, tutti con cifratura richiesta (l'abbinamento parte da lì),
e un **Battery Service (0x180F)** facoltativo, previsto dalla specifica HOGP.

**Tastiera posizionale.** Si inoltra il *tasto fisico* (scan code → HID usage), non il carattere: il carattere
lo decide l'iPhone in base al suo layout per tastiere hardware (va impostato su Tedesco (Svizzera)). Un hook
di tastiera di basso livello cattura anche il tasto Windows (→ Cmd) e filtra il Ctrl «finto» che Windows genera con AltGr.

**Invio dei report.** Una notifica alla volta per report, con coda; i movimenti del mouse in attesa vengono
sommati e c'è un intervallo minimo regolabile (default 15 ms) per non accumulare ritardo nello stack Bluetooth.

**Soppressione BR/EDR** (`BluetoothEnableDiscovery`/`BluetoothEnableIncomingConnections`): disattivata di default,
si attiva solo al passo 4. Lo stato originale viene salvato su file prima di cambiarlo e ripristinato all'uscita,
in caso di crash e al successivo avvio.

## Cosa è verificato e cosa no

| Affermazione | Stato |
|---|---|
| HidProbe compila (.NET 8, `net8.0-windows10.0.19041.0`) senza errori né avvisi | ✅ verificato (compilazione su Linux con `EnableWindowsTargeting`) |
| Gli script PowerShell hanno sintassi valida | ✅ verificato (parser PowerShell 7) |
| Registrazione del pacchetto, Package Identity, avvio dell'app su Windows | ⏳ da verificare (passo 2) |
| L'adattatore del Vivobook M1607KA supporta il ruolo Peripheral | ⏳ da verificare (passo 1) |
| L'iPhone (iOS 27) si abbina e usa il servizio HID composito | ⏳ da verificare (passo 3) — è la domanda della Fase 0 |
| Mouse relativo + AssistiveTouch su iPhone | 🟡 dovrebbe funzionare (vale per i mouse BLE commerciali), mai provato con il PC come periferica |
| Trascinamento in modalità relativa su iOS 18+ | ❓ una fonte non ufficiale dice di no — da verificare |
| Puntatore assoluto accettato da iOS | ❓ non noto — da verificare (passo 5) |
| La soppressione BR/EDR risolve il problema dual-mode | 🟡 osservato da VirtualBT su Android, mai su iPhone |
| `§`/`<` e `$` sul layout svizzero, caratteri con AltGr | ❓ da verificare: con AltGr probabilmente si ottengono i caratteri del layout Apple, non di Windows |
| AirPlay-Windows funziona con iOS 27 e convive con l'HID sullo stesso adattatore | ⏳ da verificare (passo 6) |

## Fasi successive (non ancora iniziate)

- **Fase 1 — MVP**: finestra con il video dell'iPhone e input tradotto nel punto giusto; scorciatoie Home,
  App Switcher, Centro di Controllo, Spotlight.
- **Fase 2 — Rifinitura**: riconnessione automatica, avvio con un click, tray, rotazione, impostazioni salvate.

## Progetti di riferimento

- [VirtualBT](https://github.com/taowen/BluetoothDemo) — emulatore tastiera/mouse BLE HID per Windows (solo riferimento, vedi sopra)
- [Microsoft BluetoothLEExplorer](https://github.com/microsoft/BluetoothLEExplorer) — esempio ufficiale di server GATT
- [UxPlay](https://github.com/FDH2/UxPlay) e [AirPlay-Windows](https://github.com/Thomas-lab17/AirPlay-Windows) — ricevitore AirPlay

## Licenza

[MIT](LICENSE). Nota: UxPlay/AirPlay-Windows sono GPLv3 e oggi vengono solo eseguiti come programma separato;
se in futuro se ne incorporasse il codice, quella parte dovrebbe restare sotto GPLv3.
