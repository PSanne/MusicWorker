# 🎵 MusicWorker

**MusicWorker** ist eine Windows-Anwendung zum Bearbeiten von Musik, geschrieben in C# mit WPF und .NET 8.

Langfristig soll MusicWorker **Audio-Editor, Mehrspur-Mixer und Noten-Editor** in einem Programm vereinen.
Version 0.3 bietet einen vollständigen Audio-Editor mit Live-Effekten **und ein Mehrspur-Mischpult** mit Fadern, Panorama, Sends, Effekt-Bussen, Gruppen und Projekten – seit 0.3.2 auch mit **Schnitt-Werkzeugen, Takt-Raster, Loop-Bereich und Rückgängig**, seit 0.4 mit **Hüllkurven (Automation) und Auto-Absenkung**, seit 0.5 mit **Noten-Editor, Instrumenten und MIDI**, seit 0.6 mit **Aufnahme, Metronom, Punch-In und Takes/Comping**, seit 0.7 mit **Autotune, Tonhöhen-Werkzeugen, Spektrogramm und Rauschentfernung**.

## Mischpult (Version 0.3)

Die Registerkarte **„Mischpult“** macht aus MusicWorker eine kleine Mehrspur-Arbeitsumgebung:

| Bereich | Funktionen |
|---|---|
| **Spuren** | Audiodateien als Spuren laden (auch per Drag & Drop, mehrere auf einmal) · Editor-Datei als Spur übernehmen · Clips mit der Maus **verschieben – auch auf andere Spuren** · Positionsgriff, Zoom (Strg+Mausrad), waagerecht scrollen (Umschalt+Mausrad) |
| **Schnitt** | **Teilen** an der Abspielposition (Trenner, Strg+T) · **Verbinden** (Clip mit dem nächsten zusammenführen) · **Auto-Zuschnitt** (Stille am Anfang/Ende entfernen) · **Überblendung** automatisch, sobald sich Clips überlappen (im Clip als ✕ sichtbar) · **Aneinanderreihen / Mixtape** (Clips lückenlos oder mit Überblendung hintereinander) · **Zusammenmischen** (hörbare Spuren in eine neue Spur mischen) · **Rückgängig/Wiederholen** (Strg+Z / Strg+Y, 50 Schritte) |
| **Raster & Loop** | **Takt-Raster** mit Tempo (BPM) und Taktart, Taktnummern · **Einrasten** auf die Schläge · **Loop-Bereich** mit Umschalt+Ziehen oder „Loop = Clip“, Loop ein/aus mit Strg+L |
| **Hüllkurven** | Knopf **„Hüllkurven“**: Lautstärke, Panorama, Hall-/Echo-Send und Hochpass über die Zeit steuern · Klick setzt einen Punkt, Ziehen verschiebt, Doppel- oder Rechtsklick löscht · welche Kurve gezeigt wird, wählst du links in der Spurliste · rastet mit „Einrasten“ auf das Raster ein · „Kurve löschen“ setzt auf den Regler zurück |
| **Auto-Absenkung** | Im Kanalzug unter **„Absenken durch“** eine andere Spur wählen (z. B. Sprache): Diese Spur wird automatisch leiser, solange die gewählte Spur spielt · Regler für Stärke (dB) und Schwelle |
| **Aufnahme** | Knopf **● Aufnahme** (Strg+R) nimmt auf die scharfgeschalteten Spuren auf (● im Spurkopf; ohne scharfe Spur entsteht eine neue) · Audiospuren vom gewählten **Eingang** (mit Pegelanzeige), Notenspuren in **Echtzeit** vom MIDI-Keyboard oder der Computertastatur · **Metronom** und **Vorzähler** (1 Takt) · **Punch-In**: nur im Loop-Bereich neu aufnehmen, mit einem Takt Vorlauf · **Loop-Aufnahme**: mit Loop + Punch-In wird jeder Durchgang ein eigener Take · Latenzausgleich im Menü *Wiedergabe* |
| **Takes & Comping** | Aufnahmen über einen vorhandenen Clip werden zu **Takes** · Doppelklick auf den Clip (oder „Takes …“) öffnet das Comping: über einen Take ziehen = diesen Abschnitt verwenden · **Clear-Comp** (gewählter Take überall) · Take löschen · Anhören · Zusammenfügen |
| **Kanalzüge** | **Fader** (-∞ … +6 dB) mit **Pegelanzeige** · **Panorama**-Drehregler · **Mute/Solo** · **Insert-Effekte:** 3-Band-EQ (Tiefen/Mitten/Höhen), Hochpass, Kompressor · **Sends** auf Hall- und Echo-Bus · **Gruppen**-Zuordnung |
| **Effekt-Busse** | **Hall-Bus** (Raumgröße, Dämpfung) und **Echo-Bus** (Zeit L/R, Rückkopplung, Dämpfung, Ping-Pong) mit Rückweg-Fader – ein gemeinsamer Hall für alle Spuren |
| **Gruppen** | Gruppen-Fader und -Mute wirken auf alle zugeordneten Spuren |
| **Master** | Master-Fader mit Stereo-Pegelanzeige · **Master-EQ** (3 Bänder) · **Summen-Kompressor** · **Limiter** gegen Übersteuerung |
| **Mixdown** | Als WAV/MP3 exportieren oder im Editor öffnen (mit Ausklingen der Effekt-Busse; bei Übersteuerung automatisch auf -1 dB) |
| **Projekte** | Speichern/Öffnen als `Name.mwproj` mit Audio-Ordner `Name-Audio` (verlustfreie 32-Bit-WAVs) |
| **Editor-Brücke** | Doppelklick auf einen Clip öffnet ihn im Editor – nach dem Bearbeiten „In Spur übernehmen“ |

