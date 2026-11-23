# 🎵 MusicWorker

**MusicWorker** ist eine Windows-Anwendung zum Bearbeiten von Musik, geschrieben in C# mit WPF und .NET 8.

Langfristig soll MusicWorker **Audio-Editor, Mehrspur-Mixer und Noten-Editor** in einem Programm vereinen.
Version 0.2 bietet einen vollständigen Audio-Editor mit 15-Band-Equalizer sowie Tempo- und Tonhöhenänderung.

## Funktionen (Version 0.2)

| Bereich | Funktionen |
|---|---|
| **Öffnen & Abspielen** | WAV, MP3, FLAC, AIFF, M4A/AAC, WMA öffnen (Menü, Strg+O oder Drag & Drop) · **zuletzt geöffnete Dateien** · Wellenform mit Zeitleiste · Abspielen, Pause, Stopp · laufende Abspielposition |
| **Schneiden & Trimmen** | Bereich mit der Maus markieren · Ausschneiden, Kopieren, Einfügen, Löschen · Auf Auswahl zuschneiden · **Stille einfügen** · **Rückgängig/Wiederholen** (50 Schritte) |
| **Live-Equalizer** | **15 Bänder** von 25 Hz bis 16 kHz (2/3-Oktave, ±12 dB) · Vorverstärkung · 13 Voreinstellungen (Bass-Boost, Rock, Stimme, Loudness …) · **wirkt sofort beim Abspielen** · Ein/Aus für Vorher-/Nachher-Vergleich · „In Audio übernehmen“ · beim Export einrechenbar · Schutz vor Übersteuerung · Einstellung wird gespeichert |
| **Live-Spektrum** | Frequenzspektrum der Wiedergabe *nach* dem EQ in Echtzeit (60 Bänder, 20 Hz–20 kHz) · bei Stereo links/rechts getrennt · darübergelegte EQ-Kurve · jede Spalte steht genau über ihrem EQ-Regler |
| **Stereo-Kanäle** | Wellenform in getrennten Spuren (L blau, R rosa) · Effekte wahlweise nur auf links oder rechts · einzelne Kanäle beim Abhören stummschalten · als zwei Mono-Dateien exportieren (`…_L` / `…_R`) · nur einen Kanal behalten (Stereo → Mono) |
| **Tempo & Tonhöhe** | Tempo 50–200 % ohne Tonhöhenänderung · Tonhöhe ±12 Halbtöne ohne Tempoänderung · beides kombinierbar |
| **Weitere Effekte** | Lautstärke ändern (±24 dB) · Normalisieren (-1 dB) · Einblenden/Ausblenden (Fade In/Out) · Umkehren · Stummschalten |
| **Export** | WAV (16 Bit) · MP3 (192 kbit/s, über den in Windows eingebauten Encoder) |
| **Ansicht** | Zoomen mit Strg+Mausrad · Scrollen mit dem Mausrad · Ansicht folgt beim Abspielen |

## Bedienung

| Aktion | Maus / Taste |
|---|---|
| Cursor setzen | Klick in die Wellenform |
| Bereich markieren | Klicken und ziehen |
| Auswahl erweitern | Umschalt + Klick |
| Abspielen / Pause | Leertaste |
| Stopp | Esc |
| Zum Anfang / Ende | Pos1 / Ende |
| Zoomen / Scrollen | Strg + Mausrad / Mausrad |
| Öffnen / Exportieren | Strg+O / Strg+E |
| Rückgängig / Wiederholen | Strg+Z / Strg+Y |
| Ausschneiden / Kopieren / Einfügen | Strg+X / Strg+C / Strg+V |
| Löschen | Entf |
| Alles auswählen / Zuschneiden | Strg+A / Strg+T |
| Equalizer ein-/ausblenden | Strg+Umschalt+E |
| EQ-Regler auf 0 dB | Doppelklick auf den Regler |

> **Tipp:** Ist nichts markiert, wirken *Lautstärke* und *Normalisieren* auf die ganze Datei.
> *Abspielen* spielt die Auswahl, oder ab dem Cursor bis zum Ende.
> Der **Live-Equalizer** unter der Wellenform wirkt sofort auf die Wiedergabe. Deine Datei ändert er erst,
> wenn du auf *In Audio übernehmen* klickst; beim Exportieren fragt MusicWorker, ob er eingerechnet werden soll.
> Darüber zeigt das **Live-Spektrum**, was gerade zu hören ist (Stereo: blau = links, rosa = rechts), und in Orange die eingestellte EQ-Kurve.
>
> **Stereo:** In der Werkzeugleiste „Kanäle“ legst du mit *Bearbeiten [L] [R]* fest, worauf Effekte wirken
> (die Auswahl wird nur in diesen Spuren markiert), und mit *Hören [L] [R]*, welche Kanäle beim Abspielen zu hören sind.
> Schneiden, Einfügen und Tempo/Tonhöhe betreffen immer beide Kanäle, weil sie die Länge verändern.

