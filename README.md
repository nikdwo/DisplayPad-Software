# DisplayPad Remote

Steuert per **Mountain DisplayPad** Programme auf dem **Hauptrechner und/oder einem zweiten Windows-Rechner**: Hotkeys senden und Befehle/Skripte ausführen — pro Taste wählbar. Mehrere **Seiten** (je 12 Tasten) lassen sich per Tastendruck am Pad durchschalten. Basiert auf dem offiziellen [Mountain DisplayPad SDK](https://github.com/Mountain-BC/DisplayPad.SDK.Demo).

```
Rechner A (DisplayPad)                        Rechner B (wird gesteuert)
┌────────────────────────────┐   HTTP + Token ┌──────────────────────────┐
│ DisplayPad.Host (WPF-GUI)  │ ─────────────► │ DisplayPad.Agent (Tray)  │
└────────────────────────────┘   Port 5599    └──────────────────────────┘
```

## Bauen

Voraussetzung (nur auf dem Entwicklungsrechner): .NET 8 SDK oder neuer.

**Für die Weitergabe an andere Rechner:** `publish.cmd` ausführen — erzeugt self-contained Einzeldateien, die die .NET-Runtime mitbringen. Auf dem Zielrechner muss **kein** .NET installiert sein (behebt den Fehler „You must install .NET Desktop Runtime"):

- Agent für den Zweitrechner: `publish\Agent\DisplayPad.Agent.exe` (nur diese eine Datei rüberkopieren; `agent.json` und `agent.log` entstehen daneben beim ersten Start)
- Host: `publish\Host\DisplayPad.Host.exe`

Für die lokale Entwicklung reicht `dotnet build DisplayPadRemote.sln` (Ausgabe unter `src\...\bin\...`, benötigt installierte .NET-Runtime).

## Einrichtung

**Auf Rechner B (Zweitrechner):**
1. `DisplayPad.Agent.exe` starten → läuft als Tray-Icon, erzeugt beim ersten Start ein Token in `agent.json`.
2. Tray-Icon → „Token in Zwischenablage kopieren".
3. Eingehende Firewall-Regel für Port 5599 (TCP) anlegen:
   `netsh advfirewall firewall add rule name="DisplayPad Agent" dir=in action=allow protocol=TCP localport=5599`
4. Optional: Verknüpfung in `shell:startup` legen, damit der Agent mit Windows startet.

**Auf Rechner A (DisplayPad angeschlossen):**
1. Base Camp beenden (kann sonst mit dem SDK um das Gerät konkurrieren).
2. `DisplayPad.Host.exe` starten.
3. Oben auf **„Remote"** klicken und IP des Zweitrechners, Port 5599 und das Token eintragen → Punkt wird grün, wenn der Agent erreichbar ist.
4. Taste im Raster anklicken → Beschriftung, Icon und Aktion festlegen:
   - **Hotkey:** ins Eingabefeld klicken und die Kombination drücken (z.B. `Ctrl+Alt+F1`). Medien-Tasten wie `MediaPlayPause`, `VolumeUp` können auch von Hand eingetragen werden.
   - **Befehl:** wird via `cmd /c` ausgeführt, z.B. `start "" "C:\Program Files\obs-studio\bin\64bit\obs64.exe"`.
   - **Ausführen auf:** Zweitrechner, Hauptrechner (dieser PC) oder Beiden. Für rein lokale Tasten ist kein Agent nötig.
   - **Seite wechseln:** schaltet zur nächsten/vorherigen/einer bestimmten Seite (mit Wrap-around).
5. **Seiten:** Über dem Raster Seiten anlegen („+"), umbenennen und löschen („–"). Das Raster zeigt immer die aktive Seite. Wird eine Seitenwechsel-Taste am Pad gedrückt, überträgt die App automatisch die Icons der neuen Seite aufs Pad.
6. „Aktion jetzt testen" führt die Aktion des ausgewählten Eintrags sofort aus.
7. „Speichern" schreibt die Konfiguration, „Auf Gerät übertragen" rendert Icons + Beschriftungen der aktiven Seite und lädt sie auf die Pad-Tasten (alle 12, leere Tasten werden schwarz).

Konfiguration: `%AppData%\DisplayPadRemote\config.json`

## Feintuning (config.json)

- **`KeyMatrixMap`**: Zuordnung SDK-KeyMatrix-Code → Tastenindex. Beim Drücken zeigt die Statuszeile den rohen Code an („Taste gedrückt (KeyMatrix-Code X)"). Stimmt die Zuordnung nicht, die 12 Codes in Pad-Reihenfolge (links oben → rechts unten) in das Array eintragen.
- **`UploadButtonIndexBase`**: Auf `1` setzen, falls die übertragenen Icons um eine Taste versetzt landen.

## OBS-Steuerung

Tasten können OBS direkt steuern (Szene wechseln, Stream/Aufnahme starten/stoppen/umschalten, Quelle stummschalten) — ohne Hotkey-Umweg, über das OBS-WebSocket-Protokoll v5:

1. Voraussetzung: OBS 28 oder neuer. In OBS unter **Werkzeuge → WebSocket-Servereinstellungen** den Server aktivieren (Standard-Port 4455, Passwort optional).
2. In der Host-App oben auf **„OBS"** klicken, Host/Port/Passwort eintragen, „Verbindung testen", speichern. OBS kann auf dem Hauptrechner (`127.0.0.1`) oder dem Zweitrechner (dessen IP) laufen.
3. Bei einer Taste als Aktion **„OBS steuern"** wählen und den Befehl festlegen. Szenen- und Quellennamen werden bei bestehender Verbindung automatisch aus OBS in die Auswahl geladen (Freitext geht auch).

## NVIDIA-Overlay-Aktionen

Der Aktionstyp **„NVIDIA Overlay"** löst Funktionen wie Instant Replay speichern, Aufnahme umschalten, Screenshot oder Mikrofon stumm aus. Die App liest dazu **bei jeder Ausführung** die aktuell in der NVIDIA-App hinterlegte Tastenkombination aus `%LOCALAPPDATA%\NVIDIA Corporation\NVIDIA Overlay\ShareSettings.json` und sendet genau diese — Änderungen an der NVIDIA-Belegung wirken also sofort, ohne Neustart. Voraussetzungen: NVIDIA App mit aktiviertem Overlay; die Aktion wird auf dem Hauptrechner ausgeführt. Ist eine Funktion in der NVIDIA-App nicht belegt, meldet die Statuszeile das entsprechend.

## Konflikt mit Base Camp

Das Pad führt seine Belegung **aus dem eigenen Flash-Speicher** aus — alte Base-Camp-Makros laufen also auch ohne Base Camp weiter. Zusätzlich startet der Windows-Dienst `BaseCampService` die Base-Camp-Prozesse (`BaseCamp.Service.exe`, `MountainDisplayPadWorker.exe`) automatisch neu, wenn man sie nur beendet.

Lösung in der Host-App (Kopfbereich „Base Camp"):
1. **„Base Camp deaktivieren"**: stoppt den Dienst und stellt den Starttyp auf „Deaktiviert" (Admin-Prompt). Der Statuspunkt zeigt Orange bei Konflikt, Grün wenn Ruhe ist. **„Base Camp aktivieren"** macht alles rückgängig.
2. **„Alte Belegung löschen…"** (unten): setzt die im Pad gespeicherte Tastenbelegung auf Werksstandard zurück und löscht die alten Tastenbilder — danach führt das Pad keine Base-Camp-Makros mehr selbst aus.
3. Zusätzlich übernimmt die App beim Verbinden automatisch die Software-Kontrolle über das Pad (SDK `APEnable`; abschaltbar über `AutoApEnable` in der config.json).

Manuell per PowerShell (Admin): `Stop-Service BaseCampService; Set-Service BaseCampService -StartupType Disabled` bzw. zurück mit `Set-Service BaseCampService -StartupType Automatic; Start-Service BaseCampService`.

## Einschränkungen

- Hotkeys landen auf Rechner B in der aktiven Sitzung; Secure Desktop (UAC-Prompt, Sperrbildschirm) ist von Windows aus prinzipbedingt nicht erreichbar.
- Der Agent führt beliebige Befehle aus — Token geheim halten und nur im eigenen LAN betreiben.