## Noten-Editor (Version 0.5)

Die Registerkarte **„Noten“** bearbeitet Notenclips aus dem Mischpult. Notenspuren spielen ihre Noten mit einem Instrument;
im Mischpult verhalten sie sich wie Audiospuren (Fader, Effekte, Hüllkurven, Mixdown).

| Bereich | Funktionen |
|---|---|
| **Notenspuren** | „+ Notenspur“ im Mischpult · Doppelklick auf eine freie Stelle einer Notenspur legt einen neuen Clip an · Doppelklick auf einen Notenclip öffnet ihn · Notenclips lassen sich teilen, verbinden, zuschneiden und verschieben |
| **Piano-Roll** | Klick setzt eine Note (Ziehen = Länge) · Note ziehen = verschieben (Zeit und Tonhöhe) · rechten Rand ziehen = Länge · Doppel-/Rechtsklick löscht · Umschalt+Ziehen = Auswahlrahmen, Strg+Klick ergänzt · Raster 1/4 … 1/32, Triolen, Einrasten · Klaviatur links zum Vorhören |
| **Anschlag** | Anschlag-Feld unter den Noten: Balken ziehen · „Setzen …“, Crescendo, Decrescendo |
| **Werkzeuge** | **Quantisieren** (mit Stärke) · **Humanisieren** · **Transponieren** (±Halbton, ±Oktave, beliebig; auch Pfeil hoch/runter) · **Notenlängen** (×½, ×2, = Raster, Legato) · **Stimmen aufteilen** (auf mehrere Spuren) · Kopieren/Einfügen · eigenes Rückgängig |
| **Instrumente** | 10 eingebaute Klänge ohne Zusatzdateien: Klavier, E-Piano, Orgel, Streicher, Flächenklang, Synth-Bass, Synth-Lead, Gitarre, Glocken, Schlagzeug (General-MIDI-Belegung) · zusätzlich **SoundFonts (.sf2)** laden, z. B. „GeneralUser GS“ oder „FluidR3 GM“ |
| **Spielen** | **Bildschirmtastatur** (3 Oktaven, Maus) · **Computertastatur** als Klaviatur (Y–M und Q–U, Zahlen = schwarze Tasten) · **MIDI-Keyboard** als Eingang · **Schritt-Aufnahme**: gespielte Töne/Akkorde werden am grünen Cursor eingefügt |
| **MIDI** | MIDI-Dateien importieren (Spuren, Tempo, Taktart, Klänge) und alle Notenspuren als MIDI exportieren |

## Tonhöhe & Spektrum (Version 0.7)

Im Editor gibt es dafür das Menü **„Tonhöhe & Spektrum“**. Alle Werkzeuge wirken auf die Auswahl (ohne Auswahl auf die
ganze Datei), laufen im Hintergrund und lassen sich mit Strg+Z zurücknehmen.

| Bereich | Funktionen |
|---|---|
| **Tonhöhe erkennen** | Zeigt die gesungene/gespielte Note der Auswahl, z. B. „A4 +12 ct (443,1 Hz)“ |
| **Tonhöhe korrigieren** | Verschiebt um einen festen Wert in Cent – vorgeschlagen ist genau der Weg zum nächsten Halbton; die Länge bleibt gleich |
| **Autotune** | Tonart und Tonleiter (chromatisch, Dur, Moll, Pentatonik), Geschwindigkeit (0 ms = Roboter-Effekt, 30–100 ms = natürlich) und Stärke |
| **Tonhöhen-Verlauf** | Parametrische Tonhöhe: von einem Wert zum anderen über die Auswahl (gleichmäßig oder als Kurve), z. B. 0 → -12 Halbtöne als „Plattenspieler-Stopp“ |
| **Stimmeffekte** | Roboter, Helium, Riese, Telefon, Megafon, Unterwasser – mit Mischregler |
| **Spektrogramm** | Zuschaltbar unter der Wellenform: Zeit × Frequenz (logarithmisch), Helligkeit = Lautstärke; geht mit Zoom und Scrollen mit; Klick setzt die Position, Ziehen markiert |
| **Rauschentfernung** | 1. Eine Stelle mit nur Rauschen markieren → „Rauschprofil aus Auswahl lernen“ · 2. „Rauschen entfernen …“ mit Absenkung (dB) und Empfindlichkeit |

## Editor (Funktionen)