## Loslegen

### Fertige Version herunterladen
Die aktuelle Version gibt es unter **[Releases](https://github.com/PSanne/MusicWorker/releases/latest)**:
ZIP-Datei herunterladen, entpacken, `MusicWorker.exe` starten. Es muss kein .NET installiert sein.

Zwischenstände aus jedem Push findest du außerdem unter
**Actions → „Build & Test“ → Lauf auswählen → Artifacts** (nur angemeldet sichtbar, 90 Tage aufbewahrt).

### Neue Version veröffentlichen
1. Version in `Directory.Build.props` anpassen und committen.
2. Auf GitHub **Actions → „Release“ → „Run workflow“** öffnen, Versionsnummer (z. B. `0.2.0`) eingeben und starten.

GitHub baut und testet dann automatisch, legt die Versionsmarke `v0.2.0` an und veröffentlicht das Release
mit ZIP-Download. Alternativ startet das Release auch, wenn eine Marke wie `v0.2.0` per `git push` hochgeladen wird.

### Selbst bauen
Voraussetzungen: Windows 10/11 und das [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
(oder Visual Studio 2022 mit der Workload „.NET-Desktopentwicklung“).

```powershell
git clone https://github.com/PSanne/MusicWorker.git
cd MusicWorker
dotnet run --project src/MusicWorker.App
```

In Visual Studio: `MusicWorker.sln` öffnen, **MusicWorker.App** als Startprojekt wählen und **F5** drücken.

Tests ausführen:

```powershell
dotnet test
```

## Projektstruktur

```
MusicWorker.sln
├─ src/
│  ├─ MusicWorker.Core/          Plattformunabhängiger Kern (ohne UI, voll testbar)
│  │  ├─ Audio/                  AudioBuffer (unveränderlich), Dezibel-Umrechnung
│  │  ├─ Editing/                AudioEditor, Auswahl, Rückgängig/Wiederholen (Command-Pattern)
│  │  ├─ Dsp/                    Biquad-Filter, 15-Band-Equalizer, FFT & Spektrumanalyse, Zeitstreckung (WSOLA), Resampler
│  │  ├─ Effects/                Lautstärke, Normalisieren, Fades, Equalizer, Tempo/Tonhöhe, Umkehren
│  │  └─ Visualization/          Berechnung der Wellenform-Spitzen
│  └─ MusicWorker.App/           WPF-Anwendung (MVVM)
│     ├─ Audio/                  Laden, Wiedergabe, Export und Live-Equalizer mit NAudio
│     ├─ Controls/               Wellenform (Zoom, Auswahl) und Live-Spektrum mit EQ-Kurve
│     ├─ ViewModels/             MainViewModel (CommunityToolkit.Mvvm)
│     ├─ Services/               Dialoge, gespeicherte Einstellungen
│     └─ Views/                  Equalizer, Tempo & Tonhöhe, Eingabedialoge
└─ tests/
   └─ MusicWorker.Core.Tests/    xUnit-Tests für den Kern
```

**Architektur in Kürze:** Audio wird als unveränderlicher `AudioBuffer` (32-Bit-Float) gehalten.
Jede Bearbeitung ist ein `IEditCommand`, das nur den betroffenen Ausschnitt speichert. Das macht
Rückgängig/Wiederholen speichersparend und robust. Die gesamte Logik steckt in `MusicWorker.Core`,
die Oberfläche nur in `MusicWorker.App`. So können Mehrspur- und Noten-Editor später denselben Kern nutzen.

## Fahrplan

- [x] **v0.1 – Audio-Editor:** Öffnen, Abspielen, Schneiden, Effekte, Export
- [x] **v0.2 – Komfort & Klang:** 15-Band-Live-Equalizer mit Live-Spektrum, Stereo-Kanäle getrennt anzeigen/bearbeiten/exportieren, Tempo/Tonhöhe, Stille einfügen, Umkehren, Stummschalten, zuletzt geöffnete Dateien
- [ ] **v0.3 – Mehrspur-Mixer:** mehrere Spuren, Clips verschieben, Lautstärke/Panorama/Mute/Solo pro Spur, Mixdown, **Projekte speichern/laden**
- [ ] **v0.4 – Noten-/MIDI-Editor:** MIDI importieren/exportieren, Piano-Roll, Noten bearbeiten, Wiedergabe über Software-Synthesizer
- [ ] **v0.5 – Aufnahme:** Mikrofon-/Line-In-Aufnahme direkt in eine Spur

## Verwendete Bibliotheken

- [NAudio](https://github.com/naudio/NAudio): Audio-Ein-/Ausgabe und Dateiformate (MIT-Lizenz)
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet): MVVM-Grundbausteine (MIT-Lizenz)
