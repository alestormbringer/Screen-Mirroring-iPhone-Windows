---
progetto: iPhone Mirror per Windows (nome provvisorio)
stato: Fase 0 — verifica di fattibilità
strada: zero hardware (Bluetooth del PC come periferica HID)
fallback: dongle ESP32 BLE HID (solo se la Fase 0 fallisce)
creato: 2026-10-05
tags: [progetto, iphone, windows, bluetooth, airplay]
---

# Brief per Claude — iPhone Mirror per Windows

## Chi sono e cosa voglio

Sono Alessio, sviluppatore autodidatta (Python, FastAPI, automazioni, VPS). Voglio un tool **locale e gratuito** per Windows che mi permetta di vedere e controllare il mio iPhone con mouse e tastiera, il più vicino possibile a iPhone Mirroring di macOS. L'alternativa commerciale è iMyFone MirrorTo, che non voglio pagare.

Lavoriamo **per fasi**. Adesso voglio solo la **Fase 0**: non costruire l'app completa finché la Fase 0 non è superata.

## Come funzionano i tool commerciali (e quindi il nostro)

Non c'è nessuna API Apple per controllare l'iPhone da Windows. I tool commerciali combinano due cose separate:

1. **Video via AirPlay**: l'iPhone fa "Duplica schermo" verso un ricevitore AirPlay sul PC.
2. **Input via Bluetooth HID**: il PC si presenta all'iPhone come mouse + tastiera Bluetooth. iOS mostra il puntatore tramite AssistiveTouch.

Noi replichiamo la stessa architettura con componenti open source.

## Architettura

| Componente | Soluzione | Stato |
|---|---|---|
| Ricevitore video | UxPlay (AirPlay mirror server, GPLv3) — fork Windows pronto: AirPlay-Windows | Esiste già, va solo integrato |
| Iniezione input | Server GATT BLE con HID over GATT (HOGP, servizio 0x1812) scritto in C# con WinRT `Windows.Devices.Bluetooth` | **Da costruire — è il cuore del progetto** |
| Finestra di controllo | App Windows che mostra il video e traduce mouse/tastiera in report HID | Da costruire (Fase 1) |

### Vincoli tecnici noti (già ricercati)

- Il server GATT BLE su Windows **richiede Package Identity**: l'app va eseguita come pacchetto MSIX (anche solo registrato in sviluppo con `Add-AppxPackage -Register`). Se avviata come exe non pacchettizzato, le API restituiscono successo ma il servizio HID non viene davvero pubblicato.
- L'adattatore Bluetooth deve supportare il **ruolo Peripheral** (`BluetoothAdapter.IsPeripheralRoleSupported`).
- Rischio noto: alcuni telefoni vedono il PC come dispositivo dual-mode (BR/EDR + LE) e provano a collegarsi in HID Bluetooth classico invece che in BLE/HOGP, fallendo. Soluzione già sperimentata da altri: sopprimere temporaneamente inquiry/page scan BR/EDR via `BluetoothAPIs.dll` (`BluetoothEnableDiscovery` / `BluetoothEnableIncomingConnections`) mentre si fa advertising BLE, e ripristinare all'uscita.
- Il progetto di riferimento è stato testato **solo su Android**. Il comportamento con iPhone è proprio ciò che la Fase 0 deve verificare.
- Puntatore: un mouse HID standard è **relativo** (dx/dy). Da verificare se iOS 18+ accetta un descrittore HID con coordinate **assolute** (es. 0–4095), che eviterebbe la calibrazione. Una fonte (non ufficiale) dice di sì e dice anche che su iOS 18+ in modalità relativa il trascinamento con tasto premuto non funziona più. **Va verificato, non darlo per scontato.**
- Tastiera: i codici HID sono **posizionali**. Il layout lo decide l'iPhone (Impostazioni > Generali > Tastiera > Tastiere hardware), quindi va impostato uguale a quello del PC.

### Progetti di riferimento

