# TK Voice – Architekturentscheidungen

Laufendes Protokoll der Entscheidungen (Requirements §50). Neueste unten.

## Projektstruktur

| Projekt | Inhalt | Plattform |
|---|---|---|
| `TKVoice.Core` | Abstraktionen, `DictationController`, Hotkey-Logik, Settings | `net10.0` (plattformneutral, testbar) |
| `TKVoice.OpenAI` | Realtime-Transkription (WebSocket-Protokoll) | `net10.0` |
| `TKVoice.Infrastructure` | Windows-Implementierungen: Keyboard-Hook, Mikrofon, Zielfenster, Texteingabe, Credential Manager, Logs | `net10.0-windows` |
| `TKVoice.App` | Composition Root, Tray-Icon, Dialoge → `TKVoice.exe` | `net10.0-windows`, WPF |
| `TKVoice.Tests` | xUnit | `net10.0` |

Kein DI-Container: die Verdrahtung in `App.OnStartup` ist klein genug, um sie explizit zu halten.

## ADR-001 – .NET 10 + WPF

.NET 10 ist LTS (.NET 8 endet im November 2026). WPF statt WinUI 3: ausgereift, rahmenlose Overlay-Fenster
(Flow Bar) und Tray-Icon ohne MSIX-Paketierung. Das Tray-Icon nutzt `System.Windows.Forms.NotifyIcon`.
Die Solution liegt im neuen Format `TKVoice.slnx`.

## ADR-002 – Globale Hotkeys über Low-Level-Keyboard-Hook

`RegisterHotKey` meldet kein Loslassen, Push-to-talk braucht das aber. Deshalb `WH_KEYBOARD_LL`:

- Hook läuft auf eigenem Thread mit Message-Loop und reiht Tastenereignisse nur ein; Auswertung
  (`HotkeyMatcher`) und Events laufen auf einem separaten Dispatch-Thread. So bremst TK Voice nie die
  systemweite Tastatureingabe.
- Tasten werden durchgereicht, nicht verschluckt.
- Injizierte Eingaben (`LLKHF_INJECTED`) werden ignoriert, damit die eigene Texteingabe keine Hotkeys auslöst.
- Verpasste Key-ups (z. B. UAC/Secure Desktop) werden per `GetAsyncKeyState`-Abgleich alle 250 ms erkannt.
- Default Push-to-talk: rechte Strg-Taste (auf deutschen Tastaturen selten genutzt; AltGr ist tabu).

## ADR-003 – Transkription: OpenAI Realtime, `gpt-live-transcribe`, manueller Commit

- WebSocket `wss://api.openai.com/v1/realtime?intent=transcription`, Session-Typ `transcription`,
  24 kHz PCM16 mono.
- Modell `gpt-live-transcribe` liefert Deltas bereits während des Sprechens; es unterstützt keine
  Server-VAD, daher `turn_detection: null` und ein `input_audio_buffer.commit` am Aufnahmeende.
  Finales Transkript = `…transcription.completed` aller committeten Items.
- Die Verbindung wird beim Hotkey-Druck aufgebaut; Audio wird bis dahin in einer Queue gepuffert,
  die Aufnahme wartet nie auf das Netzwerk.
- Modell, URL, `delay` und Sprachhinweise (`languages`) stehen in `settings.json`. `keywords` und `prompt`
  sind im Protokoll vorbereitet (Wörterbuch, Phase 5).
- Offen für Phase 7: lange Hands-free-Diktate brauchen Segmentierung mit Commit bei Sprechpausen;
  eine etwaige maximale Session-Dauer ist zu prüfen und ggf. per Session-Wechsel zu umgehen.

## ADR-004 – Texteingabe per `SendInput` Unicode (Phase 1)

Unicode-Tastenanschläge (`KEYEVENTF_UNICODE`) funktionieren in praktisch allen Textfeldern und lassen die
Zwischenablage unberührt. Zeilenumbrüche werden als Enter gesendet. Vor dem Tippen wird gewartet, bis
alle Modifier losgelassen sind, damit keine Shortcuts entstehen.

Das Ziel ist das beim Start erfasste Vordergrundfenster. Ist es nicht wieder fokussierbar
(`SetForegroundWindow`, notfalls mit `AttachThreadInput`), wird **nicht** eingefügt (FR-027).
Phase 2 ergänzt: fokussiertes Eingabeelement, Clipboard+Paste für lange Texte mit Clipboard Preservation.

## ADR-005 – API Key im Windows Credential Manager

Generic Credential `TKVoice/OpenAI`, gelesen/geschrieben über `CredRead`/`CredWrite`.
Nie in `settings.json`, nie in Logs.

## ADR-006 – Logs ohne Inhalte

Eigener minimaler Datei-Logger, tägliche Datei unter `%LOCALAPPDATA%\TK Voice\logs`. Geloggt werden
Zustände, Latenzen, Zeichenanzahlen und Fehlercodes – nie Audio, Transkripte, eingefügter Text oder Keys.
