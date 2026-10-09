# DisplayPad-Symbole

Vorlage: vom Nutzer bereitgestelltes transparentes Pad-Motiv mit grünem Statuskreis. Die drei PNG-Dateien wurden mit dem eingebauten ImageGen bearbeitet; die ICO-Dateien sind daraus skalierte Windows-Symbole mit Alpha-Transparenz in 16, 20, 24, 32, 48, 64, 128 und 256 Pixeln. Der transparente Rand wird für die ICO-Konvertierung auf eine Pixelbreite reduziert, das Motiv proportional zentriert.

- `displaypad.png` / `.ico`: EXE, Fenster und Taskleiste, ohne Kreis.
- `tray-connected.png` / `.ico`: Infobereich, grüner Kreis mit weißem Rand.
- `tray-disconnected.png` / `.ico`: Infobereich, roter Kreis mit weißem Rand.

## Verwendete ImageGen-Anweisungen

1. **Ohne Kreis:** Remove the entire round green status badge and its white rim. Restore the black pad casing hidden underneath. Preserve the perspective, side stand, six rounded keys in two rows of three, cyan-white-cyan / white-cyan-white colors and shading. No text, decoration or badge. Preserve genuine transparent background.
2. **Verbunden:** Keep the pad without a badge unchanged. Add the reference's bright green disk with thick white circular rim in the upper-right, overlapping the pad corner with the same size and placement. No text or additional elements. Preserve genuine transparent background.
3. **Getrennt:** Change only the green fill of the upper-right status badge to bright red, approximately #ED3434. Preserve the white rim, pad, buttons, perspective, placement and transparency.

Die PNG-Dateien sind die Bildvorlagen; zur Laufzeit werden ausschließlich die eingebetteten ICO-Dateien geladen. Keine ImageGen-Anfrage findet beim Programmstart statt.
