# TK Voice – Requirements Specification
## Personal Windows AI Dictation App

**Version:** 1.0  
**Status:** MVP Requirements Baseline  
**Produktname:** TK Voice  
**Repository:** `tk-voice`  
**Executable:** `TKVoice.exe`  
**Namespace:** `TKVoice`  
**Zielplattform:** Windows  
**Nutzerkreis:** ausschließlich ein persönlicher Nutzer  
**Referenzprodukt:** Wispr Flow hinsichtlich des grundlegenden Diktier-Workflows, kein 1:1-Klon

---

# 1. Produktvision

TK Voice ist eine schlanke Windows-Desktop-Anwendung, mit der Sprache systemweit in Text umgewandelt werden kann.

Der Nutzer aktiviert TK Voice über einen globalen Hotkey, spricht frei und erhält nach Beendigung des Diktats einen automatisch transkribierten und optional intelligent bereinigten Text direkt im ursprünglich aktiven Eingabefeld.

Der Kernworkflow soll sich möglichst unmittelbar anfühlen:

**Hotkey → sprechen → Hotkey loslassen/stoppen → Verarbeitung → Text erscheint**

TK Voice soll insbesondere schneller und komfortabler sein als manuelles Tippen und dabei natürliche Sprache, Selbstkorrekturen, Formatierungen, Fachbegriffe sowie unterschiedliche Anwendungskontexte berücksichtigen.

---

# 2. MVP-Ziele

Der MVP MUSS:

1. ausschließlich unter Windows funktionieren,
2. systemweit verfügbar sein,
3. Push-to-talk und Hands-free-Diktieren unterstützen,
4. Sprache über die OpenAI API transkribieren,
5. Transkriptionen intelligent nachbearbeiten können,
6. den finalen Text direkt in das ursprünglich aktive Textfeld einfügen,
7. Deutsch, Englisch und gemischte Sprache automatisch verarbeiten,
8. App-spezifische Textoptimierung ermöglichen,
9. persönliche Begriffe lernen können,
10. keinerlei lokale Diktat-Historie führen,
11. möglichst geringe wahrgenommene Latenz besitzen,
12. als normale Windows-Anwendung installierbar sein.

---

# 3. Nicht-Ziele des MVP

Folgende Funktionen gehören ausdrücklich nicht zum MVP:

- Mehrbenutzerfähigkeit
- Benutzerkonten
- Cloud-Synchronisation
- Mobile App
- macOS-Version
- Browser-Erweiterung
- Diktat-Historie
- Audio-Historie
- Dashboard oder Statistiken
- automatisches Software-Update
- Offline-Spracherkennung
- Bearbeitung bereits vorhandener Texte per Sprache
- automatisches Auslesen von Dokument-, E-Mail- oder Chat-Inhalten
- automatische Analyse nachträglicher manueller Korrekturen
- zentraler Backend-Service für mehrere Nutzer

---

# 4. Kern-User-Flow

## UF-01 – Push-to-talk

1. Nutzer befindet sich in einem beliebigen Textfeld.
2. Nutzer hält den konfigurierten Push-to-talk-Hotkey.
3. TK Voice speichert Zielanwendung und ursprüngliches Eingabefeld.
4. Flow Bar erscheint.
5. Startsound wird abgespielt, sofern aktiviert.
6. Mikrofonaufnahme startet.
7. Audio wird während des Sprechens bereits gestreamt/transkribiert.
8. Nutzer lässt Hotkey los.
9. Aufnahme endet sofort.
10. Stopsound wird abgespielt.
11. Flow Bar wechselt auf „Processing“.
12. finales Transkript wird erstellt.
13. Smart Processing oder Raw Processing wird angewendet.
14. ursprüngliches Ziel wird wieder fokussiert.
15. Text wird vollständig eingefügt.
16. Flow Bar verschwindet.
17. temporäres Audio und temporäre Textdaten werden verworfen.

---

# 5. Hands-free-Flow

## UF-02 – Hands-free

1. Nutzer drückt den konfigurierten Start/Stop-Hotkey.
2. TK Voice speichert Zielanwendung und Eingabefeld.
3. Aufnahme startet.
4. Nutzer kann beliebig lange sprechen.
5. Aufnahme endet durch:
   - erneutes Drücken des Hotkeys oder
   - optional durch konfigurierbare längere Stille.
6. Verarbeitung erfolgt anschließend analog zu Push-to-talk.

Es darf keine künstliche maximale Diktatlänge geben.