- VirtualBT (porting WinUI 3 / .NET 8 di un emulatore tastiera/mouse BLE HID per Windows): https://github.com/taowen/BluetoothDemo
- Microsoft BluetoothLEExplorer (esempio ufficiale UWP di server GATT): https://github.com/microsoft/BluetoothLEExplorer
- UxPlay: https://github.com/FDH2/UxPlay
- AirPlay-Windows (fork Windows di UxPlay con installer): https://github.com/Thomas-lab17/AirPlay-Windows

## Il mio ambiente

- PC: Asus Vivobook 16 M1607KA, Windows, 32 GB RAM, grafica integrata (nessuna GPU dedicata)
- iPhone: **modello e versione iOS da compilare: ____**
- Adattatore Bluetooth del PC: **da verificare in Gestione dispositivi: ____**
- Strumenti disponibili: Python; posso installare Visual Studio 2022 Community / .NET 8 SDK se serve

## Fase 0 — Verifica di fattibilità (l'unica da fare adesso)

Obiettivo: rispondere a una sola domanda, **"l'iPhone accetta il mio PC come mouse/tastiera BLE?"**

Passi:

1. **Check adattatore**: un piccolo programma o script che stampi se l'adattatore supporta il ruolo Peripheral (e chip/driver).
2. **Prototipo HID minimo** in C# (.NET 8, pacchettizzato MSIX in modalità sviluppo): pubblica un servizio HID con mouse (report relativo 4 byte: pulsanti, dx, dy, rotella) + tastiera (report 8 byte standard). Può partire da VirtualBT o essere scritto da zero, valuta tu cosa è più pulito e motivalo.
3. **Test di accoppiamento** con l'iPhone: accoppiamento da Impostazioni > Bluetooth o da Accessibilità > Tocco > AssistiveTouch > Dispositivi. Verificare che compaia il puntatore, che si muova, che il click funzioni e che la tastiera scriva.
4. Se l'iPhone tenta il collegamento classico e fallisce: implementare la soppressione BR/EDR e ritestare.
5. **Test descrittore assoluto**: seconda variante del mouse con X/Y assoluti; verificare se iOS lo accetta e se click e trascinamento in punti precisi funzionano.
6. **Test video**: installare AirPlay-Windows, duplicare lo schermo e misurare a occhio la latenza.

**Criterio di uscita**:
- ✅ Puntatore + click + tastiera funzionano → si passa alla Fase 1.
- ❌ Non si riesce a far funzionare l'HID con l'iPhone dopo i tentativi ragionevoli → ci fermiamo e passo al piano B (dongle ESP32 via seriale).

Per ogni test dammi istruzioni passo passo (comandi PowerShell esatti, dove cliccare sull'iPhone) e dimmi cosa annotare. Aggiungi log dettagliati su file (es. in `%TEMP%`) per capire dove si rompe.

## Fasi successive (solo come contesto, NON implementare ora)

**Fase 1 — MVP**: finestra unica che mostra il video dell'iPhone; click, trascinamento, rotella e tastiera vengono tradotti in report HID nel punto giusto (mappatura coordinate finestra → schermo iPhone, con calibrazione se il puntatore è relativo); scorciatoie per Home, App Switcher, Centro di Controllo e Spotlight. Per il video, valutare: overlay trasparente sopra la finestra di UxPlay (più semplice) oppure incorporare il flusso video nell'app (più pulito).

**Fase 2 — Rifinitura**: riconnessione automatica, avvio con un click di video + HID, icona nella tray, gestione rotazione schermo, impostazioni salvate.

## Regole di lavoro

- Tutto **in locale**: nessun servizio cloud, nessuna telemetria.
- Spiegami le scelte tecniche in breve prima di scrivere molto codice; se ci sono alternative, proponile con pro e contro.
- Se ti manca un'informazione (versione iOS, adattatore, ecc.), chiedimela invece di supporre.
- Non dare per verificato ciò che è solo ipotizzato: separa sempre "funziona (testato)" da "dovrebbe funzionare".
- Codice in inglese (nomi e commenti), spiegazioni a me in italiano.

## Limiti accettati rispetto al Mac

Lo schermo dell'iPhone resta acceso durante il controllo, il puntatore di AssistiveTouch è visibile, c'è un po' di latenza AirPlay, i gesti multitouch vanno sostituiti da scorciatoie, niente drag-and-drop di file né notifiche integrate. Va bene così.
