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
- Lange Diktate: siehe ADR-015 (Segmentierung, Session-Rotation).

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
- **Überspringen:** Kurze Transkripte (≤ 20 Wörter), bereits korrekt interpunktiert (Großschreibung am Anfang,
  Satzzeichen am Ende, Komma vor „dass/weil/wenn …“), ohne Füllwörter, Korrekturen, Befehle, Buchstabieren,
  Zahlwörter und Wiederholungen gehen ohne Modellaufruf durch (`SmartSkipHeuristic`, bewusst konservativ).
- **Output-Guard:** Codefences werden entfernt; eine Ausgabe, die deutlich länger ist als das Transkript
  (> 1,3× + 40 Zeichen), gilt als Antwort statt Bereinigung → Rohtext.
- **Nie Diktat verlieren:** Fehler/Timeout (8 s) im Smart-Schritt → Rohtranskript wird eingefügt, Hinweis in
  der Flow Bar. Leere Smart-Ausgabe (nur Füllwörter) → nichts einfügen.
- Der Modus wird beim Aufnahmestart fixiert; Umschalten per Hotkey (`Ctrl+Shift+F12`) oder Tray.
- Beim Aufnahmestart wird die HTTPS-Verbindung per `HEAD` vorgewärmt: erster Aufruf ~0,9 s statt ~3 s.

Gemessen (10 Akzeptanzbeispiele, warm): Smart-Schritt 0,85–1,3 s. Zusammen mit dem finalen Transkript
(~0,6 s) liegt der Smart-Pfad bei ~1,5–2 s, der übersprungene/Raw-Pfad weiterhin bei ~0,6 s.

## ADR-013 – Persönliches Wörterbuch (Phase 5)

- `PersonalDictionary`: Liste von Begriffen in `%APPDATA%\TK Voice\dictionary.json`, Duplikate
  case-insensitiv ausgeschlossen, max. 60 Zeichen, einzeilig.
- Verwendung (FR-019): Transkription erhält die zuletzt hinzugefügten 100 Begriffe als `keywords`
  (vom Realtime-API akzeptiert, verifiziert); Smart Processing erhält alle als Schreibvorgabe.
- Lernen per Hotkey (FR-020, `Ctrl+Shift+F11`): wartet, bis die Hotkey-Modifier losgelassen sind, sichert
  die Zwischenablage, sendet Strg+C, liest nur den Text und stellt die Zwischenablage wieder her.
  Einschränkung: Das Zielprogramm legt die Kopie selbst ab, daher kann sie im Windows-Clipboard-Verlauf
  landen. UI Automation (`TextPattern.GetSelection`) wäre ohne Zwischenablage, wird aber von vielen
  Programmen nicht unterstützt; ggf. später als erster Versuch vor dem Kopieren.
- Lernen beim Diktieren (FR-021): nur bei expliziten Triggern („geschrieben“, „buchstabiert“, „spelled“)
  gefolgt von Einzelbuchstaben. Die Schreibweise wird aus dem eingefügten Text übernommen („Kuhn“ statt
  „KUHN“), sonst die Buchstabenfolge. Abschaltbar: `Dictionary.LearnSpelledTerms`.
- Pflege über Tray → „Wörterbuch …“ (später Teil des Einstellungsfensters).
- Logs enthalten nur die Länge neuer Begriffe, nie den Begriff selbst.

## ADR-014 – App-spezifische Regeln (Phase 6)

- `AppRules` in `settings.json`: Name, Prozessnamen (ohne `.exe`), Stilanweisung. Defaults: Chat
  (Teams, Slack, WhatsApp, …), E-Mail (Outlook klassisch/neu, Thunderbird), Dokument (Word, OneNote,
  Notion, Obsidian), Präsentation (PowerPoint), Entwicklung (VS Code, Cursor, Visual Studio, JetBrains,
  Terminals, Claude). Eigene Liste ersetzt die Defaults vollständig.
- Die Stilanweisung wird als eigene Zeile in den Smart-Input gegeben; sie steuert Ton und Eingriffstiefe,
  überstimmt aber nie explizite Diktatbefehle oder das Verbot, Inhalte hinzuzufügen.
- Kontext bleibt auf Prozessname + Beschreibung der EXE („Microsoft Outlook (OUTLOOK)“) beschränkt
  (FR-022/023). Browser haben keine Regel, da die Website ohne Fenstertitel unbekannt ist.