| Bereich | Funktionen |
|---|---|
| **Öffnen & Abspielen** | WAV, MP3, FLAC, AIFF, M4A/AAC, WMA öffnen (Menü, Strg+O oder Drag & Drop) · **zuletzt geöffnete Dateien** · Wellenform mit Zeitleiste · Abspielen, Pause, Stopp · **Positionsgriff zum Ziehen** (springt auch während der Wiedergabe) · 5 Sekunden vor/zurück |
| **Schneiden & Trimmen** | Bereich mit der Maus markieren · Ausschneiden, Kopieren, Einfügen, Löschen · Auf Auswahl zuschneiden · **Stille einfügen** · **Rückgängig/Wiederholen** (50 Schritte) |
| **Live-Equalizer** | **15 Bänder** von 25 Hz bis 16 kHz (2/3-Oktave, ±12 dB) · Vorverstärkung · 13 Voreinstellungen (Bass-Boost, Rock, Stimme, Loudness …) · **wirkt sofort beim Abspielen** · **Pro-Kanal-EQ:** links und rechts getrennt einstellbar · Ein/Aus für Vorher-/Nachher-Vergleich · „In Audio übernehmen“ · beim Export einrechenbar · Schutz vor Übersteuerung · Einstellung wird gespeichert |
| **Live-Hall** | Freeverb-Hall mit **Drehreglern**: Stärke **pro Kanal** (links/rechts), Raumgröße, Dämpfung · wirkt sofort beim Abspielen · „In Audio übernehmen“ (bei der ganzen Datei mit Ausklingen am Ende) · beim Export einrechenbar |
| **Live-Kompressor** | Schwelle, Verhältnis, Attack, Release, Aufholverstärkung (Drehregler) · Stärke pro Kanal (Parallel-Kompression) · L/R gekoppelt oder getrennt · Anzeige der Pegelreduktion |
| **Filter** | Hochpass und Tiefpass (24 dB/Oktave) – z. B. gegen Brummen oder Rauschen |
| **Verzerrung** | Overdrive (warm), Fuzz (hart), Bitcrusher (Lo-Fi) · Drive, Ton, Stärke pro Kanal |
| **Chorus & Flanger** | Tempo, Tiefe (Flanger: Rückkopplung) · Stärke pro Kanal · L/R versetzt moduliert für mehr Breite |
| **Live-Echo** | Zeit und Stärke pro Kanal (20–2000 ms) · Rückkopplung · Dämpfung (Bandecho-Charakter) · **Ping-Pong** · weiches Gleiten bei Zeitänderungen |
| **Live-Spektrum** | Frequenzspektrum der Wiedergabe *nach* dem EQ in Echtzeit (60 Bänder, 20 Hz–20 kHz) · bei Stereo links/rechts getrennt · darübergelegte EQ-Kurve · jede Spalte steht genau über ihrem EQ-Regler |
| **Stereo-Kanäle** | Wellenform in getrennten Spuren (L blau, R rosa) · Effekte wahlweise nur auf links oder rechts · einzelne Kanäle beim Abhören stummschalten · als zwei Mono-Dateien exportieren (`…_L` / `…_R`) · nur einen Kanal behalten (Stereo → Mono) |
| **Tempo & Tonhöhe** | Time-Stretching 50–200 % ohne Tonhöhenänderung · Pitch-Shifting ±12 Halbtöne ohne Tempoänderung · beides kombinierbar · **pro Kanal verschieden** (wird ein Kanal länger, wird der andere am Ende des Stücks mit Stille aufgefüllt) |
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
| Editor ↔ Mischpult | Registerkarten oben (Leertaste, Esc, Strg+←/→ gelten für die sichtbare Ansicht) |
| Projekt speichern (Mischpult) | Strg+S |
| Clip löschen (Mischpult) | Entf |
| Clip teilen (Mischpult) | Strg+T (an der Abspielposition) |
| Clip auf andere Spur (Mischpult) | Clip nach oben/unten ziehen |
| Loop-Bereich festlegen (Mischpult) | Umschalt + Ziehen in der Spuransicht |
| Rückgängig im Mischpult | Strg+Z / Strg+Y (gilt für die sichtbare Ansicht) |
| Hüllkurven-Punkt setzen / löschen | Knopf „Hüllkurven“ an, dann Klick / Doppel- oder Rechtsklick |
| Noten spielen (Noten-Reiter) | Computertastatur: Y S X D C V G B H N J M (untere Oktave), Q 2 W 3 E R 5 T 6 Z 7 U (obere) |
| Noten: Rückgängig, Auswahl, Kopieren | Strg+Z / Strg+Y, Strg+A, Strg+C / Strg+X / Strg+V, Entf |
| Noten transponieren | Pfeil hoch/runter (mit Umschalt: Oktave) |
| Aufnahme starten/beenden | Strg+R (oder Leertaste/Esc zum Beenden) |
| Position verschieben | roten Griff ▼ in der Zeitleiste ziehen oder in die Zeitleiste klicken |
| 5 Sekunden vor / zurück | Strg+→ / Strg+← |
| Schleife (Loop) ein/aus | Strg+L oder 🔁 in der Werkzeugleiste |
| Wiedergabegerät wählen | Menü *Wiedergabe → Wiedergabegerät* |
| Zum Anfang / Ende | Pos1 / Ende |
| Zoomen / Scrollen | Strg + Mausrad / Mausrad |
| Öffnen / Exportieren | Strg+O / Strg+E |
| Rückgängig / Wiederholen | Strg+Z / Strg+Y |
| Ausschneiden / Kopieren / Einfügen | Strg+X / Strg+C / Strg+V |
| Löschen | Entf |
| Alles auswählen / Zuschneiden | Strg+A / Strg+T |
| Equalizer ein-/ausblenden | Strg+Umschalt+E |
| EQ-Regler auf 0 dB | Doppelklick auf den Regler |
| Drehregler (Hall) | hoch/runter ziehen (mit Umschalt feiner) · Mausrad · Pfeiltasten · Doppelklick = Standardwert |

