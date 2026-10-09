# DisplayPad Remote

Steuert per **Mountain DisplayPad** Programme auf dem **Hauptrechner und/oder einem zweiten Windows-Rechner**: Hotkeys senden und Befehle/Skripte ausführen — pro Taste wählbar. Mehrere **Seiten** (je 12 Tasten) lassen sich per Tastendruck am Pad durchschalten. Die Remote-Verbindung ist ausschließlich per HTTPS mit Token und manuell geprüftem SHA-256-Zertifikatfingerabdruck möglich. Basiert auf dem offiziellen [Mountain DisplayPad SDK](https://github.com/Mountain-BC/DisplayPad.SDK.Demo).

```
Rechner A (DisplayPad)                        Rechner B (wird gesteuert)
┌────────────────────────────┐ HTTPS + Token  ┌──────────────────────────┐
│ DisplayPad.Host (WPF-GUI)  │ ─────────────► │ DisplayPad.Agent (Tray)  │
└────────────────────────────┘   Port 5599    └──────────────────────────┘
```

## Bauen

Voraussetzung (nur auf dem Entwicklungsrechner): .NET 8 SDK oder neuer.

**Für einen vollständigen Release-Build:** `build.bat` ausführen. Der Ablauf leert ausschließlich den Ordner `build`, erzeugt self-contained Einzeldateien, signiert sie wie bei FPS Anzeige und meldet am Ende die vollständigen Ausgabepfade. Auf dem Zielrechner muss **kein** .NET installiert sein:

- Host: `build\DisplayPad.Host.exe`
- Agent für den Zweitrechner: `build\DisplayPad.Agent.exe` (Konfiguration, Zertifikat und rotierende Logs entstehen unter `%LocalAppData%\DisplayPadRemote\Agent`)

Für die lokale Entwicklung reicht weiterhin `dotnet build DisplayPadRemote.sln`. Diese Entwicklerausgabe liegt unter `src\...\bin\...` und ist nicht zur Weitergabe gedacht.

## Einrichtung

**Auf Rechner B (Zweitrechner):**
1. `DisplayPad.Agent.exe` starten → der Agent erzeugt beim ersten Start Token und selbstsigniertes HTTPS-Zertifikat.
2. Tray-Icon → **„Verbindungsdaten kopieren"**. Die Zwischenablage enthält Adresse, Token und SHA-256-Fingerabdruck.
3. Eingehende Firewall-Regel nur für das private Netzwerkprofil anlegen:
   `netsh advfirewall firewall add rule name="DisplayPad Agent" dir=in action=allow protocol=TCP localport=5599 profile=private`
4. Optional: Verknüpfung in `shell:startup` legen, damit der Agent mit Windows startet.

**Auf Rechner A (DisplayPad angeschlossen):**
1. Base Camp beenden (kann sonst mit dem SDK um das Gerät konkurrieren).
2. `DisplayPad.Host.exe` starten.
3. Links **„Dienste"** öffnen und die Remote-Einstellungen wählen. IP/Name, Port 5599, Token und den vollständigen SHA-256-Fingerabdruck eintragen. Der Host akzeptiert das Zertifikat nur bei exakter Übereinstimmung.
4. Taste im Raster anklicken → Beschriftung, Icon und Aktion festlegen:
   - **Schriftgröße:** Im Dropdown 8 bis 16 in Einerschritten auswählen. Neue Tasten verwenden Größe 10. Bereits gespeicherte Größen bleiben erhalten und werden auch außerhalb dieses Bereichs angezeigt, bis eine neue Größe gewählt wird.
   - **Hotkey:** ins Eingabefeld klicken und die Kombination drücken (z.B. `Ctrl+Alt+F1`). Medien-Tasten wie `MediaPlayPause`, `VolumeUp` können auch von Hand eingetragen werden.
   - **Multimedia:** Wiedergabe/Pause, Stopp, nächster/vorheriger Titel, Lauter, Leiser oder Stummschaltung ein/aus auswählen. Ausführung auf Hauptrechner, Zweitrechner oder beiden; für neue Multimedia-Belegungen ist Wiedergabe/Pause voreingestellt. Beschriftung, Bild und Ziel bleiben erhalten.
   - **Programm ausführen:** Über „Installierte Programme…“ ein lokal installiertes Programm suchen und auswählen, eine EXE-Datei oder Windows-Verknüpfung (.lnk) über „Durchsuchen“ auswählen oder einen absoluten Pfad eingeben. Programmliste und Dateiauswahl übernehmen automatisch das Windows-Symbol als Tastenbild. EXE-Dateien unterstützen Startparameter und ein optionales Arbeitsverzeichnis; leer bedeutet Programmordner. Bei Verknüpfungen gelten die darin gespeicherten Startparameter und das Arbeitsverzeichnis. Die Auswahl zeigt lokale Programme; der Pfad muss auf dem gewählten Zielrechner vorhanden sein, bei „beiden“ auf beiden Rechnern. Testen ist über „Aktion jetzt testen“ möglich.
   - **Befehl:** wird via `cmd /c` ausgeführt, z.B. `start "" "C:\Program Files\obs-studio\bin\64bit\obs64.exe"`.
   - **Ausführen auf:** Hauptrechner (dieser PC), Zweitrechner oder Beiden, in dieser Reihenfolge. Für neue Belegungen und bisher unbelegte Tasten ist der Hauptrechner voreingestellt; bestehende Aktionen behalten ihr gespeichertes Ziel. Für rein lokale Tasten ist kein Agent nötig.
   - **Seite wechseln:** schaltet zur nächsten/vorherigen/einer bestimmten Seite (mit Wrap-around).
5. **Seiten:** Unter „Tastenbelegung" in der Seitenleiste neben dem Raster auswählen. Darunter lassen sich Seiten anlegen („+"), umbenennen, löschen („–"), kopieren und importieren/exportieren. Das Raster zeigt immer die aktive Seite bzw. den geöffneten Ordner. Wird eine Seitenwechsel-Taste am Pad gedrückt, überträgt die App automatisch die Icons der neuen Seite aufs Pad.
6. „Aktion jetzt testen" führt die Aktion des ausgewählten Eintrags sofort aus.
7. „Speichern" schreibt die Konfiguration, „Auf Gerät übertragen" rendert Icons + Beschriftungen der aktiven Seite und lädt sie auf die Pad-Tasten (alle 12, leere Tasten werden schwarz).

Host-Konfiguration: `%AppData%\DisplayPadRemote\config.json`. Agent-Konfiguration, Zertifikat und Logs: `%LocalAppData%\DisplayPadRemote\Agent`. Agent-Token und OBS-Passwort werden mit Windows DPAPI für den aktuellen Benutzer geschützt gespeichert.

Multimedia verwendet die vorhandenen Medienhotkeys. Profile, Speicherformat und Agent bleiben kompatibel; ein Agent-Update ist dafür nicht erforderlich. Vorhandene reine Medienhotkeys werden beim Laden als Multimedia angezeigt, auch mit abweichender Groß-/Kleinschreibung oder äußeren Leerzeichen. Kombinationen wie `Ctrl+MediaNext` bleiben normale Hotkeys. Der Wechsel zurück zu „Hotkey senden“ erhält den eingetragenen Befehl. Spulen, Shuffle, Wiederholung und die Auswahl eines bestimmten Players sind nicht enthalten.

Programmstarts werden als eigene Aktion `LaunchProgram` mit getrenntem Pfad und Startparametern gespeichert und über Windows gestartet. Dafür Host **und Agent** aktualisieren; ein älterer Agent wird vor dem Programmstart erkannt und mit einem Update-Hinweis gemeldet. Andere Aktionen bleiben nutzbar. Konfigurationsversion 3 liest bestehende Konfigurationen weiterhin, wandelt vorhandene Befehle aber nicht in Programmstarts um. Vor einem Rückwechsel zu älteren Versionen die bisherige Konfiguration sichern; neue Programmbelegungen sind dort nicht lesbar. Beschriftung, Bild, Schriftgestaltung und Ausführungsziel bleiben beim Kategorienwechsel erhalten. Zusätzliche Fenster- oder Administratoroptionen sind nicht enthalten.

Die lokale Programmliste liest Startmenü-Verknüpfungen mit vorhandenem EXE-Ziel und die Windows-Registrierung „App Paths“, ohne Programme zu starten. Sie erfasst damit Desktopprogramme mit diesen Einträgen; andere Programme lassen sich über „Durchsuchen“ wählen. Verknüpfungen bleiben als Verknüpfung gespeichert. Automatisch übernommene Symbole liegen als PNG unter `%AppData%\DisplayPadRemote\program-icons` und verwenden das vorhandene Bildfeld, ohne Konfigurationsmigration oder zusätzliches Agent-Update. Das reine Programm- bzw. eigene Verknüpfungssymbol wird ohne Verknüpfungspfeil übernommen. Für bereits gespeicherte Bilder mit Pfeil das Programm erneut auswählen. Beim Übertragen einer Konfiguration auf einen anderen Host müssen diese Bilddateien wie andere eigene Tastenbilder mitkopiert werden. Manuelles Eingeben eines Pfads oder Laden einer Belegung ersetzt vorhandene Bilder nicht; die Bildauswahl bleibt verfügbar. Kann Windows kein Symbol liefern, wird das bisherige Bild entfernt und ein Hinweis angezeigt.

## Oberfläche

EXE, Fenster und Taskleiste verwenden das Pad-Symbol ohne Statuskreis. Im Infobereich neben der Uhr zeigt dasselbe Motiv einen weißen Rand mit grünem Kreis bei verbundenem DisplayPad und rotem Kreis ohne Verbindung; der Tooltip nennt den Zustand in der gewählten Sprache. Der Kreis zeigt die Geräteverbindung, unabhängig von Bildübertragung, Agent und OBS, und wird auch bei ausgeblendetem Fenster aktualisiert.

Profile und Seiten lassen sich in ihren Listen mit gedrückter linker Maustaste umordnen. Die gelbe Linie zeigt die Einfügeposition; an den Listenrändern wird bei Bedarf gescrollt. Die aktuelle Auswahl und geöffnete Ordner bleiben erhalten. Direkte Seitenwechsel folgen weiterhin derselben Zielseite, „Nächste/Vorherige Seite“ dagegen der neuen Reihenfolge. „Speichern“ sichert die Reihenfolge. Während Übertragungen und im Zuordnungsmodus ist das Umordnen gesperrt.

Der Aufbau orientiert sich an der [offiziellen DisplayPad-Ansicht von MOUNTAIN Base Camp](https://mountain.gg/start/keybindings-displaypad): Navigation links, Seiten daneben, Geräteansicht rechts und Tasteneditor darunter. „Profile" enthält Profilverwaltung und Import/Export; das aktive Profil lässt sich auch oben rechts wechseln. „Dienste" bündelt Remote, OBS und Base Camp. „Einstellungen" enthält Gerätezuordnung, Zurücksetzen der alten Belegung, Autostart, Sprache und Design. Speichern und Übertragen sind in der Fußleiste erreichbar.

Tastenbelegungen lassen sich im Raster mit gedrückter linker Maustaste auf einen anderen Platz ziehen. Aktion, Bild, Beschriftung und Ordnerinhalt wandern gemeinsam; ein belegtes Ziel tauscht mit dem Ausgangsplatz. Ein kurzer Klick wählt weiterhin nur die Taste aus. Loslassen außerhalb des Rasters oder Escape bricht das Ziehen ab. In Ordnern bleibt die zwölfte Taste für „Zurück" reserviert; während der Gerätezuordnung oder einer Übertragung ist das Verschieben gesperrt. Änderungen anschließend wie gewohnt speichern und auf das Gerät übertragen.

Ordner werden über die Ordnertaste am Pad oder „Ordner-Inhalt bearbeiten…“ in der Software geöffnet. Beide zeigen denselben Ordner und Unterordner. Jeder Ordner bietet elf belegbare Tasten und unten rechts die feste, nicht bearbeitbare Zurücktaste. Sie und die Zurück-Schaltfläche über dem Raster führen jeweils eine Ebene nach oben und wählen den verlassenen Ordner aus. Hauptseiten behalten zwölf frei belegbare Tasten.

Mit angeschlossenem Pad wird ein Ordnerwechsel erst nach erfolgreicher Bildübertragung übernommen. Bei Fehlern bleiben Ansicht und Ordnerpfad erhalten. Ohne Pad lässt sich sofort navigieren; beim Wiederverbinden wird die aktuelle Ansicht übertragen, bevor Pad-Aktionen freigegeben werden. Seiten- und Profilwechsel öffnen und übertragen die gewählte Hauptseite. Scheitert diese Übertragung, bleibt die Softwareauswahl bestehen; Pad-Aktionen bleiben bis zur erfolgreichen erneuten Übertragung gesperrt. Während einer Übertragung sind Navigation und Bearbeitung gesperrt.

## Feintuning (config.json)

- **`KeyMatrixMap`**: Zuordnung SDK-KeyMatrix-Code → Tastenindex. Beim Drücken zeigt die Statuszeile den rohen Code an („Taste gedrückt (KeyMatrix-Code X)"). Stimmt die Zuordnung nicht, die 12 Codes in Pad-Reihenfolge (links oben → rechts unten) in das Array eintragen.
- **`UploadButtonIndexBase`**: Auf `1` setzen, falls die übertragenen Icons um eine Taste versetzt landen.

## OBS-Steuerung

Tasten können OBS direkt steuern (Szene wechseln, Stream/Aufnahme starten/stoppen/umschalten, Quelle stummschalten) — ohne Hotkey-Umweg, über das OBS-WebSocket-Protokoll v5:

1. Voraussetzung: OBS 28 oder neuer. In OBS unter **Werkzeuge → WebSocket-Servereinstellungen** den Server aktivieren (Standard-Port 4455, Passwort optional).
2. In der Host-App unter **„Dienste" → OBS-Einstellungen** Host/Port/Passwort eintragen, „Verbindung testen", speichern. OBS kann auf dem Hauptrechner (`127.0.0.1`) oder dem Zweitrechner (dessen IP) laufen.
3. Bei einer Taste als Aktion **„OBS steuern"** wählen und den Befehl festlegen. Szenen- und Quellennamen werden bei bestehender Verbindung automatisch aus OBS in die Auswahl geladen (Freitext geht auch).

## NVIDIA-Overlay-Aktionen

Der Aktionstyp **„NVIDIA Overlay"** löst Funktionen wie Instant Replay speichern, Aufnahme umschalten, Screenshot oder Mikrofon stumm aus. Die App liest dazu **bei jeder Ausführung** die aktuell in der NVIDIA-App hinterlegte Tastenkombination aus `%LOCALAPPDATA%\NVIDIA Corporation\NVIDIA Overlay\ShareSettings.json` und sendet genau diese — Änderungen an der NVIDIA-Belegung wirken also sofort, ohne Neustart. Voraussetzungen: NVIDIA App mit aktiviertem Overlay; die Aktion wird auf dem Hauptrechner ausgeführt. Ist eine Funktion in der NVIDIA-App nicht belegt, meldet die Statuszeile das entsprechend.

## Konflikt mit Base Camp

Das Pad führt seine Belegung **aus dem eigenen Flash-Speicher** aus — alte Base-Camp-Makros laufen also auch ohne Base Camp weiter. Zusätzlich startet der Windows-Dienst `BaseCampService` die Base-Camp-Prozesse (`BaseCamp.Service.exe`, `MountainDisplayPadWorker.exe`) automatisch neu, wenn man sie nur beendet.

Lösung in der Host-App:
1. **„Dienste" → „Base Camp deaktivieren"**: stoppt den Dienst und stellt den Starttyp auf „Deaktiviert" (Admin-Prompt). Der Statuspunkt zeigt Orange bei Konflikt, Grün wenn Ruhe ist. **„Base Camp aktivieren"** macht alles rückgängig.
2. **„Einstellungen" → „Alte Belegung löschen…"**: setzt die im Pad gespeicherte Tastenbelegung auf Werksstandard zurück und löscht die alten Tastenbilder — danach führt das Pad keine Base-Camp-Makros mehr selbst aus.
3. Zusätzlich übernimmt die App beim Verbinden automatisch die Software-Kontrolle über das Pad (SDK `APEnable`; abschaltbar über `AutoApEnable` in der config.json).

Manuell per PowerShell (Admin): `Stop-Service BaseCampService; Set-Service BaseCampService -StartupType Disabled` bzw. zurück mit `Set-Service BaseCampService -StartupType Automatic; Start-Service BaseCampService`.

## Einschränkungen

- Hotkeys landen auf Rechner B in der aktiven Sitzung; Secure Desktop (UAC-Prompt, Sperrbildschirm) ist von Windows aus prinzipbedingt nicht erreichbar.
- Der Agent führt bewusst beliebige Befehle aus — Token geheim halten, Fingerabdruck prüfen und nur im eigenen privaten LAN betreiben.

## Sicherheit, Rotation und Wiederherstellung

- Der Agent akzeptiert remote ausschließlich `Hotkey`, `Command` und `LaunchProgram`, maximal 64 KiB pro Anfrage. Fehlanmeldungen werden pro Quelladresse begrenzt; Tokens, Passwörter, Programmpfade und vollständige Befehle werden nicht protokolliert.
- **Token rotieren:** Agent-Tray → „Token rotieren". Anschließend das kopierte neue Token im Host speichern. Das alte Token ist sofort ungültig.
- **Zertifikatswechsel:** Ändert sich der im Tray kopierte Fingerabdruck unerwartet, nicht einfach übernehmen. Zuerst auf dem Agent-Rechner prüfen, warum `agent.pfx` ersetzt wurde.
- Die Host-Konfiguration wird atomar gespeichert. Beim Überschreiben entsteht `config.json.bak`; eine beschädigte Hauptdatei wird sichtbar aus dieser Sicherung wiederhergestellt. Sind Hauptdatei und Sicherung ungültig, startet der Host mit einer sichtbaren Fehlermeldung.
- Eine beschädigte Agent-Konfiguration führt zu einem sichtbaren Startfehler und erzeugt bewusst kein neues Token.

## Tests und Release

`dotnet test DisplayPadRemote.sln -c Release` führt Shared-, Host- und Agent-Tests aus. Die Windows-CI prüft zusätzlich Release-Build, Lokalisierung, bekannte NuGet-Schwachstellen und beide self-contained Publish-Ausgaben. Die manuellen Release-Gates und Hardwarematrix stehen in [`docs/RELEASE_CHECKLIST.md`](docs/RELEASE_CHECKLIST.md); langfristige Vorhaben in [`docs/BACKLOG.md`](docs/BACKLOG.md).

Die Produktversion steht ausschließlich in `src/Directory.Build.props`. `testing` behält beim Entwickeln die aktuelle Version; Binärmetadaten enthalten zusätzlich den Git-Commit. Für eine Veröffentlichung wird `testing` nach `main` gemergt, die Version einmal auf `main` erhöht und genau dieser geprüfte Commit mit `v<Version>` getaggt. Anschließend wird `testing` per Fast-Forward wieder auf `main` gebracht. Konfigurationsversionen bleiben davon unabhängig und werden nur bei Änderungen des gespeicherten Datenformats erhöht.