Lange Aufnahmen müssen technisch so verarbeitet werden, dass Speicherverbrauch und Stabilität nicht von der Gesamtlänge abhängig explodieren.

---

# 6. Funktionale Anforderungen

## FR-001 – Globale Hotkeys

TK Voice MUSS globale Windows-Hotkeys unterstützen.

Mindestens folgende Aktionen benötigen frei konfigurierbare Hotkeys:

- Push-to-talk
- Start/Stop Hands-free
- Smart/Raw-Modus wechseln
- Begriff zum Wörterbuch hinzufügen

Hotkeys dürfen nicht fest im Code verankert werden.

Konflikte mit bereits vergebenen globalen Hotkeys SOLLEN erkannt werden.

Wenn technisch sinnvoll, SOLLEN auch zusätzliche Maustasten als Auslöser unterstützt werden.

---

## FR-002 – Push-to-talk

Solange der Push-to-talk-Hotkey gehalten wird, MUSS aufgenommen werden.

Beim Loslassen MUSS die Aufnahme unmittelbar beendet werden.

Eine automatische Stilleerkennung darf Push-to-talk nicht eigenständig beenden.

---

## FR-003 – Hands-free

Ein separater Hotkey MUSS eine längere Aufnahme starten und stoppen können.

Optional MUSS der Nutzer eine automatische Beendigung nach einer konfigurierbaren Dauer ohne erkannte Sprache aktivieren können.

---

# 7. Audioaufnahme

## FR-004 – Mikrofon

Der Nutzer MUSS das verwendete Aufnahmegerät auswählen können.

TK Voice MUSS mit dem Windows-Standardmikrofon funktionieren.

Gerätewechsel während des Betriebs SOLLEN ohne vollständigen Neustart möglich sein.

---

## FR-005 – Streaming

Audio SOLL bereits während der Aufnahme an die Transkriptionspipeline übertragen werden.

Ziel ist, möglichst viel Transkriptionsarbeit bereits während des Sprechens abzuschließen.

Der Text darf trotzdem erst nach Beendigung der Aufnahme in das Zielprogramm eingefügt werden.

---

# 8. OpenAI-Transkription

Für Live-Transkription soll die aktuelle OpenAI-Realtime-Transkriptionsschnittstelle verwendet werden.

Das konkrete Modell darf nicht tief in die Businesslogik hardcodiert werden.

Die Transkriptionskomponente MUSS austauschbar bzw. konfigurierbar aufgebaut werden.

Die Architektur soll insbesondere folgende Funktionen verwenden können:

- Streaming-Audio
- partielle Transkription
- finales Transkript
- Sprachhinweise
- Keyword-/Vocabulary-Hints

---

## FR-006 – Mehrsprachigkeit

TK Voice MUSS mehrere Sprachen automatisch erkennen können.

Insbesondere MUSS funktionieren:

- Deutsch
- Englisch
- deutsche Sprache mit englischen Fachbegriffen
- Code-Switching innerhalb eines Diktats

Beispiel:

„Wir müssen das Shopfloor Management verbessern und den First Pass Yield messen.“

Englische Begriffe dürfen nicht unnötig übersetzt oder eingedeutscht werden.

---

# 9. Raw Mode

## FR-007 – Raw Transcription

TK Voice MUSS einen Raw Mode besitzen.

Der Raw Mode soll:

- möglichst nah am Gesprochenen bleiben,
- keine umfangreiche Umformulierung durchführen,
- Inhalt nicht interpretieren,
- lediglich technisch notwendige Satzzeichen und elementare Formatierung ergänzen.

---

# 10. Smart Mode

## FR-008 – Smart Processing

Smart Mode ist der Standardmodus.

Er MUSS:

- klassische Füllwörter entfernen,
- Versprecher bereinigen,
- Grammatik korrigieren,
- Satzzeichen ergänzen,
- Wiederholungen reduzieren,
- offensichtliche Selbstkorrekturen berücksichtigen,
- sinnvolle Absätze erzeugen,
- Listen erkennen,
- Zahlen und Einheiten sinnvoll formatieren.

Der ursprüngliche Inhalt und die Intention dürfen dabei nicht verändert werden.

---

## FR-009 – Konservatives Verhalten bei Unsicherheit

Die KI soll moderat optimieren.

Offensichtliche Intentionen dürfen interpretiert werden.

Bei Unsicherheit MUSS die Ausgabe möglichst nah am Gesprochenen bleiben.

TK Voice darf:

- keine neuen Fakten hinzufügen,
- keine Aussagen erfinden,
- keine inhaltliche Erweiterung vornehmen, die nicht aus dem Diktat hervorgeht.

---

# 11. Füllwörter

## FR-010 – Füllwortbereinigung

Typische Füllwörter sollen automatisch entfernt werden.

Beispiele:

- äh
- ähm
- quasi
- also, sofern rein als Füllwort verwendet
- unnötige Wiederholungen

Der persönliche Sprachstil soll ansonsten möglichst erhalten bleiben.

---

# 12. Selbstkorrekturen

## FR-011 – Natürliche Selbstkorrekturen

Folgende Sprachmuster müssen verstanden werden können:

„Dienstag – nein, Mittwoch.“

„Um 13 Uhr, ich meine 14 Uhr.“

„Martin – Korrektur – Markus.“

Ausgabe:

Nicht beide Varianten ausgeben, sondern die korrigierte Aussage.

---

## FR-012 – Explizite Korrekturbefehle

Smart Mode SOLL Sprachbefehle verstehen wie:

- „letztes Wort löschen“
- „letzten Satz löschen“
- „letzte drei Wörter löschen“
- „zurück“
- „ersetze X durch Y“

Diese Befehle beziehen sich auf das aktuelle Diktat, nicht auf bereits vorhandenen Text im Zielprogramm.

---

# 13. Formatierungsbefehle

## FR-013 – Klassische Befehle

Unterstützt werden sollen unter anderem:

- neue Zeile
- neuer Absatz
- Punkt
- Komma
- Doppelpunkt
- Semikolon
- Klammer auf
- Klammer zu

---

## FR-014 – Intelligente Formatierung

Zusätzlich sollen Anweisungen möglich sein wie:

- „mach daraus drei Bulletpoints“
- „schreib das als Liste“
- „mach daraus einen Absatz“
- „Überschrift“
- „danach neuer Absatz“

Explizite Formatierungsbefehle haben Vorrang vor automatisch erkannter Struktur.

---

## FR-015 – Automatische Struktur

Auch ohne explizite Befehle soll Smart Mode erkennen können, ob sich aus dem Gesprochenen sinnvoll ergibt:

- Fließtext
- mehrere Absätze
- Liste
- nummerierte Punkte

---

# 14. Zahlen, Daten und Einheiten

## FR-016 – Intelligente Formatierung

Gesprochene Zahlen sollen situationsabhängig formatiert werden.

Beispiele:

„fünfundzwanzig Prozent“

→ `25 %`

„dreitausendzweihundert Euro“

→ `3.200 €`

„siebzig Minuten“

→ `70 Minuten` oder abhängig vom Kontext `70 Min.`

„dreizehnter August zweitausendsechsundzwanzig“

→ geeignete Datumsdarstellung

Die Formatierung soll kontextabhängig erfolgen und nicht ausschließlich aus starren Ersetzungsregeln bestehen.

---

# 15. Buchstabieren

## FR-017 – Spelling Detection

TK Voice MUSS erkennen können, wenn ein Begriff buchstabiert wird.

Beispiel:

„NEONEX, geschrieben N-E-O-N-E-X“

→ `NEONEX`

Explizite Trigger wie:

- „geschrieben“
- „buchstabiert“

haben hohe Priorität.

Auch offensichtliches Buchstabieren ohne Trigger SOLL nach Möglichkeit erkannt werden.

---

# 16. Persönliches Wörterbuch

## FR-018 – Custom Vocabulary

Es MUSS ein lokales persönliches Wörterbuch geben.

Einträge können beispielsweise sein:

- Personennamen
- Firmennamen
- Produktnamen
- Abkürzungen
- Fachbegriffe

Beispiel:

`NEONEX`

`FPY`

`Shopfloor Management`

---

## FR-019 – Verwendung des Wörterbuchs

Wörterbucheinträge sollen sowohl für:

1. die Transkriptionsphase als auch
2. die Smart-Processing-Phase

als Kontext verwendet werden.

Keyword-Hints des Transkriptionsmodells sollen verwendet werden, wenn verfügbar.

---

# 17. Lernen neuer Begriffe

TK Voice darf nicht nach jeder Einfügung automatisch das Zieltextfeld überwachen.

Lernen erfolgt ausschließlich über explizite Interaktion.

## FR-020 – Manueller Lernmechanismus

Der Nutzer kann einen Begriff markieren und über einen Hotkey bzw. eine Aktion:

**„Zum Wörterbuch hinzufügen“**

auslösen.

Nur der bewusst ausgewählte Begriff darf dafür gelesen werden.

---

## FR-021 – Lernen beim Diktieren

Konstruktionen wie:

„NEONEX, geschrieben N-E-O-N-E-X“

können automatisch als möglicher Wörterbucheintrag erkannt werden.

Der Begriff kann daraufhin lokal gespeichert werden.

---

# 18. Anwendungskontext

## FR-022 – Aktive Anwendung erkennen

Beim Start eines Diktats MUSS die aktive Anwendung erkannt werden.

Verwendbare Kontextinformationen sind ausschließlich:

- Prozessname
- Anwendungsname
- optional Fenstertitel

Beispiele:

- Outlook
- Teams
- Chrome
- Word
- PowerPoint
- Claude
- VS Code

---

## FR-023 – Kein Inhaltszugriff

TK Voice darf im MVP NICHT automatisch:

- E-Mails lesen,
- Chats lesen,
- Dokumentinhalte lesen,
- Text um den Cursor auslesen,
- vorherigen Text im Eingabefeld analysieren.

---

# 19. App-spezifische Smart-Regeln

## FR-024

Es gibt einen globalen Smart Mode.

Zusätzlich können einzelne Anwendungen abweichende Regeln erhalten.

Beispiele:

### Teams / Chat
- natürlich
- relativ knapp
- geringe sprachliche Überarbeitung

### Outlook
- vollständige Sätze
- stärkere sprachliche Glättung
- professionell, aber nicht unnötig steif

### Word
- saubere Absätze
- strukturierter Fließtext

### Claude Code / Entwicklungsumgebung
- möglichst geringe Umformulierung
- technische Begriffe erhalten
- Code-Begriffe nicht sprachlich verändern

Es gibt keine separate Profilverwaltung im MVP.

---

# 20. Zieltextfeld

## FR-025 – Target Capture

Beim Start eines Diktats MUSS sich TK Voice merken:

- aktives Fenster
- Zielprozess
- soweit technisch möglich das fokussierte Eingabeelement

Dieses Ziel bleibt während des gesamten Diktats erhalten.

---

## FR-026 – Fensterwechsel

Wechselt der Nutzer während der Aufnahme in eine andere Anwendung, darf sich das Einfügeziel dadurch NICHT ändern.

Beispiel:

Diktat wird in Outlook gestartet.

Während der Aufnahme wechselt Nutzer zu Chrome.

Nach Aufnahmeende muss der Text trotzdem in Outlook eingefügt werden.

---

## FR-027 – Ungültiges Ziel

Existiert das ursprüngliche Ziel nach Abschluss nicht mehr oder kann es nicht sicher wiederhergestellt werden:

**NICHT in ein anderes Feld einfügen.**

Stattdessen:

- Einfügen abbrechen
- Nutzer informieren

---

# 21. Texteingabe

## FR-028 – Universelle Einfügung

TK Voice soll möglichst universell in Windows-Textfelder einfügen können.

Die technische Einfügemethode darf abhängig vom Ziel variieren.

Mögliche Strategien:

1. Windows UI Automation
2. simulierte Tastatureingabe
3. Clipboard + Paste
4. geeignete Windows APIs

---

## FR-029 – Clipboard Preservation

Wenn die Zwischenablage verwendet wird:

1. aktuellen Clipboard-Inhalt sichern,
2. Diktattext temporär setzen,
3. einfügen,
4. ursprünglichen Clipboard-Inhalt wiederherstellen.

Der vorherige Clipboard-Inhalt darf nicht dauerhaft überschrieben werden.

---

# 22. Direct Insert

## FR-030

Es gibt im MVP keinen Preview-Dialog.

Nach erfolgreicher Verarbeitung wird der Text unmittelbar eingefügt.

Workflow:

**Stop → Processing → Insert**

---

# 23. Undo

Es gibt keinen eigenen Undo-Mechanismus.

Der Nutzer verwendet das normale:

`Ctrl + Z`

der jeweiligen Zielanwendung.

Nach erfolgreicher Verarbeitung muss daher kein Diktat für eine Undo-Funktion vorgehalten werden.

---

# 24. TK Voice Flow Bar

## FR-031 – Recording State

Während der Aufnahme erscheint eine kleine schwebende Flow Bar.

Sie zeigt:

- Audioaktivität bzw. Wellenform
- klar erkennbaren Aufnahmezustand

Sie darf:

- nicht den Fokus stehlen,
- nicht das Zieltextfeld deaktivieren,
- möglichst wenig Bildschirmfläche beanspruchen.

---

## FR-032 – Processing State

Nach Aufnahmeende bleibt die Flow Bar bestehen.

Sie wechselt von:

**Recording**

zu:

**Processing**

Nach erfolgreichem Einfügen verschwindet sie.

---

## FR-033 – Slow Processing

Dauert die Verarbeitung ungewöhnlich lange, zeigt die Flow Bar einen dezenten Hinweis wie:

**„Verarbeitung dauert länger …“**

---

# 25. Audiofeedback

## FR-034

Optional werden kurze Sounds abgespielt bei:

- Aufnahme gestartet
- Aufnahme beendet

Der Nutzer kann:

- Sounds vollständig deaktivieren
- Lautstärke einstellen

---

# 26. Fehlerbehandlung

## FR-035 – API Retry

Bei vorübergehenden Fehlern erfolgen automatisch bis zu zwei Retry-Versuche.

Beispiele:

- Netzwerkfehler
- Timeout
- temporärer API-Fehler

---

## FR-036 – Timeout

Für OpenAI Requests muss ein konfigurierbares Timeout existieren.

Nach Timeout:

1. Retry durchführen
2. bei erneutem Scheitern Verarbeitung beenden
3. Fehlermeldung anzeigen

---

## FR-037 – Temporäres Audio

Audio darf während Retry-Versuchen temporär erhalten bleiben.

Nach:

- erfolgreicher Verarbeitung oder
- endgültigem Abbruch

muss es gelöscht werden.

---

# 27. Sensible Felder

## FR-038 – Password Protection

Wenn ein Eingabefeld eindeutig als Passwortfeld erkannt wird:

- keine normale Diktierfunktion anbieten bzw.
- Einfügen blockieren.

Feingranulare Ausnahmen sind kein Bestandteil des MVP.

---

# 28. Smart/Raw-Umschaltung

## FR-039

Der aktuelle Modus muss jederzeit zwischen:

- Smart
- Raw

wechselbar sein.

Der Wechsel soll über:

- Einstellungen
- globalen Hotkey

möglich sein.

Smart ist der Default.

---

# 29. Latenz

## NFR-001 – Perceived Latency

TK Voice soll sich unmittelbar anfühlen.

Ziel für kurze Diktate:

**idealerweise < 1 Sekunde zwischen Aufnahmeende und Texteingabe.**

Dies ist ein Performance-Ziel und kein absolutes Funktionskriterium.

Qualität und Stabilität haben Vorrang vor künstlicher Einhaltung der Grenze.

---

## NFR-002 – Streaming Optimization

Die Architektur soll möglichst viel Verarbeitung bereits während der Aufnahme durchführen.

Die finale Smart-Nachbearbeitung sollte nur das bereits vorhandene finale Transkript bearbeiten.

---

# 30. Datenschutz und lokale Speicherung

## NFR-003 – Keine History

TK Voice führt keine lokale Diktat-Historie.

Nach erfolgreicher Verarbeitung dürfen nicht persistent gespeichert werden:

- Audio
- Rohtranskript
- finales Diktat
- Zieltext

---

## NFR-004 – Zulässige lokale Persistenz

Persistent gespeichert werden dürfen ausschließlich notwendige Konfigurationen wie:

- Hotkeys
- Mikrofoneinstellung
- Wörterbuch
- App-Regeln
- Audiofeedback-Einstellungen
- Retry-/Timeout-Werte
- Kostenkonfiguration
- technische Logs
- API-Konfiguration

---

# 31. OpenAI API Key

## NFR-005 – Secure Storage

Der OpenAI API Key darf nicht im Klartext in einer normalen Konfigurationsdatei abgelegt werden.

Für Windows soll ein geeigneter sicherer Credential-Mechanismus verwendet werden, beispielsweise:

**Windows Credential Manager**

oder ein vergleichbarer Windows-Sicherheitsmechanismus.

Der API Key darf niemals in Logs erscheinen.

---

# 32. Kostenkontrolle

## FR-040 – Usage Tracking

TK Voice soll technische Nutzungsdaten erfassen können, insbesondere:

- Audio-Dauer
- Anzahl Requests
- verwendetes Modell
- Token-/Usage-Daten, sofern verfügbar
- geschätzte Kosten

Nicht gespeichert werden:

- Audioinhalt
- Diktattext

---

## FR-041 – Monatsbudget

Der Nutzer kann ein monatliches Kostenlimit hinterlegen.

Zusätzlich können Warnschwellen definiert werden.

Beispiel:

- Warnung bei 50 %
- Warnung bei 80 %
- Blockade bei 100 %

Das Limit wird lokal von TK Voice durchgesetzt.

Kosten dürfen als **Schätzung** gekennzeichnet werden.

---

# 33. Logging

## NFR-006 – Normal Logging

Im Normalbetrieb dürfen Logs enthalten:

- Zeitstempel
- Komponentenzustände
- Latenzen
- API Request IDs
- Modellname
- Fehlercodes
- Retry-Versuche
- Nutzungsmetriken

Nicht enthalten:

- Audio
- vollständige Transkripte
- fertige Texte
- API Key

---

## NFR-007 – Debug Mode

Es darf einen bewusst aktivierbaren erweiterten Debug-Modus geben.

Dieser darf zur Entwicklung zusätzliche Informationen erfassen.

Wenn dadurch Inhalte gespeichert werden könnten:

- muss dies deutlich angezeigt werden,
- Debug Mode muss standardmäßig deaktiviert sein,
- Debug-Daten müssen einfach löschbar sein.

---

# 34. Startverhalten

## FR-042

TK Voice kann automatisch mit Windows starten.

Nach Start:

- läuft TK Voice im Hintergrund,
- Hotkeys sind aktiv,
- Hauptfenster muss nicht geöffnet sein.

---

# 35. System Tray

## FR-043

Im Windows System Tray muss ein **TK Voice Icon** vorhanden sein.

Mindestens folgende Aktionen:

- Aktivieren/Pausieren
- Einstellungen öffnen
- Smart/Raw anzeigen bzw. wechseln
- Beenden

---

# 36. Einstellungsfenster

Fenstertitel:

**TK Voice – Settings**

Das Einstellungsfenster soll bewusst kompakt bleiben.

Mindestens folgende Bereiche:

### General
- Autostart
- TK Voice aktiv/pausiert

### Hotkeys
- Push-to-talk
- Hands-free
- Smart/Raw
- Wörterbuch hinzufügen

### Audio
- Mikrofon
- Start-/Stopsounds
- Lautstärke
- Stille-Timeout Hands-free

### Processing
- Smart/Raw Default
- Processing Timeout
- Retry-Konfiguration

### Dictionary
- Einträge anzeigen
- hinzufügen
- bearbeiten
- löschen

### App Rules
- Anwendung erkennen
- spezifische Smart-Regeln definieren

### OpenAI
- API Key
- Verbindung testen
- Modellkonfiguration

### Costs
- aktueller Monatsverbrauch
- Warnschwellen
- lokales Limit

### Diagnostics
- Log-Level
- Debug Mode
- Logs öffnen/löschen

---

# 37. Keine History-UI

Es darf keine Seite geben für:

- vergangene Diktate
- letzte Transkriptionen
- Audioaufnahmen
- Textsuche über vergangene Eingaben

---

# 38. Installierbarkeit

## NFR-008

TK Voice wird als reguläre Windows-Anwendung ausgeliefert.

Der Installer soll als Produktnamen verwenden:

**TK Voice**

Installationsziel beispielsweise:

```text
C:\Program Files\TK Voice\
```

Executable:

```text
TKVoice.exe
```

Der Installer soll mindestens:

- TK Voice installieren
- benötigte Komponenten installieren
- Startmenü-Eintrag `TK Voice` anlegen
- saubere Deinstallation ermöglichen

Automatische Updates sind nicht erforderlich.

Neue Versionen werden manuell installiert.

---

# 39. Empfohlene technische Architektur

Diese Architektur ist eine Empfehlung und keine harte Produktanforderung.

## Desktop

Empfohlen:

**C# / .NET Desktop-Anwendung**

mit einer ausgereiften Windows-UI-Technologie.

Gründe:

- globale Hotkeys
- Windows APIs
- UI Automation
- Credential Manager
- Audio APIs
- System Tray
- Fokus-/Fenstersteuerung
- Installer
- geringe zusätzliche Runtime-Komplexität

Die konkrete UI-Technologie kann nach einem kurzen technischen Spike ausgewählt werden.

---

# 40. Solution- und Projektstruktur

Empfohlene Solution:

```text
TKVoice.sln
```

Mögliche Projektstruktur:

```text
TKVoice
│
├── TKVoice.App
├── TKVoice.Core
├── TKVoice.Infrastructure
├── TKVoice.OpenAI
└── TKVoice.Tests
```

Innerhalb der Anwendung sollen mindestens folgende Services logisch getrennt sein:

```text
TK Voice
│
├── HotkeyService
├── AudioCaptureService
├── TranscriptionService
├── SmartProcessingService
├── DictionaryService
├── ApplicationContextService
├── TargetCaptureService
├── TextInsertionService
├── ClipboardService
├── OverlayService
├── SettingsService
├── CredentialService
├── UsageService
├── LoggingService
└── TrayService
```

Die OpenAI-Integration darf nicht direkt in UI-Code eingebaut werden.

---

# 41. Processing Pipeline

Empfohlener Datenfluss:

```text
Global Hotkey
      ↓
Capture Target
      ↓
Start Audio Capture
      ↓
Streaming Transcription
      ↓
Final Transcript
      ↓
┌─────────────────────┐
│ Raw Mode            │
│       ODER          │
│ Smart Processing    │
└─────────────────────┘
      ↓
Restore Original Target
      ↓
Insert Text
      ↓
Delete Temporary Data
```

---

# 42. Smart Processing Input

Das Smart-Processing-Modell darf erhalten:

- finales Transkript
- verwendete Sprache(n)
- aktiver App-Name
- optional Fenstertitel
- persönliches Wörterbuch
- Formatierungsregeln
- App-spezifische Regel

Es darf NICHT erhalten:

- vorherigen Text des Zielprogramms
- gesamten Fensterinhalt
- Clipboard-Inhalt
- andere geöffnete Dokumente

---

# 43. Smart Processing Output Contract

Die Smart-Processing-Komponente soll ausschließlich den final einzufügenden Text zurückgeben.

Keine Antworten wie:

„Hier ist dein verbesserter Text:“

Keine Markdown-Codefences, sofern nicht ausdrücklich aus dem Diktat hervorgehend.

Kein Kommentar zur Bearbeitung.

Nur:

```text
<finaler einzufügender Text>
```

---

# 44. OpenAI-Modellkonfiguration

Modelle dürfen über Konfiguration austauschbar sein.

Mindestens zwei logische Modellrollen:

### Transcription Model
Live Speech-to-Text

### Smart Processing Model
Textbereinigung und Strukturierung

Kein anderer Anwendungscode darf von einer konkreten Modell-ID abhängig sein.

---

# 45. Lange Diktate

Bei langen Hands-free-Aufnahmen muss TK Voice Streaming bzw. sinnvolle Segmentierung unterstützen.

Nicht akzeptabel wäre:

- komplette unkomprimierte Audioaufnahme dauerhaft im RAM sammeln,
- erst nach 30 Minuten mit der Transkription beginnen.

Die Transkription soll während des Sprechens entstehen.

---

# 46. Pause Detection

Stilleerkennung ist nur für Hands-free relevant.

Sie muss:

- deaktivierbar sein
- Dauer konfigurierbar machen

Push-to-talk ignoriert automatische Beendigung durch Stille.

---

# 47. Sicherheitsprinzipien

TK Voice muss mindestens folgende Prinzipien erfüllen:

- Least Privilege
- keine Speicherung unnötiger Inhalte
- Secrets nicht loggen
- keine Textfeldüberwachung im Hintergrund
- keine versteckte Clipboard-Analyse
- keine Aufnahme ohne sichtbaren Aufnahmeindikator
- Mikrofon nur während aktiver Diktierphase verwenden

---

# 48. Akzeptanzkriterien – MVP

Der MVP gilt als funktional abgeschlossen, wenn mindestens folgende Tests erfolgreich sind.

### AC-01
In Notepad Push-to-talk starten, deutschen Satz sprechen und Hotkey loslassen.

**Erwartung:** Text erscheint korrekt in Notepad.

### AC-02
In Teams deutschen Satz mit englischen Begriffen sprechen.

**Erwartung:** Begriffe bleiben sinnvoll englisch.

### AC-03
Sprechen:

„Wir treffen uns Dienstag, nein Mittwoch um vierzehn Uhr.“

**Erwartung:** Nur Mittwoch erscheint im finalen Text.

### AC-04
Sprechen:

„Wir haben fünfundzwanzig Prozent Ausschuss.“

**Erwartung:** sinnvolle Zahlenformatierung.

### AC-05
Sprechen:

„NEONEX, geschrieben N-E-O-N-E-X.“

**Erwartung:** `NEONEX`.

### AC-06
Outlook als Ziel starten, während Aufnahme zu Chrome wechseln.

**Erwartung:** Text wird anschließend in Outlook eingefügt.

### AC-07
Während eines Diktats Internetverbindung kurz unterbrechen.

**Erwartung:** definierter Retry bzw. sauberer Fehlerzustand.

### AC-08
Vor Diktat beliebigen Text kopieren.

Diktat ausführen.

Danach Paste testen.