> **Tipp:** Ist nichts markiert, wirken *Lautstärke* und *Normalisieren* auf die ganze Datei.
> *Abspielen* spielt die Auswahl, oder ab dem Cursor bis zum Ende.
> Der **Live-Equalizer** unter der Wellenform wirkt sofort auf die Wiedergabe. Deine Datei ändert er erst,
> wenn du auf *In Audio übernehmen* klickst; beim Exportieren fragt MusicWorker, ob er eingerechnet werden soll.
> Darüber zeigt das **Live-Spektrum**, was gerade zu hören ist (Stereo: blau = links, rosa = rechts), und in Orange die eingestellte EQ-Kurve.
>
> **Weitere Effekte:** Über dem EQ liegen Filter, Verzerrung, Kompressor, Chorus, Flanger, Echo und Hall
> als gleich hohe Felder mit Drehreglern – alle gleichzeitig sichtbar und sofort hörbar (auf schmalen Fenstern
> rutschen Felder in eine zweite Reihe). Ein grüner Punkt zeigt, welcher Effekt gerade wirkt. Reihenfolge der
> Live-Kette: Filter → EQ → Verzerrung → Kompressor → Chorus → Flanger → Echo → Hall.
>
> **Pro-Kanal-EQ:** Mit „Links/rechts getrennt“ bekommt jeder Kanal seinen eigenen Equalizer. Über *Einstellen [L] [R]*
> wählst du, welchen die Regler gerade zeigen; im Spektrum erscheinen beide Kurven (L blau, R rosa).
>
> **Stereo:** In der Werkzeugleiste „Kanäle“ legst du mit *Bearbeiten [L] [R]* fest, worauf Effekte wirken
> (die Auswahl wird nur in diesen Spuren markiert), und mit *Hören [L] [R]*, welche Kanäle beim Abspielen zu hören sind.
> Schneiden und Einfügen betreffen immer beide Kanäle. *Tempo & Tonhöhe* lässt sich im Dialog
> „Links und rechts getrennt“ einstellen; wird dabei ein Kanal länger, füllt MusicWorker den anderen
> am Ende des Stücks mit Stille auf.

## Loslegen