- `settings.json` wird beim Start mit allen aktuellen Optionen zurückgeschrieben (neue Optionen erscheinen
  mit Default, Benutzerwerte bleiben), Umlaute unescaped für manuelles Bearbeiten.

Verifiziert mit der API: dasselbe Diktat wird in Outlook zur Mail mit Anrede/Absätzen/Gruß, in VS Code
bleiben Bezeichner erhalten („getUserById“, „README.md“), in Teams bleibt es ein knapper Chat-Text.

## ADR-015 – Hands-free und lange Diktate (Phase 7)

- Bedienung: `RightCtrl+Space` startet freihändig bzw. „verriegelt“ ein laufendes Push-to-talk (rechte
  Strg halten, Leertaste dazu, loslassen); ein Tipp auf rechte Strg oder erneut `RightCtrl+Space` beendet.
  Die Flow Bar zeigt „Freihändig · m:ss“.
- Hotkey-Tasten, die keine Modifier sind (Space, F11, F12 …), werden im Hook verschluckt, wenn sie einen
  Hotkey vervollständigen – sonst landet z. B. das Leerzeichen im Text. Entscheidung über den physischen
  Tastenzustand (`GetAsyncKeyState`), damit verpasste Key-ups nichts dauerhaft blockieren.
- **Segmentierung (§45):** Pegelbasierte Pausenerkennung (`SpeechActivityTracker`). Nach ≥ 10 s Audio und
  ≥ 700 ms Pause wird ein Segment committet; es wird transkribiert, während weitergesprochen wird. Gilt auch
  für Push-to-talk → lange Diktate haben nach dem Loslassen nur noch das letzte Segment offen.
  Die Session zählt gesendete Commits vs. Bestätigungen (`committed`/`commit_empty`) und liefert das Ergebnis
  erst, wenn alle bestätigt und transkribiert sind.
- **Speicher:** Audio wird nie gesammelt; Chunks gehen direkt in die WebSocket-Queue.
- **Session-Limit (60 min):** abgelöst durch ADR-016 – jedes Segment hat eine eigene Session; ein Segment
  ohne jede Pause wird nach 55 min zwangsweise geteilt.
- **Stille-Ende (§46):** nur Hands-free, `Audio.HandsFreeSilenceTimeoutSeconds` (0 = aus, Standard, da die
  Spec es als aktivierbare Option beschreibt). Push-to-talk wird nie durch Stille beendet.

Verifiziert mit synthetischer Sprache (Windows TTS, 19,6 s) gegen die API: 5 Segment-Commits während des
Streamens, vollständiger Text in korrekter Reihenfolge, Ergebnis 0,7 s nach Stopp.

## ADR-016 – Zuverlässigkeit: Retry, Timeouts, Fehlerbilder (Phase 8)

- **Eine Realtime-Session pro Segment** (`SegmentedTranscriptionSession`). Das Audio eines Segments wird
  gehalten, bis sein Transkript da ist. Scheitert die Session (Verbindungsabbruch, Timeout, Serverfehler),
  wird das Segment in einer neuen Session aus dem gehaltenen Audio erneut transkribiert – bis zu
  `Processing.MaxRetries` (2) Mal, Backoff 0,5 s / 2 s (FR-035). Danach wird das Audio sofort verworfen,
  ebenso nach Erfolg (FR-037). Speicherbedarf: nur das aktuell offene Segment.
  Ersetzt die Session-Rotation; die nächste Session wird beim Segmentwechsel geöffnet, während weitergesprochen wird.
- **Timeouts (FR-036):** pro Versuch `Processing.TimeoutSeconds` (Transkript nach Segmentende) bzw.
  `Processing.SmartTimeoutSeconds`; der Controller hat darüber nur noch ein Sicherheitsnetz
  (alle Versuche + 15 s).
- **Fehlerklassifikation:** vorübergehend = Netzwerk, Timeout, 408/409/429/5xx, `server_error`,
  `rate_limit_exceeded` → Retry. Konfigurationsfehler (401 ungültiger Key, 403, 404 Modell, `insufficient_quota`)
  → kein Retry, verständliche Meldung („OpenAI API Key ungültig …“). Der WebSocket-Handshake liefert dafür
  den HTTP-Status (`CollectHttpResponseDetails`).
