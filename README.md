# TK Voice

Persönliche Windows-Diktier-App: **Hotkey halten → sprechen → loslassen → Text steht im ursprünglichen Eingabefeld.**

Anforderungen: [TK Voice – Requirements Specification.md](TK%20Voice%20–%20Requirements%20Specification.md)
Architekturentscheidungen: [docs/architecture.md](docs/architecture.md)

## Stand

Phase 1 (Vertical Slice): Push-to-talk → Mikrofon → OpenAI Realtime Transcription → Texteingabe ins Ursprungsfenster.
Noch kein Smart Processing, keine Flow Bar, kein Einstellungsfenster.

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
- Tray-Icon: blau = bereit, rot = Aufnahme, orange = Verarbeitung.

## Dateien

| Was | Wo |
|---|---|
| Einstellungen | `%APPDATA%\TK Voice\settings.json` |
| Technische Logs (ohne Inhalte) | `%LOCALAPPDATA%\TK Voice\logs\` |
| API Key | Windows Credential Manager, `TKVoice/OpenAI` |

Hotkeys werden in `settings.json` als Tastenkombination angegeben, z. B. `"RightCtrl"`, `"Ctrl+Win"`, `"Ctrl+Shift+F9"`.
Änderungen werden nach einem Neustart von TK Voice wirksam.