### Fertige Version herunterladen
Die aktuelle Version gibt es unter **[Releases](https://github.com/PSanne/MusicWorker/releases/latest)**:

- **Setup (empfohlen):** `MusicWorker-Setup-….exe` herunterladen und starten. Installiert ohne Administratorrechte,
  legt einen Startmenü-Eintrag (auf Wunsch auch eine Desktop-Verknüpfung) an und verknüpft Projekte (`.mwproj`),
  sodass sie sich per Doppelklick öffnen. Deinstallieren über Windows-Einstellungen → Apps.
- **Ohne Installation:** ZIP-Datei herunterladen, entpacken, `MusicWorker.exe` starten.

Es muss kein .NET installiert sein. Warnt Windows SmartScreen: „Weitere Informationen“ → „Trotzdem ausführen“.

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
├─ installer/                    Setup-Skript (Inno Setup), wird beim Release automatisch gebaut
├─ src/
│  ├─ MusicWorker.Core/          Plattformunabhängiger Kern (ohne UI, voll testbar)
│  │  ├─ Audio/                  AudioBuffer (unveränderlich), Dezibel-Umrechnung
│  │  ├─ Editing/                AudioEditor, Auswahl, Rückgängig/Wiederholen (Command-Pattern)
│  │  ├─ Recording/              Takes & Comping (Zusammenstellen mit Überblendung), Metronom, Zuordnung von Aufnahmen zur Zeitleiste
│  │  ├─ Notes/                  Noten (Ticks), Quantisieren, Humanisieren, Transponieren, Stimmaufteilung, MIDI-Dateien lesen/schreiben
│  │  ├─ Synthesis/              Eingebauter Synthesizer (10 Instrumente), SoundFont-Wiedergabe, Noten → Audio
│  │  ├─ Mixing/                 Misch-Engine: Spuren, Panorama, Fader, Gruppen, Sends, Effekt-Busse, Inserts, Hüllkurven, Auto-Absenkung, Master, Mixdown
│  │  ├─ Dsp/                    Biquad-Filter, 15-Band-Equalizer, Kompressor, Echo, Hall (Freeverb), FFT/STFT & Spektrumanalyse, Zeitstreckung (WSOLA), Resampler, Tonhöhenerkennung (YIN), Pitch-Shifter, Rauschentfernung
│  │  ├─ Effects/                Lautstärke, Normalisieren, Fades, Equalizer, Tempo/Tonhöhe, Umkehren, Autotune, Tonhöhen-Verlauf, Stimmeffekte
│  │  └─ Visualization/          Wellenform-Spitzen, Spektrogramm
│  └─ MusicWorker.App/           WPF-Anwendung (MVVM)
│     ├─ Audio/                  Laden, Wiedergabe, Export und Live-Equalizer mit NAudio
│     ├─ Controls/               Wellenform, Spuransicht (Timeline), Piano-Roll, Bildschirmtastatur, Live-Spektrum, Drehregler
│     ├─ ViewModels/             MainViewModel, MixerViewModel, Spuren/Gruppen/Busse (CommunityToolkit.Mvvm)
│     ├─ Services/               Dialoge, gespeicherte Einstellungen, Projektdateien
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
- [x] **v0.2 – Komfort & Klang:** 15-Band-Live-Equalizer mit Live-Spektrum, Live-Hall, -Kompressor und -Echo pro Kanal, Stereo-Kanäle getrennt anzeigen/bearbeiten/exportieren, Tempo/Tonhöhe, Stille einfügen, Umkehren, Stummschalten, zuletzt geöffnete Dateien
- [x] **v0.3 – Mehrspur-Mischpult:** mehrere Spuren, Clips verschieben, Fader, Panning, Mute/Solo pro Spur, Sends & Effekt-Busse, Gruppen, Mixdown, Projekte speichern/laden
- [x] **v0.3.1 – Effekte & Komfort:** Chorus, Flanger, Verzerrung (Overdrive, Fuzz, Bitcrusher), Hochpass/Tiefpass-Filter, Wiedergabegerät wählen, Loop-Wiedergabe
- [x] **v0.3.2 – Schnitt im Mischpult:** Trenner hinzufügen / Clip-Trennung, Clip-Zusammenführung, Clip-Mischung, Crossfade zwischen Clips, Auto-Zuschnitt (Stille an Anfang/Ende entfernen), Rasterlinien mit Tempo/Takt und Einrasten, Loop-Bereich, Mixtape (Songs automatisch mit Überblendung aneinanderreihen), Insert- und Master-Effekte, Rückgängig im Mischpult, Clips zwischen Spuren verschieben
- [x] **v0.4 – Automation & Dynamik:** Hüllkurven für Lautstärke, Panorama und Effektparameter (Hall-/Echo-Send, Hochpass); Auto-Absenkung (Musik wird leiser, sobald die Stimme einsetzt)
- [x] **v0.5 – Noten, MIDI & Instrumente:** Piano-Roll, Bildschirmtastatur, Notenlängen-Anpassung, Quantisieren, Humanisierung, Anschlagstärke-Panel, Transponieren, Stimmaufteilung (Noten auf Stimmen verteilen), frei verfügbare SoundFont-Instrumente
- [x] **v0.6 – Aufnahme:** Record, Neuaufnahme (Punch-In), Metronom, Takes mit Comping-Tool (inkl. Clear-Comp)
- [x] **v0.7 – Tonhöhe & Spektrum:** Autotune, Tonhöhen-Korrektur, parametrische Tonhöhe, Stimmeffekte, Spektrogramm (Wasserfall-Ansicht der Fourier-Transformation), spektrale Rauschentfernung
- [ ] **Nächster Schritt – Aussehen:** Gestaltung der Oberfläche (übernimmt der Auftraggeber zum Schluss)

## Entscheidungen

Hier halte ich fest, wie ich bei offenen Fragen entschieden habe – und warum. So bleibt nachvollziehbar,
was bewusst so gebaut ist und was sich später leicht ändern lässt.

### v0.3.2 – Schnitt im Mischpult
- **Rückgängig im Mischpult speichert den ganzen Mischpult-Zustand (max. 50 Schritte).** Das Audio selbst wird
  dabei nicht kopiert, nur Verweise darauf (Audio ist unveränderlich) – deshalb ist das speichersparend und
  zuverlässig, auch für Aktionen wie „Zusammenmischen“, die viele Spuren auf einmal ändern.
- **Überblendungen entstehen automatisch, sobald sich zwei Clips überlappen** (mit gleicher Leistung, also ohne
  Lautstärkeloch). Grund: Das ist die intuitivste Bedienung – Clip überlappend ziehen, fertig. Ein eigener
  Überblend-Dialog wäre nur ein zusätzlicher Schritt.
- **Zusammenmischen schaltet die Quellspuren stumm statt sie zu löschen.** So geht nichts verloren und man kann
  jederzeit vergleichen oder mit Strg+Z zurück.

### v0.4 – Automation & Dynamik
- **Die Lautstärke-Hüllkurve wirkt zusätzlich zum Fader** (0 dB = Fader-Stellung). Grund: Der Fader bleibt
  nutzbar, um die ganze Spur lauter oder leiser zu machen, ohne die Kurve neu zeichnen zu müssen.
- **Panorama-, Send- und Hochpass-Hüllkurven ersetzen den jeweiligen Regler**, solange es Punkte gibt.
  Grund: Ein „zusätzliches“ Panorama ist kaum vorstellbar. Ohne Kurve (oder nach „Kurve löschen“) gilt wieder
  der Regler; die gestrichelte Linie zeigt dann dessen Stellung.
- **Hüllkurven gehören zur Spur, nicht zum Clip** – wie in den gängigen Musikprogrammen. Grund: Clips lassen sich
  teilen, verbinden und austauschen, ohne dass die Automation verloren geht. Folge: Verschiebt man einen Clip,
  bleibt die Kurve an ihrer Stelle.
- **Als „Effektparameter“ lassen sich Hall-Send, Echo-Send und die Hochpass-Frequenz automatisieren.**
  Grund: Das sind die in der Praxis häufigsten Fahrten (Hall nur im Refrain, Filter-Aufblende). Weitere Ziele
  lassen sich im Kern (`AutomationTarget`) leicht ergänzen.
- **Die Kurve liegt direkt über der Spurbahn statt in eigenen Automationsbahnen.** Grund: Die Spuransicht bleibt
  kompakt; welche Kurve sichtbar ist, wählt man pro Spur. Clips werden im Hüllkurven-Modus abgedunkelt, damit
  keine Verwechslung mit dem Verschieben von Clips entsteht.
- **Die Hochpass-Kurve verläuft logarithmisch** (gleiche Abstände = gleiche Tonschritte). Grund: So klingt eine
  Filterfahrt gleichmäßig; linear würde sie unten kaum und oben sehr schnell wirken.
- **Hüllkurven werden alle 64 Samples (≈ 1,5 ms) ausgewertet und dazwischen übergeblendet.** Grund: knackfrei
  und trotzdem sparsam für die Live-Wiedergabe.
- **Auto-Absenkung ist allgemein gehalten („Absenken durch“ eine beliebige Spur)**, nicht nur für Sprache.
  Grund: Dieselbe Technik dient auch für Musik (z. B. Bass weicht der Bassdrum aus). Einschwing- und
  Rückkehrzeit sind fest (40 ms / 600 ms), damit nur zwei verständliche Regler nötig sind: Stärke und Schwelle.
- **Die Absenkung wird aus der Quellspur nach deren Fader berechnet.** Grund: Ist die Quellspur stumm oder
  heruntergezogen, soll sie auch nichts absenken.

### v0.5 – Noten, MIDI & Instrumente
- **Eingebauter Synthesizer statt mitgelieferter Klangdateien.** Grund: SoundFonts sind 30–150 MB groß und haben
  teils unklare Lizenzen. Die zehn eingebauten Klänge (Klavier, E-Piano, Orgel …) funktionieren sofort, ohne
  Download, und klingen auf jedem Rechner gleich. Für realistischere Klänge kann man **zusätzlich eigene SoundFonts
  (.sf2)** laden (über die freie Bibliothek MeltySynth, MIT-Lizenz); fehlt eine SoundFont später, wird automatisch
  das eingebaute Instrument verwendet.
- **Noten werden in musikalischer Zeit gespeichert** (480 Ticks pro Viertelnote, wie in MIDI). Grund: Ändert man
  das Tempo, bleiben die Noten auf ihren Schlägen – nur die Clip-Startpunkte im Mischpult bleiben in Sekunden.
- **Notenclips werden im Hintergrund in Audio umgerechnet** statt live synthetisiert. Grund: So funktionieren
  Fader, Effekte, Hüllkurven, Auto-Absenkung, Zusammenmischen und Mixdown ohne jede Sonderbehandlung, und die
  Wiedergabe ruckelt nie. Nach einer Änderung dauert es etwa 0,2 Sekunden, bis sie zu hören ist; vor Mixdown und
  Export wird immer auf den aktuellen Stand gewartet.
- **Das Instrument gehört zur Spur, nicht zum Clip** – wie in gängigen Musikprogrammen. Notenclips lassen sich
  daher nur auf Notenspuren ziehen, Audioclips nur auf Audiospuren.
- **Notenclips werden beim Überlappen nicht übergeblendet**, und der Ausklang eines Instruments darf über das
  Clip-Ende hinausklingen. Grund: Bei Noten wäre eine Überblendung hörbar falsch (abgeschnittener Nachhall).
- **Piano-Roll statt klassischer Notenschrift.** Grund: Notensatz (Pausen, Balken, Vorzeichen, Bindebögen) ist sehr
  aufwendig und für das Bearbeiten weniger direkt; die Piano-Roll ist der Standard in Musikprogrammen. Eine
  Notenschrift-Ansicht wäre eine mögliche spätere Erweiterung.
- **Computertastatur wie bei „Trackern“:** untere Buchstabenreihe Y–M = eine Oktave, obere Reihe Q–U = die nächste,
  Zahlen = schwarze Tasten (für QWERTZ ausgelegt). Grund: zwei spielbare Oktaven ganz ohne MIDI-Keyboard.
- **Schritt-Aufnahme jetzt, Echtzeit-Aufnahme mit v0.6.** Grund: Schritt für Schritt eingeben braucht kein
  Timing-Gefühl und passt zum Editor; Aufnehmen in Echtzeit gehört zum Thema „Aufnahme“.
- **Stimmaufteilung erzeugt neue Spuren**, die ursprüngliche Spur wird nur stummgeschaltet. Grund: Jede Stimme
  lässt sich so einzeln mischen oder umbesetzen, und nichts geht verloren (Strg+Z).
- **Rückgängig im Noten-Editor ist fein (jede Änderung), im Mischpult zählt eine Notenbearbeitung als ein Schritt.**
  Grund: Beim Komponieren braucht man kleine Schritte, im Mischpult bleibt die Liste übersichtlich.
- **MIDI-Import:** Ein leeres Projekt übernimmt Tempo und Taktart der Datei; General-MIDI-Klänge werden dem
  passenden eingebauten Instrument zugeordnet. Tempowechsel innerhalb der Datei werden (noch) nicht übernommen –
  es gilt das erste Tempo.
- **„Neues Projekt“ setzt Tempo und Taktart auf 120 BPM, 4/4 zurück** (bisher blieben die alten Werte stehen).

### v0.6 – Aufnahme
- **Aufgenommen wird über die Windows-Standardschnittstelle** (16 Bit, in der Abtastrate des Projekts). Grund: Das
  funktioniert mit jedem Mikrofon, Headset und Audio-Interface ohne zusätzliche Treiber. ASIO (für sehr kurze
  Verzögerungen und viele Eingänge gleichzeitig) wäre eine mögliche spätere Erweiterung.
- **Latenzausgleich als einstellbarer Wert** (Standard 60 ms, Menü *Wiedergabe*) statt automatischer Messung.
  Grund: Die tatsächliche Verzögerung von Aus- und Eingang hängt von Gerät und Treiber ab und lässt sich nicht
  zuverlässig auslesen. Der Dialog erklärt, wie man den Wert mit dem Metronom prüft.
- **Kein Mithören des Eingangs über das Programm.** Grund: Über die Standardschnittstelle käme das eigene Signal
  hörbar verzögert zurück – das stört beim Singen und Spielen. Besser direkt am Interface/Headset mithören.
  Die Pegelanzeige zeigt den Eingang, sobald eine Audiospur scharfgeschaltet ist.
- **Aufnahmen werden über die gemerkten Sprünge der Wiedergabe der Zeitleiste zugeordnet.** Grund: So landen sie
  auch mit Vorzähler, nach einem Sprung mit dem roten Griff und bei Loop-Durchgängen exakt an der richtigen Stelle.
- **Eine Audiospur und eine Notenspur können gleichzeitig aufnehmen** (jeweils die erste scharfgeschaltete).
  Grund: Ein Eingangsgerät liefert ein Signal; mehrere Eingänge gleichzeitig wären ein ASIO-Thema.
- **Wer über einen vorhandenen Clip aufnimmt, bekommt Takes** – nichts wird überschrieben. Der neue Take klingt
  dort, wo er aufgenommen wurde; im Comping lässt sich jede Stelle aus jedem Take wählen.
- **Punch-In nutzt den Loop-Bereich** (ein Bereich weniger zu verwalten) und spielt einen Takt Vorlauf. Ist Loop
  zusätzlich eingeschaltet, wird jeder Durchgang ein eigener Take („Loop-Aufnahme“) – ideal, um eine schwierige
  Stelle mehrmals hintereinander einzuspielen.
- **Comping in einem eigenen Fenster mit „Swipe-Comping“** (über einen Take ziehen). Grund: Die Spuransicht bleibt
  übersichtlich, und im Fenster haben alle Takes genug Platz. An den Schnittstellen wird 10 ms übergeblendet –
  kurz genug für präzise Schnitte, lang genug gegen Knackser. **Clear-Comp** setzt die Zusammenstellung auf den
  gewählten Take zurück.
- **Teilen, Verbinden, Zuschneiden und Bearbeiten im Editor fügen die Takes vorher zusammen** (mit Strg+Z
  umkehrbar). Grund: Takes über geschnittene Clips hinweg zu verwalten wäre fehleranfällig und kaum zu durchschauen.
- **Echtzeit-Aufnahme von Noten mischt in einen vorhandenen Notenclip hinein** (Overdub) statt ihn zu ersetzen.
  Grund: Schicht für Schicht einspielen (z. B. erst Bassdrum, dann Hi-Hat) ist der häufigste Fall; Unerwünschtes
  löscht man danach im Noten-Editor.
- **Das Metronom klingt nur bei der Wiedergabe, nie im Mixdown**; der Vorzähler klickt immer (auch ohne Metronom).
- **Strg+R im Editor-Reiter wechselt ins Mischpult** und startet dort die Aufnahme – aufgenommen wird immer ins Projekt.

### v0.7 – Tonhöhe & Spektrum
- **Tonhöhenerkennung nach dem YIN-Verfahren.** Grund: bewährt und robust gerade bei Gesang; zur Beschleunigung wird
  auf etwa 11 kHz heruntergerechnet (Grundtöne 70 – 1000 Hz). Grenze: Erkannt wird eine Stimme bzw. ein einzelnes
  Instrument – bei Akkorden oder einem ganzen Mix gibt es keine eindeutige Tonhöhe.
- **Ein stufenlos steuerbarer Pitch-Shifter** (zwei überblendete Lesezeiger in einer Verzögerungsleitung) für
  Autotune, Korrektur, Verläufe und Stimmeffekte. Grund: Er erlaubt eine sich laufend ändernde Tonhöhe ohne
  Längenänderung und ist schnell. Die Klangfarbe wird mitverschoben: Korrekturen bis etwa ±3 Halbtöne klingen
  natürlich, große Sprünge klingen nach „Micky Maus“ bzw. „Riese“ – bei den Stimmeffekten ist genau das gewollt.
  Formant-erhaltende Verfahren wären eine mögliche spätere Verbesserung.
- **Autotune wird auf die Auswahl angewendet, nicht live beim Abspielen.** Grund: Die Analyse ist so genauer, die
  Wiedergabe bleibt leichtgängig, und das Ergebnis ist mit Strg+Z umkehrbar. Die Einstellungen werden bis zum
  nächsten Aufruf gemerkt.
- **„Parametrische Tonhöhe“ ist als Tonhöhen-Verlauf umgesetzt** (Anfangswert, Endwert, Kurvenform). Grund: Damit
  sind die typischen Anwendungen (Stopp-Effekt, Hineingleiten, Sirene) mit drei verständlichen Einstellungen
  abgedeckt. Eine frei zeichenbare Tonhöhenkurve wäre eine Erweiterung im Stil der Hüllkurven.
- **Die Tonhöhen-Korrektur schlägt den Weg zum nächsten Halbton vor** (in Cent) – man kann ihn übernehmen oder ändern.
- **Spektrogramm als Bereich unter der Wellenform** statt eines reinen Live-Wasserfalls. Grund: Zum Bearbeiten will
  man sehen, *wo* im Stück etwas passiert (Brummen, Zischlaute, Störgeräusche) und es direkt markieren. Deshalb zeigt
  es denselben Ausschnitt wie die Wellenform (Zoom und Scrollen gehen mit), mit logarithmischer Frequenzachse und
  einer gut lesbaren Farbskala („Magma“). Das Live-Spektrum beim Abspielen bleibt zusätzlich erhalten.
- **Rauschentfernung in zwei Schritten: erst Rauschprofil lernen, dann entfernen.** Grund: So weiß das Programm genau,
  was Rauschen ist, statt es zu raten – das ist zuverlässiger und schont das Nutzsignal. Die Absenkung wird über Zeit
  und Frequenz geglättet, damit kein „Zwitschern“ (Musical Noise) entsteht.
- **Stimmeffekte als feste Voreinstellungen mit Mischregler.** Grund: Stimmfunktionen haben für dieses Projekt
  geringere Priorität – deshalb bewusst einfach gehalten, aber sofort brauchbar.

### v0.7.1 – Anordnung der Effekte (auf Wunsch)
- **Die Effektfelder stehen über dem Equalizer, alle gleich hoch, die Regler auf einer Linie.** Grund: So sind alle
  Effekte ohne seitliches Scrollen sichtbar und wirken aufgeräumt. Auf schmalen Fenstern rutschen Felder in eine
  zweite Reihe, statt abgeschnitten zu werden.
- **Der Effektbereich nimmt höchstens 70 % der Höhe ein** (sonst scrollt er senkrecht). Grund: Die Wellenform bleibt
  auch auf kleineren Bildschirmen bedienbar. Ist das Spektrogramm eingeschaltet, teilen sich Wellenform und
  Spektrogramm den übrigen Platz zu gleichen Teilen.

### v0.7.2 – Setup
- **Das Setup wird mit Inno Setup gebaut** (kostenlos, seit vielen Jahren bewährt) – automatisch bei jedem Release,
  neben dem ZIP. Grund: Ein klassisches Setup mit Startmenü-Eintrag und Deinstallation ist für Windows-Nutzer am
  vertrautesten; MSI-Pakete wären aufwendiger und bringen für eine Einzelplatz-Anwendung keinen Vorteil.
- **Installation ohne Administratorrechte** (in den eigenen Benutzerordner); wer möchte, kann im Setup „für alle
  Benutzer“ wählen. Grund: Kein UAC-Dialog nötig, und Updates lassen sich einfach drüberinstallieren.
- **Nur Projekte (`.mwproj`) werden mit MusicWorker verknüpft, keine Audioformate.** Grund: MP3 & Co. sollen weiter
  mit dem gewohnten Player aufgehen; „Öffnen mit → MusicWorker“ funktioniert für Audiodateien trotzdem.
- **Setup und Programm sind nicht digital signiert.** Grund: Ein Code-Signing-Zertifikat kostet jährlich Geld;
  deshalb kann Windows SmartScreen beim ersten Start warnen.

### v0.7.3 – Fehlerbehebung Aufnahme
- **Beim Aufnehmen läuft die Wiedergabe über das Projektende hinaus weiter, bis du *Stopp* drückst.** Vorher endete
  eine Aufnahme in einem leeren Projekt (oder hinter dem letzten Clip) sofort wieder. Grund: Beim Aufnehmen weiß
  MusicWorker vorher nicht, wie lang die Aufnahme wird – beim reinen Abspielen stoppt es weiterhin am Projektende.

## Verwendete Bibliotheken

- [NAudio](https://github.com/naudio/NAudio): Audio-Ein-/Ausgabe und Dateiformate (MIT-Lizenz)
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet): MVVM-Grundbausteine (MIT-Lizenz)
- [MeltySynth](https://github.com/sinshu/meltysynth): SoundFont-Synthesizer in reinem C# (MIT-Lizenz)
