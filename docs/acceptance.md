# TK Voice 1.0 – Akzeptanzkriterien (§48)

| AC | Kriterium | Umsetzung | Nachweis |
|---|---|---|---|
| AC-01 | Push-to-talk in Notepad, deutscher Satz | Phase 1 | manuell bestätigt |
| AC-02 | Teams, deutsch mit englischen Begriffen | Prompt: keine Übersetzung, Wörterbuch-Keywords | API-Test („Shopfloor Management“, „First Pass Yield“), manuell |
| AC-03 | „Dienstag, nein Mittwoch um vierzehn Uhr“ | Smart Mode: Selbstkorrektur | API-Test: „Wir treffen uns Mittwoch um 14 Uhr.“ |
| AC-04 | „fünfundzwanzig Prozent Ausschuss“ | Smart Mode: Zahlen/Einheiten | API-Test: „Wir haben 25 % Ausschuss.“ |
| AC-05 | „NEONEX, geschrieben N-E-O-N-E-X“ | Smart Mode + Lernen ins Wörterbuch | API-Test: „NEONEX“, Unit-Tests `SpelledTermDetector` |
| AC-06 | Start in Outlook, Wechsel zu Chrome | Ziel beim Start fixiert, Fokus-Wiederherstellung | Unit-Test, manuell |
| AC-07 | Internet kurz weg | Segment-Replay mit bis zu 2 Retries, klare Fehlermeldung | Unit-Tests, API-Test mit unerreichbarem Server, manuell |
| AC-08 | Clipboard bleibt erhalten | Snapshot + Delayed Rendering, Restore nach dem Lesen | manuell bestätigt |
| AC-09 | Neustart behält Einstellungen | `settings.json`, `dictionary.json`, Credential Manager | Unit-Tests, manuell |
| AC-10 | Kein Audio, keine Historie gespeichert | Audio nur im RAM je Segment, keine Texte in Logs/Usage | Code-Review, Unit-Test `usage.json` |
| AC-11 | Füllwörter im Smart Mode entfernt | Prompt | API-Test: „Wir müssen morgen mit Peter sprechen.“ |
| AC-12 | Raw Mode näher am Gesprochenen | Raw = Transkript unverändert | Unit-Test |
| AC-13 | Hands-free ohne Zeitlimit | Segmente je eigene Session, kein Limit | Unit-Tests, TTS-API-Test, manuell |
| AC-14 | Passwortfeld blockiert | UI Automation `IsPassword` vor Aufnahme und Einfügen | Test mit echtem Passwortfeld, Unit-Tests |
| AC-15 | API Key nicht im Klartext | Windows Credential Manager, nie in Logs | Unit-Test (keine Key-Felder in `settings.json`), Code-Review |

Installer (NFR-008): Installation, Start über das Startmenü und Diktat manuell bestätigt (25.09.2026).