- **Smart Processing** mit derselben Retry-Logik; scheitert es endgültig, wird der Rohtext eingefügt.
- **Mikrofon:** Fehler-Event bei Abbruch; Watchdog meldet einen Ausfall, wenn 2 s lang keine Audiodaten
  kommen (manche Geräte verstummen beim Abziehen nur). Das bis dahin Gesagte wird verarbeitet und eingefügt.
  Startfehler mit klarer Meldung („Kein Mikrofon verfügbar …“).

Verifiziert gegen die API: 19,6 s TTS-Sprache in 6 Sessions/Segmenten korrekt transkribiert; nicht
erreichbarer Server → 3 Versuche mit Replay, klare Fehlermeldung nach 2,6 s.

## ADR-017 – Einstellungsfenster, Tray, Autostart (Phase 9)

- **Laufzeit neu aufbauen statt Neustart:** `TKVoiceRuntime` bündelt alles, was aus den Einstellungen
  entsteht (Pipeline, Hotkeys). „Speichern“ schreibt `settings.json` und baut die Runtime neu auf –
  bei laufendem Diktat erst danach. Tray, Flow Bar, Wörterbuch, Modus und Zwischenablage-Owner bleiben.
- **Einstellungsfenster „TK Voice – Settings“** (§36): Allgemein, Hotkeys, Audio, Verarbeitung, Wörterbuch,
  App-Regeln, OpenAI, Diagnose. Bearbeitet eine Kopie; Wörterbuch-Änderungen gelten sofort.
  Kosten folgen mit Usage Tracking in Phase 10.
- **Hotkey-Aufnahme:** Kombination drücken, übernommen beim Loslassen. Rechte Modifier werden als solche
  gespeichert („RightCtrl“), linke generisch („Ctrl“). Während der Aufnahme sind die globalen Hotkeys
  ausgesetzt (`IHotkeyService.Suspended`), sonst würde z. B. die Leertaste verschluckt.
- **Konflikte (FR-001):** doppelte Belegung innerhalb von TK Voice blockiert das Speichern; von anderen
  Programmen registrierte Kombinationen werden per Probe-`RegisterHotKey` erkannt und als Warnung angezeigt
  (nur für Modifier + eine Taste möglich).
- **Mikrofon per Name** (`Audio.InputDeviceName`), bei jeder Aufnahme neu aufgelöst → Geräte an-/abstecken
  ohne Neustart (FR-004); nicht gefundenes Gerät → Windows-Standard mit Log-Warnung.
- **Tray (FR-043):** aktiv/pausiert (graues Icon, Hotkeys ignoriert, persistiert), Smart/Raw, Einstellungen,
  Wörterbuch, Logs, Beenden; Doppelklick öffnet die Einstellungen.
- **Autostart (FR-042):** `HKCU\...\Run\TK Voice` mit dem Pfad der laufenden EXE; beim Start abgeglichen.
- **OpenAI:** Key-Eingabe (Credential Manager), „Verbindung testen“ prüft Key und beide Modelle über
  `GET /v1/models/{id}`. Der separate API-Key-Dialog entfällt; ohne Key öffnen sich beim Start die Einstellungen.
- **App-Regeln:** „Anwendung erkennen“ gibt 3 s Zeit, zum Zielprogramm zu wechseln, und übernimmt dessen
  Prozessnamen.

## ADR-018 – Fluent-Design (Windows 11)

- WPF-Standardsteuerelemente sehen ohne Theme wie Windows 7 aus. `Application.ThemeMode="System"`
  aktiviert das in .NET 9+ eingebaute Fluent-Theme inkl. Hell/Dunkel nach Windows-Einstellung – ohne
  Fremdbibliothek. Deshalb keine impliziten Control-Styles in `App.xaml` (sie würden Fluent ersetzen);
  eigene Farben nur über Theme-Ressourcen (`DynamicResource …FillColor…Brush`).
- Einstellungsfenster im Stil der Windows-Einstellungen: Navigation links (Akzentstrich statt Vollfläche),
  Einstellungen als Karten mit Symbol, Titel, Beschreibung und Steuerelement; Schalter als eigens
  gestylte CheckBox (WPF hat keinen ToggleSwitch).
- Tray-Menü als WPF-`ContextMenu` (Fluent) statt WinForms-`ContextMenuStrip`; ein unsichtbares
  Host-Fenster sorgt dafür, dass es sich bei Klick daneben schließt.
- Von `TextBox` abgeleitete Controls übernehmen den Fluent-Style nicht automatisch
  (`SetResourceReference(StyleProperty, typeof(TextBox))`).

## ADR-019 – Sicherheit & Diagnose (Phase 10)

- **Passwortfelder (FR-038):** UI Automation liefert für das fokussierte Element `IsPassword` (Win32
  Password-Edits, WPF/WinUI-PasswordBox, `<input type="password">` im Browser). Geprüft wird beim
  Aufnahmestart (dann öffnet sich das Mikrofon gar nicht) und nochmals direkt vor dem Einfügen nach der
  Fokus-Wiederherstellung. Nur dieses eine Flag wird gelesen, nie Inhalte. Zeitlimit 250 ms (gemessen: 4 ms);
  antwortet eine Anwendung nicht, gilt das Feld als normales Feld. UIA wird beim Start vorgewärmt.
  Dafür nutzt `TKVoice.Infrastructure` `UseWPF` (UIAutomationClient).
- **Usage Tracking (FR-040):** `UsageTracker` in `%APPDATA%\TK Voice\usage.json`, je Monat: Diktate,
  Audiosekunden, Smart-Anfragen, Tokens, geschätzte Kosten, bereits gemeldete Schwellen – keine Inhalte.
  Transkription wird nach Audiodauer berechnet (auch bei Fehlschlag, da gestreamtes Audio abgerechnet wird),
  Smart Processing nach den Tokens aus der API-Antwort, Fast mode mit Faktor.
- **Preise (Kostenkonfiguration)** in `settings.json` (`Costs`): 0,017 $/min Transkription, 0,10/0,50 $
  pro Mio. Tokens, Fast mode ×2 – als Schätzung gekennzeichnet.
- **Budget (FR-041):** Monatslimit in USD (0 = aus), Warnschwellen (Standard 50/80 %) je einmal pro Monat
  als Hinweis; bei 100 % blockiert der Controller neue Diktate bis Monatsende oder bis das Limit erhöht wird.
- **Debug-Modus (NFR-007):** standardmäßig aus; wenn an, schreibt `FileDictationDebugLog` Programm, Modus,
  Transkript und eingefügten Text in `%LOCALAPPDATA%\TK Voice\debug\` (getrennt vom technischen Log).
  Sichtbar: „DEBUG“-Badge in der Flow Bar, Tray-Tooltip und Hinweis beim Einschalten. „Debug-Daten löschen“
  entfernt den Ordner.

## ADR-020 – Auslieferung als MSI (Phase 11)

- **WiX Toolset 5 als NuGet-SDK** (`installer/TKVoice.Installer.wixproj`): baut mit `dotnet build`, keine
  globale Installation nötig. MSI, weil es sich als reguläre Anwendung unter „Apps“ einträgt und sauber
  deinstallieren sowie per Major Upgrade aktualisieren lässt.
- **Self-contained Publish** (win-x64, ReadyToRun): .NET-Runtime wird mitinstalliert („benötigte
  Komponenten“), ReadyToRun verkürzt den Kaltstart des ersten Diktats. MSI ≈ 57 MB.
- Installation pro Maschine nach `C:\Program Files\TK Voice\`, Startmenü-Eintrag „TK Voice“, Icon aus
  `assets/TKVoice.ico` (reproduzierbar per `build/New-AppIcon.ps1`).
- **Laufende Instanz:** Vor Installation, Upgrade und Deinstallation ruft das MSI `TKVoice.exe --quit` auf
  (benanntes Event, Fallback: Prozess beenden), damit keine Dateien gesperrt sind.
- **Deinstallation** (nicht beim Upgrade) ruft `TKVoice.exe --uninstall`: entfernt Autostart-Eintrag, Logs und
  Debug-Daten. Einstellungen, Wörterbuch, Nutzungsdaten und API Key bleiben für eine Neuinstallation erhalten
  (vollständig entfernen: `%APPDATA%\TK Voice` löschen, Credential `TKVoice/OpenAI` in der Anmeldeinformationsverwaltung).
- ICE38/43/57 sind unterdrückt: Sie halten den Startmenü-Ordner fälschlich für benutzerbezogen; bei
  `Scope="perMachine"` ist es das Startmenü für alle Benutzer.
- Neue Versionen: `Version` in `Directory.Build.props` erhöhen, `build/Build-Installer.ps1` ausführen, MSI
  installieren – ersetzt die alte Version automatisch. Die `UpgradeCode` bleibt immer gleich.
