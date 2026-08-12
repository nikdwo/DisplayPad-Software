# Release-Checkliste

## Automatisch vor jedem Release

- `dotnet restore DisplayPadRemote.sln`
- `dotnet build DisplayPadRemote.sln -c Release --no-restore`
- `dotnet test DisplayPadRemote.sln -c Release --no-build`
- `dotnet list DisplayPadRemote.sln package --vulnerable --include-transitive` ohne Treffer
- `publish.cmd`; beide EXE-Dateien müssen vorhanden und startbar sein
- `git diff --check` und sauberer Arbeitsbaum des Release-Commits

## Frische Windows-VM

- Host und Agent starten ohne vorinstallierte .NET-Runtime.
- Agent erzeugt genau eine Konfiguration und ein Zertifikat unter `%LocalAppData%\DisplayPadRemote\Agent`.
- Host lehnt fehlenden, falschen und unvollständigen Fingerabdruck ab.
- Kopierte Verbindungsdaten ermöglichen nach manueller Fingerabdruckprüfung `/ping` und eine Testaktion.
- Tokenrotation macht das alte Token sofort ungültig.
- Firewallregel ist auf `profile=private` beschränkt.
- DE/EN-Wechsel aktualisiert aktuelle Geräte-, Agent-, OBS- und Base-Camp-Statusmeldungen.
- Beschädigte Host-Konfiguration wird aus `.bak` sichtbar wiederhergestellt; ohne gültige Sicherung erfolgt ein sichtbarer Startabbruch.
- Beschädigte Agent-Konfiguration führt sichtbar zum Startabbruch und erzeugt kein neues Token.

## Hardwarematrix (manuelles Release-Gate)

| Bereich | Variante | Erwartung |
|---|---|---|
| DisplayPad | direkt per USB | Erkennung, APEnable, 12 Bilder, Tastendrücke |
| DisplayPad | USB-Hub | identisches Verhalten, kontrolliertes Abziehen/Anstecken |
| Seiten | normale Seite | vollständiger Upload; Aktionen erst danach aktiv |
| Ordner | Ebene 1 und Ebene 8 | 11 Inhalte plus reservierte Zurück-Taste |
| Fehlerfall | Abziehen während Upload | kein Crash; kein falscher Runtime-Commit; Rollback oder sichtbare Sperre |
| Base Camp | installiert/laufend | Konflikt sichtbar; Deaktivierung und Status verifiziert |
| Base Camp | nicht installiert | keine Ausnahme; korrekter Status |
| OBS | lokal und LAN | Verbinden, Trennen, erneutes Verbinden ohne doppelte Handler |
| Remote | Rechnername und IPv4 | exakter Zertifikat-Pin, Token, Rate-Limit |

Ergebnisse mit Windows-Version, DisplayPad-Firmware, SDK-Version, USB-Pfad und Host-/Agent-Version dokumentieren. Hardwaretests bleiben bewusst ein manuelles Release-Gate.
