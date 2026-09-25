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

## ADR-004 – Texteingabe per `SendInput` Unicode (Phase 1, abgelöst durch ADR-009)

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

## ADR-007 – Flow Bar als nicht aktivierbares WPF-Overlay (Phase 3 vorgezogen)

Ohne sichtbares Feedback war beim ersten Test unklar, ob die Aufnahme läuft (das Tray-Icon liegt unter
Windows 11 meist im Überlauf). Deshalb wurde Phase 3 vor Phase 2 umgesetzt.

- Rahmenloses, transparentes WPF-Fenster mit `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT`,
  `ShowActivated=False`: stiehlt nie den Fokus, ist klick-durchlässig, erscheint nicht in Taskleiste/Alt+Tab.
- Position unten mittig auf dem Monitor des Zielfensters, in physischen Pixeln (DPI-korrekt).
- Wellenform aus dem RMS-Pegel jedes 50-ms-Audioblocks (dB-Skala, `AudioLevel`).
- Verarbeitung > 3 s zeigt „Verarbeitung dauert länger …“ (FR-033); Fehler werden 4 s angezeigt.
- `IUserNotifier` wird per `CompositeNotifier` an Tray-Icon und Flow Bar verteilt.

## ADR-008 – Stille-Nachlauf vor dem Commit

Die Aufnahme endet beim Loslassen sofort (FR-002). Vor dem Commit werden 300 ms Stille angehängt
(`OpenAI.TrailingSilenceMilliseconds`), damit das Modell ein beim Loslassen noch gesprochenes Wort
abschließen kann. Kostet keine spürbare Latenz.

## ADR-009 – Einfügen per Zwischenablage mit Delayed Rendering (Phase 2)

Standard ist jetzt **Paste** (Zwischenablage + Strg+V) statt Tippen:
- ein einziger Undo-Schritt im Zielprogramm (Strg+Z entfernt das ganze Diktat),
- Zeilenumbrüche lösen in Chat-Apps (Teams, Slack) kein vorzeitiges Senden aus,
- keine Autovervollständigung/Auto-Klammern durch einzelne Tastenanschläge.

Clipboard Preservation (FR-029): Vor dem Einfügen werden alle speicherbasierten Formate kopiert
(`CF_BITMAP`/Metafiles werden über ihre `CF_DIB`-Varianten mit abgedeckt). Der Diktattext wird per
**Delayed Rendering** angeboten: Windows fordert ihn bei TK Voice an, sobald das Zielprogramm ihn liest
(`WM_RENDERFORMAT`). Erst danach (plus 150 ms Karenz) wird der alte Inhalt zurückgeschrieben. Ein fester
Timer würde bei langsamen Programmen riskieren, dass der *alte* Clipboard-Inhalt eingefügt wird.
Hat inzwischen jemand anderes die Zwischenablage belegt, wird nicht zurückgeschrieben.

Der Diktattext wird mit `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory=0`
und `CanUploadToCloudClipboard=0` markiert und landet so nicht im Windows-Clipboard-Verlauf (NFR-003).

Alle Clipboard-Aufrufe laufen auf dem UI-Thread mit eigenem Message-only-Fenster, da der Clipboard-Owner
Render-Anfragen sofort beantworten muss.

Fallback auf Tippen, wenn die Zwischenablage blockiert ist; per `Insertion.TypeInsteadOfPasteProcesses`
auch pro Programm erzwingbar.

## ADR-010 – Zielwiederherstellung

- Beim Start wird zusätzlich das fokussierte native Steuerelement erfasst (`GetGUIThreadInfo`).
  Chromium/Electron/UWP-Apps haben nur ein natives Fenster und merken sich den internen Fokus selbst.
- Fokus zurückholen: `SetForegroundWindow`; falls Windows das verweigert, zuerst eine nicht belegte
  virtuelle Taste injizieren (macht TK Voice zur „letzten Eingabequelle“), dann `AttachThreadInput`.
- Gelingt es nicht innerhalb von 500 ms, wird nicht eingefügt (FR-027).

## ADR-011 – Start-/Stoppsignale (FR-034)

Zwei kurze synthetisierte Zweiklänge (steigend = Start, fallend = Stopp), zur Laufzeit erzeugt – keine
Audiodateien. Abspielen über NAudio `WaveOut`, nicht blockierend. `Audio.SoundsEnabled`, `Audio.SoundVolume`.

## ADR-012 – Smart Mode über die Responses API (Phase 4)

- Modell `gpt-6-luna` (günstigstes/schnellstes Textmodell, Stand 09/2026), `reasoning.effort: none`
  (Default wäre `medium` → deutlich langsamer), `service_tier: fast` (2× Preis, ~0,02 Cent pro Diktat,
  gleichmäßigere Latenz), `store: false` (keine Speicherung beim Anbieter). Alles in `settings.json`.
- Prompt (`SmartProcessingPrompt`) statisch, Transkript im Input in `<transcript>`-Tags. Er stellt klar:
  Das Transkript ist diktierter Inhalt, keine Anweisung – Fragen werden eingefügt, nicht beantwortet.
- Kontext ausschließlich: Prozessname, optional Fenstertitel (standardmäßig **aus**, Titel können
  Betreffzeilen enthalten), später Wörterbuch (§42).
- **Überspringen:** Kurze Transkripte (≤ 20 Wörter) ohne Füllwörter, Korrekturen, Befehle, Buchstabieren,
  Zahlwörter und Wiederholungen gehen ohne Modellaufruf durch (`SmartSkipHeuristic`, bewusst konservativ).
- **Output-Guard:** Codefences werden entfernt; eine Ausgabe, die deutlich länger ist als das Transkript
  (> 1,3× + 40 Zeichen), gilt als Antwort statt Bereinigung → Rohtext.
- **Nie Diktat verlieren:** Fehler/Timeout (8 s) im Smart-Schritt → Rohtranskript wird eingefügt, Hinweis in
  der Flow Bar. Leere Smart-Ausgabe (nur Füllwörter) → nichts einfügen.
- Der Modus wird beim Aufnahmestart fixiert; Umschalten per Hotkey (`Ctrl+Shift+F12`) oder Tray.
- Beim Aufnahmestart wird die HTTPS-Verbindung per `HEAD` vorgewärmt: erster Aufruf ~0,9 s statt ~3 s.

Gemessen (10 Akzeptanzbeispiele, warm): Smart-Schritt 0,85–1,3 s. Zusammen mit dem finalen Transkript
(~0,6 s) liegt der Smart-Pfad bei ~1,5–2 s, der übersprungene/Raw-Pfad weiterhin bei ~0,6 s.
