# Fase 0 — Risultati

Compila e rimandami questo file insieme ai log (`%TEMP%\iPhoneMirror\`).
Legenda: ✅ funziona · ⚠️ funziona in parte (spiega) · ❌ non funziona · — non provato

## Ambiente

| Voce | Valore |
|---|---|
| iPhone / iOS | iPhone 16 Pro Max, iOS 27.0.1 |
| Windows (edizione, versione, build) | |
| Adattatore Bluetooth (chip, VID/PID) | |
| Driver Bluetooth (versione, data) | |
| `IsPeripheralRoleSupported` | |
| iPhone già abbinato al PC prima dei test? (Phone Link) | |
| Nome con cui il PC compare sull'iPhone | |

## Passo 2 — Build e avvio

| Verifica | Esito | Note |
|---|---|---|
| Build + registrazione pacchetto | | |
| Package Identity OK | | |
| Advertising `Started` | | |

## Passo 3 — Puntatore relativo (soppressione BR/EDR: no)

| Verifica | Esito | Note |
|---|---|---|
| Il PC compare in Impostazioni > Bluetooth | | |
| Il PC compare in AssistiveTouch > Dispositivi | | |
| Strada di abbinamento usata (A / B) | | |
| Prompt di abbinamento su iPhone / su Windows (codice?) | | |
| `Iscritti alle notifiche` tastiera / mouse / consumer | | |
| Puntatore visibile | | |
| Movimento (fluidità, direzione) | | |
| Click sinistro | | |
| Click destro | | |
| Trascinamento con tasto premuto | | |
| Rotella (verso) | | |
| Pulsante «angolo in alto a sinistra» | | |
| Riconnessione dopo standby / dopo Ferma-Avvia | | |

### Tastiera

| Verifica | Esito | Note |
|---|---|---|
| Nome della voce di layout scelta sull'iPhone | | |
| Pulsante «Scrivi Zürich 123» → testo ottenuto | | |
| Lettere, numeri, z/y | | |
| ü ö ä è é à | | |
| `§` e `<` (scambiati?) | | |
| `$` | | |
| AltGr+2 / AltGr+3 / AltGr+7 → caratteri ottenuti | | |
| Invio, Backspace, frecce, Tab, Esc | | |
| Blocco maiuscole (+ `Keyboard LEDs written` nel log?) | | |
| Win+Spazio / pulsante Cmd+Spazio → Spotlight | | |
| Pulsante Cmd+H | | |

### Tasti consumer

| Voce | Effetto sull'iPhone |
|---|---|
| 0x0223 AC Home | |
| 0x0040 Menu | |
| 0x0221 AC Search | |
| 0x00B8 Eject | |
| 0x01AE AL Keyboard Layout | |
| 0x00CD Play/Pausa | |
| 0x00E9 / 0x00EA Volume | |
| 0x00E2 Mute | |

## Passo 4 — Soppressione BR/EDR (solo se il passo 3 è fallito)

| Verifica | Esito | Note |
|---|---|---|
| Riga stato: rilevabile=False connettibile=False | | |
| Abbinamento riuscito con soppressione | | |
| Ripristino dopo «Ferma» (connettibile=True) | | |

## Passo 5 — Puntatore assoluto

| Verifica | Esito | Note |
|---|---|---|
| L'iPhone accetta il dispositivo / puntatore visibile | | |
| Posizione corrispondente (angoli, centro; errore stimato) | | |
| Click preciso su icone | | |
| Trascinamento | | |
| Rotella | | |

## Passo 6 — Video AirPlay

| Verifica | Esito | Note |
|---|---|---|
| Duplicazione schermo funzionante | | |
| Qualità / scatti / blocchi | | |
| Latenza misurata (3 prove, ms) | | |
| Video + HidProbe insieme: HID ancora funzionante? | | |
| Serve togliere «Enable Bluetooth Discovery»? | | |
| Usabilità guardando solo il PC | | |

## Conclusione

- [ ] ✅ Puntatore + click + tastiera funzionano → Fase 1
- [ ] ❌ Non funziona dopo i tentativi ragionevoli → piano B (ESP32)

Note libere:
