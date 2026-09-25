# TK Voice

Persönliche Windows-Diktier-App: **Hotkey halten → sprechen → loslassen → Text steht im ursprünglichen Eingabefeld.**

Anforderungen: [TK Voice – Requirements Specification.md](TK%20Voice%20–%20Requirements%20Specification.md)
Architekturentscheidungen: [docs/architecture.md](docs/architecture.md)

## Stand

- Phase 1 (Vertical Slice): Push-to-talk → Mikrofon → OpenAI Realtime Transcription → Texteingabe ins Ursprungsfenster.
- Phase 2 (Target Handling): Einfügen per Zwischenablage mit Wiederherstellung des vorherigen Inhalts, Fokus auf Ursprungsfenster und -feld.
- Phase 3 (Flow Bar): schwebende Statusanzeige unten mittig mit Wellenform, Verarbeitung, Fehlern.
- Start-/Stoppsignale.
- Phase 8 (Zuverlässigkeit): automatische Wiederholung bei Netzwerk-/Serverfehlern ohne Diktatverlust, klare Fehlermeldungen, Mikrofonausfall.
- Phase 7 (Hands-free): freihändige Aufnahme ohne Längenbegrenzung, optionales Stille-Ende.
- Phase 6 (App-Regeln): Stil je Programm – Chat knapp, E-Mail ausformuliert, Entwicklung ohne Umformulierung.
- Phase 5 (Wörterbuch): Begriffe verbessern Erkennung und Schreibweise; lernen per Markieren + Hotkey oder Buchstabieren.
- Phase 4 (Smart Mode): Füllwörter, Selbstkorrekturen, Zahlen, Buchstabieren, Formatierungs- und Korrekturbefehle.

Noch kein Einstellungsfenster (Konfiguration über `settings.json`).

## Voraussetzungen

- Windows 10/11
- .NET 10 SDK
- OpenAI API Key

## Bauen, testen, starten

```powershell
dotnet build TKVoice.slnx
dotnet test TKVoice.slnx
dotnet run --project src/TKVoice.App
```

Beim ersten Start fragt TK Voice nach dem OpenAI API Key und speichert ihn im Windows Credential Manager
(`TKVoice/OpenAI`). Später jederzeit über das Tray-Menü → „OpenAI API Key hinterlegen …“.

## Bedienung

- **Push-to-talk:** rechte Strg-Taste halten, sprechen, loslassen.
- **Freihändig:** rechte Strg halten und Leertaste dazu drücken → Aufnahme läuft ohne Halten weiter. Beenden mit einem Tipp auf rechte Strg.
  Optional automatisches Ende nach Stille: `Audio.HandsFreeSilenceTimeoutSeconds` in `settings.json`.
- **Smart/Raw umschalten:** `Strg+Umschalt+F12` oder Tray-Menü. Smart bereinigt und formatiert, Raw fügt das Transkript unverändert ein.
- **Wörterbuch:** Begriff markieren + `Strg+Umschalt+F11`, oder beim Diktieren buchstabieren („NEONEX, geschrieben N-E-O-N-E-X“). Pflege über Tray → „Wörterbuch …“.
- Das Diktat wird eingefügt (Strg+V); die vorherige Zwischenablage ist danach wieder da. Ein Strg+Z im Zielprogramm entfernt das ganze Diktat.
- Flow Bar unten mittig: roter Punkt + Wellenform = Aufnahme, oranger Punkt + Lauflicht = Verarbeitung.
- Tray-Icon: blau = bereit, rot = Aufnahme, orange = Verarbeitung.

## Dateien

| Was | Wo |
|---|---|
| Einstellungen | `%APPDATA%\TK Voice\settings.json` |
| Wörterbuch | `%APPDATA%\TK Voice\dictionary.json` |
| Technische Logs (ohne Inhalte) | `%LOCALAPPDATA%\TK Voice\logs\` |
| API Key | Windows Credential Manager, `TKVoice/OpenAI` |

App-Regeln stehen unter `AppRules` in `settings.json`: Prozessname (steht im Log bei „Recording started … Target:“)
plus Stilanweisung. Eine eigene Liste ersetzt die Standardregeln.

Hotkeys werden in `settings.json` als Tastenkombination angegeben, z. B. `"RightCtrl"`, `"Ctrl+Win"`, `"Ctrl+Shift+F9"`.
Änderungen werden nach einem Neustart von TK Voice wirksam.