**Erwartung:** ursprünglicher Clipboard-Inhalt ist weiterhin vorhanden.

### AC-09
TK Voice schließen und erneut starten.

**Erwartung:** persönliche Hotkeys, Wörterbuch und Einstellungen bleiben erhalten.

### AC-10
Diktat durchführen und anschließend lokalen Datenspeicher prüfen.

**Erwartung:** kein gespeichertes Audio und keine Diktat-Historie.

### AC-11
Smart Mode:

„Ähm also wir müssen quasi morgen äh mit Peter sprechen.“

**Erwartung:** Füllwörter werden sinnvoll entfernt.

### AC-12
Raw Mode mit demselben Diktat.

**Erwartung:** deutlich näher am tatsächlich Gesprochenen.

### AC-13
Hands-free starten und längeren Text diktieren.

**Erwartung:** keine künstliche Zeitbegrenzung.

### AC-14
Passwortfeld fokussieren und Hotkey aktivieren.

**Erwartung:** Diktieren/Einfügen wird blockiert.

### AC-15
API Key in TK Voice hinterlegen.

**Erwartung:** Key ist nach Neustart nutzbar, erscheint aber weder in normalen Config-Dateien noch Logs im Klartext.

---

# 49. Priorisierte Implementierungsreihenfolge

## Phase 1 – Vertical Slice

Zunächst ausschließlich:

```text
Hotkey
→ Mikrofon
→ OpenAI Transcription
→ Text
→ aktives Notepad-Feld
```

Noch ohne Smart Processing und umfangreiche UI.

Ziel:

**End-to-End-Workflow beweisen.**

---

## Phase 2 – Target Handling

Implementieren:

- ursprüngliches Fenster erfassen
- Fokus wiederherstellen
- universelle Texteingabe
- Clipboard Preservation

---

## Phase 3 – TK Voice Flow Bar

Implementieren:

- Recording
- Wellenform
- Processing
- Fehlerstatus

---

## Phase 4 – Smart Mode

Implementieren:

- Transkriptbereinigung
- Selbstkorrekturen
- Füllwörter
- Zahlen
- Struktur
- Formatierungsbefehle

---

## Phase 5 – Dictionary

Implementieren:

- lokales Wörterbuch
- Keyword-Hints
- Hinzufügen/Bearbeiten/Löschen
- Spelling Detection

---

## Phase 6 – Application Context

Implementieren:

- aktive App erkennen
- App-spezifische Regeln
- kein Lesen von Fensterinhalten

---

## Phase 7 – Hands-free

Implementieren:

- Start/Stop
- lange Sessions
- Silence Detection

---

## Phase 8 – Reliability

Implementieren:

- Timeouts
- Retry
- Fehlerbehandlung
- Netzwerkprobleme
- Mikrofonprobleme
- API-Fehler

---

## Phase 9 – Settings & Tray

Implementieren:

- TK Voice Tray Icon
- Einstellungsfenster
- Autostart
- Hotkeys
- Audio
- Dictionary
- App Rules
- OpenAI
- Kosten

---

## Phase 10 – Security & Diagnostics

Implementieren:

- Credential Storage
- Password Field Detection
- Logging
- Debug Mode
- Usage Tracking
- Budget Limits

---

## Phase 11 – Packaging

Implementieren:

- `TKVoice.exe`
- Release Build
- TK Voice Installer
- Deinstallation
- manuelle Upgradefähigkeit

---

# 50. Entwicklungsprinzip für Claude Code

Nicht versuchen, alle Funktionen gleichzeitig zu implementieren.

Nach jeder Phase:

1. Build erfolgreich.
2. relevante Tests erfolgreich.
3. TK Voice manuell ausführbar.
4. keine Regression des Kernflows.
5. Commit erstellen.

Neue Architekturentscheidungen sollen dokumentiert werden.

Bei Unsicherheit:

**einfachste robuste Lösung für einen persönlichen Windows-MVP bevorzugen.**

Nicht vorsorglich Infrastruktur für ein SaaS-Produkt bauen.

---

# 51. Definition of Done

Version 1.0 des persönlichen TK-Voice-MVP ist fertig, wenn der Nutzer unter Windows in einer normalen Anwendung einen Hotkey drücken, natürlich sprechen und nach Loslassen zuverlässig einen sinnvoll transkribierten und bereinigten Text im ursprünglichen Textfeld erhalten kann.

Der gesamte Vorgang soll sich im Normalfall wie eine einzige Interaktion anfühlen:

**Drücken → sprechen → loslassen → Text steht da.**