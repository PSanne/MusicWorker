# 🎵 MusicWorker

**MusicWorker** ist eine Windows-Anwendung zum Bearbeiten von Musik, geschrieben in C# mit WPF und .NET 8.

Langfristig soll MusicWorker **Audio-Editor, Mehrspur-Mixer und Noten-Editor** in einem Programm vereinen.
Version 0.3 bietet einen vollständigen Audio-Editor mit Live-Effekten **und ein Mehrspur-Mischpult** mit Fadern, Panorama, Sends, Effekt-Bussen, Gruppen und Projekten – seit 0.3.2 auch mit **Schnitt-Werkzeugen, Takt-Raster, Loop-Bereich und Rückgängig**, seit 0.4 mit **Hüllkurven (Automation) und Auto-Absenkung**, seit 0.5 mit **Noten-Editor, Instrumenten und MIDI**, seit 0.6 mit **Aufnahme, Metronom, Punch-In und Takes/Comping**, seit 0.7 mit **Autotune, Tonhöhen-Werkzeugen, Spektrogramm und Rauschentfernung**, seit 0.8 mit **automatischer Sicherung, Markern, Handbuch und Update-Hinweis**, seit 0.9 mit **Lautheit in LUFS, FLAC/OGG/M4A, Einzelspuren und Ton aus Videos**, seit 0.10 mit **Fade-Griffen, Mehrfachauswahl, Kopieren/Einfügen von Clips, Spurfarben, Spurhöhe und Umsortieren**, seit 0.11 mit **Presets und fünf neuen Effekten**, seit 0.12 mit **Knackser-Entfernung und spektraler Reparatur**, seit 0.13 mit **Tempo- und Tonart-Erkennung, Tempospur, Akkorden, Arpeggiator und Schlagzeug-Raster**, seit 0.14 mit **Notenblatt (Drucken/PDF) und Aufnahme mit geringer Latenz samt Mithören**.

## Notenblatt & Aufnahme (Version 0.14)

| Bereich | Funktionen |
|---|---|
| **Notenblatt** | Im Noten-Editor *Notenblatt …*: alle Notenspuren (ohne Schlagzeug) als Noten – mit Titel, Tempoangabe, Taktart, Tonart-Vorzeichen, Violin- oder Bassschlüssel bzw. Klaviersystem bei großem Tonumfang, Notenwerten mit Punktierungen und Haltebögen, Pausen, Balken bei Achteln/Sechzehnteln, Versetzungszeichen und Hilfslinien · Stimmen einzeln oder zusammen, Tonart wählbar (die erkannte Tonart ist vorgewählt) · **Drucken** und **Als PDF speichern** (A4, ohne zusätzlichen PDF-Drucker) |
| **Geringe Latenz** | Im Mischpult unter „Treiber“: *Standard (MME)*, *WASAPI* (geringe Verzögerung), *WASAPI exklusiv* (sehr gering) oder – wenn ein Audio-Interface installiert ist – *ASIO* · Puffer 5 – 80 ms · der Latenzausgleich für Aufnahmen wird automatisch passend gesetzt (bei ASIO meldet der Treiber seine Verzögerung selbst) · klappt ein Treiber nicht, nimmt MusicWorker automatisch den nächst einfacheren und sagt Bescheid |
| **Mithören** | *Mithören* einschalten, Spur scharfschalten: Mikrofon oder Instrument sind über Kopfhörer zu hören – beim Aufnehmen und schon vorher; Lautstärke über den Fader der scharfgeschalteten Spur · bei ASIO zusätzlich Wahl der Eingänge (einzeln oder als Stereo-Paar) und Knopf für die ASIO-Einstellungen |

## Musik-Assistent (Version 0.13)

| Bereich | Funktionen |
|---|---|
| **Tempo und Tonart erkennen** | Editor: *Tonhöhe & Spektrum → Tempo und Tonart erkennen* zeigt BPM und Tonart (z. B. „a-Moll“) der Auswahl oder der ganzen Datei; die Tonart lässt sich für Autotune übernehmen · Mischpult: im Tempo-Menü *Tempo des gewählten Clips erkennen* (wird auf Wunsch Projekttempo, und der Clip wird so verschoben, dass sein erster Schlag auf einem Takt liegt) und *Tonart des gewählten Clips erkennen* |
| **Tempospur** | Tempowechsel ab einem bestimmten Takt (z. B. ein langsamer werdender Schluss): *Tempowechsel → am Abspielkopf einfügen*, bearbeiten oder löschen. Raster, Einrasten, Metronom und alle Notenclips folgen dem Tempoverlauf; im Zeitlineal steht „♩ 90“ an jedem Wechsel · Tempowechsel werden im Projekt und in MIDI-Dateien gespeichert und beim MIDI-Import übernommen |
| **Akkord einfügen** | Im Noten-Editor *Akkord …*: Grundton, Art (Dur, Moll, 7, maj7, m7, vermindert, übermäßig, sus2, sus4), Oktave, Umkehrung und Länge wählen – der Akkord landet am grünen Cursor, danach rückt der Cursor weiter |
| **Akkordfolge** | *Akkordfolge …*: bekannte Folgen (Pop I–V–vi–IV, 50er, Jazz ii–V–I, 12-Takt-Blues, Moll-Pop, Andalusisch …) in jeder Tonart, auf Wunsch mit Septimen und Bass – die Akkorde werden eng geführt, damit sie weich ineinander übergehen. Eine erkannte Tonart wird vorgeschlagen |
| **Arpeggiator** | Akkorde nacheinander spielen: aufwärts, abwärts, auf und ab, Alberti oder zufällig · in 1/8, 1/16, Triolen oder 1/32 · über bis zu 3 Oktaven · Tonlänge von gezupft bis gebunden |
| **Schlagzeug-Raster** | Rhythmus in einem Raster aus Kästchen anklicken (12 Instrumente × 16 Sechzehntel) oder einen fertigen Rhythmus wählen (Rock, Pop, Disco/House, Hip-Hop, Ballade, Funk, Reggae, Shuffle, Bossa Nova, Walzer) und für mehrere Takte einfügen · ein vorhandener Takt wird zum Weiterbearbeiten geladen |

## Restaurierung (Version 0.12)

Alles im neuen Menü **„Reparieren“** des Editors – wirkt auf die Auswahl (ohne Auswahl auf die ganze Datei) und ist mit Strg+Z rückgängig zu machen.

| Bereich | Funktionen |
|---|---|
| **Knackser und Knistern entfernen** | Für digitalisierte Schallplatten und Kassetten oder Klicks von Schnitten: kurze Störungen werden gefunden und aus dem Klang davor und danach neu berechnet · Empfindlichkeit und größte Länge einstellbar · meldet, wie viele Knackser entfernt wurden |
| **Störgeräusch entfernen** | Husten, Klingeln, Stuhlknarren: Stelle markieren → wird durch den Klang davor und danach ersetzt (oder nur leiser gemacht). Im **Spektrogramm mit Strg+Ziehen** lässt sich zusätzlich ein **Frequenzbereich** markieren – dann wird nur z. B. das Klingeln bei 3 kHz entfernt und alles andere bleibt unverändert |
| **Rauschen entfernen** | Wie bisher (Rauschprofil lernen, dann entfernen) – jetzt ebenfalls im Menü „Reparieren“ |

## Presets & neue Effekte (Version 0.11)

| Bereich | Funktionen |
|---|---|
| **Klang-Presets** | Oben im Effektbereich: **alle Effekte auf einmal** einstellen – fertig dabei sind „Podcast-Stimme“, „Warme Stimme“, „Radio-Moderation“, „Telefon“, „Großer Saal“, „Lo-Fi / Kassette“, „Gitarre mit Chorus“, „Breites Stereo“ und „Alles aus“. Mit **„Aktuelle Einstellung als Preset speichern …“** wird z. B. „Meine Stimme“ zum eigenen Klang-Preset |
| **Presets je Effekt** | Jeder Effekt hat ein Menü **„Presets ▾“** mit fertigen Einstellungen (z. B. Hall „Kirche“, Kompressor „Sprache / Podcast“, Echo „Ping-Pong“) und eigenen Presets (mit ★). Beim Equalizer heißt es „Eigene ▾“ |
| **Noise-Gate** | Macht Pausen leise – Rauschen und Brummen zwischen den Sätzen verschwinden · Schwelle, Absenkung, Haltezeit, Release |
| **Bandsättigung** | Warme, weiche Verzerrung wie bei einem Tonbandgerät · Drive, Wärme (dämpft Höhen), Anteil |
| **Phaser** | Wandernde Klangfarbe – der „schwebende“ Klang der 70er · Tempo, Tiefe, Rückkopplung, Anteil |
| **Tremolo** | Lautstärke schwankt regelmäßig · Tempo, Tiefe, auf Wunsch als **Stereo-Pendel** |
| **Stereo-Breite** | 0 % = Mono, 100 % = unverändert, bis 200 % = doppelt so breit |

Alle neuen Effekte wirken sofort beim Abspielen, werden mit „Übernehmen“ fest eingerechnet und beim Export auf Wunsch berücksichtigt.
Reihenfolge der Live-Kette: Noise-Gate → Filter → EQ → Bandsättigung → Verzerrung → Kompressor → Chorus → Flanger → Phaser → Tremolo → Stereo-Breite → Echo → Hall.

## Mischpult-Komfort (Version 0.10)

| Bereich | Funktionen |
|---|---|
| **Fade-Griffe** | Jeder Audioclip hat oben links und rechts einen kleinen weißen Griff: nach innen ziehen = **ein- bzw. ausblenden** – die Kurve ist im Clip zu sehen und sofort hörbar |
| **Mehrere Clips** | **Strg+Klick** wählt weitere Clips, **Ziehen in eine freie Fläche** zieht einen Auswahlrahmen, **Strg+A** wählt alle · gewählte Clips lassen sich **gemeinsam verschieben** und löschen |
| **Kopieren & Einfügen** | **Strg+C / Strg+X / Strg+V**: Clips kopieren, ausschneiden und an der Abspielposition einfügen – jeweils auf ihre eigene Spur; mehrmals Strg+V reiht sie hintereinander · **Strg+D** wiederholt die Auswahl direkt dahinter · alles auch per **Rechtsklick auf einen Clip** |
| **Spuren ordnen** | Spurnamen links **nach oben oder unten ziehen** · Rechtsklick auf den Spurnamen: **Farbe** (12 Farben), **Höhe** (klein bis sehr groß), nach oben/unten · Regler **„Spurhöhe“** für alle Spuren |

## Lautheit & Export (Version 0.9)

| Bereich | Funktionen |
|---|---|
| **Lautheit messen** | Menü *Effekte → Lautheit messen*: Gesamtlautheit in **LUFS** (nach ITU-R BS.1770 / EBU R128), Lautheitsumfang (LU), lauteste Stelle und **echte Spitze (dBTP)** – mit Erklärung, was die Werte bedeuten |
| **Auf Lautheit normalisieren** | Knopf **„Lautheit …“**: auf -14 LUFS (Spotify, YouTube, Tidal), -16 (Apple Music, Podcast), -9 (lauter Club-Mix), -23 (Rundfunk) oder einen eigenen Wert. Spitzen über der Obergrenze (Standard -1 dBTP) fängt ein vorausschauender Begrenzer sanft ab |
| **Live-Lautheit** | Während der Wiedergabe zeigt die Statusleiste (Editor) bzw. der Master-Kanalzug (Mischpult) die Lautheit der letzten 3 Sekunden |
| **Exportformate** | **WAV** 16 oder 24 Bit · **FLAC** verlustfrei (16/24 Bit, etwa halb so groß wie WAV) · **MP3** · **M4A/AAC** · **OGG Vorbis** – das zuletzt gewählte Format ist beim nächsten Mal vorausgewählt |
| **Mixdown mit Lautheitsziel** | Beim Mixdown-Export lässt sich gleich das Lautheitsziel wählen (z. B. -14 LUFS) |
| **Einzelspuren (Stems)** | *Projekt → Einzelspuren exportieren*: jede Spur als eigene Datei, alle gleich lang ab Projektbeginn – „wie im Mix“ (mit Fader, Panorama, Effekten und Hall-Anteil; zusammen ergeben sie wieder den Mix) oder „roh“ (nur die Clips) |
| **Ton aus Videos** | MP4, MOV, M4V, WMV, AVI (und MKV, sofern Windows es kennt) öffnen oder ins Mischpult ziehen – die Tonspur wird geladen |

## Sicherheit & Hilfe (Version 0.8)

| Bereich | Funktionen |
|---|---|
| **Automatische Sicherung** | Alle zwei Minuten wird ungespeicherte Arbeit (Editor und Mischpult) in einen eigenen Ordner gesichert. Stürzt MusicWorker ab oder fällt der Strom aus, bietet es beim nächsten Start die **Wiederherstellung** an. Ein-/ausschaltbar im Menü *Datei* |
| **Marker** | Taste **M** (oder Knopf „Marker“) setzt einen Marker an der aktuellen Position – im Editor und im Mischpult, auch während der Wiedergabe · **Bild↑ / Bild↓** springt zum vorherigen / nächsten Marker, „Springen ▾“ listet alle · Fähnchen ziehen = verschieben, Doppelklick = umbenennen (z. B. „Refrain“), Rechtsklick = Menü · Marker wandern beim Schneiden mit, sind rückgängig machbar und werden im Projekt bzw. in **WAV-Dateien** gespeichert (auch andere Audioprogramme lesen sie) |
| **Hilfe-Menü** | **Handbuch als PDF** (F1, liegt dem Programm bei – funktioniert auch ohne Internet) · **Tastenkürzel-Übersicht** · Nach Updates suchen · Über MusicWorker |
| **Update-Hinweis** | Einmal am Tag prüft MusicWorker beim Start, ob es eine neue Version gibt, und zeigt dann oben einen Hinweis mit „Setup herunterladen“. Abschaltbar im Menü *Hilfe* |

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
| **Öffnen & Abspielen** | WAV, MP3, FLAC, OGG, AIFF, M4A/AAC, WMA sowie die Tonspur von Videos öffnen (Menü, Strg+O oder Drag & Drop) · **zuletzt geöffnete Dateien** · Wellenform mit Zeitleiste · Abspielen, Pause, Stopp · **Positionsgriff zum Ziehen** (springt auch während der Wiedergabe) · 5 Sekunden vor/zurück |
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
| Mehrere Clips wählen (Mischpult) | Strg+Klick oder Rahmen in einer freien Fläche aufziehen · Strg+A = alle |
| Clips kopieren / ausschneiden / einfügen (Mischpult) | Strg+C / Strg+X / Strg+V (an der Abspielposition) · Strg+D = direkt dahinter wiederholen |
| Ein-/Ausblenden (Mischpult) | weiße Griffe oben links/rechts am Clip ziehen |
| Spuren sortieren, Farbe, Höhe (Mischpult) | Spurnamen ziehen · Rechtsklick auf den Spurnamen |
| Marker setzen | M (Editor und Mischpult) |
| Zum vorherigen / nächsten Marker | Bild↑ / Bild↓ |
| Marker verschieben / umbenennen / löschen | Fähnchen ziehen / Doppelklick / Rechtsklick |
| Frequenzbereich markieren (Spektrogramm) | Strg + Ziehen – für *Reparieren → Störgeräusch entfernen* |
| Handbuch | F1 (Menü *Hilfe*; dort auch alle Tastenkürzel) |
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
├─ docs/Handbuch.pdf             Handbuch (wird mit dem Programm installiert, Taste F1)
├─ installer/                    Setup-Skript (Inno Setup), wird beim Release automatisch gebaut
├─ src/
│  ├─ MusicWorker.Core/          Plattformunabhängiger Kern (ohne UI, voll testbar)
│  │  ├─ Audio/                  AudioBuffer (unveränderlich), Dezibel-Umrechnung, Marker in WAV-Dateien (cue/labl), FLAC (eigener Kodierer/Dekodierer), OGG Vorbis
│  │  ├─ Editing/                AudioEditor, Auswahl, Rückgängig/Wiederholen (Command-Pattern), Marker
│  │  ├─ Persistence/            Automatische Sicherung & Wiederherstellung, Update-Prüfung (Versionsvergleich), eigene Presets
│  │  ├─ Recording/              Takes & Comping (Zusammenstellen mit Überblendung), Metronom, Zuordnung von Aufnahmen zur Zeitleiste
│  │  ├─ Notes/                  Noten (Ticks), Quantisieren, Humanisieren, Transponieren, Stimmaufteilung, MIDI-Dateien lesen/schreiben
│  │  ├─ Synthesis/              Eingebauter Synthesizer (10 Instrumente), SoundFont-Wiedergabe, Noten → Audio
│  │  ├─ Mixing/                 Misch-Engine: Spuren, Panorama, Fader, Gruppen, Sends, Effekt-Busse, Inserts, Hüllkurven, Auto-Absenkung, Master, Mixdown, Einzelspuren
│  │  ├─ Dsp/                    Biquad-Filter, 15-Band-Equalizer, Kompressor, Echo, Hall (Freeverb), FFT/STFT & Spektrumanalyse, Zeitstreckung (WSOLA), Resampler, Tonhöhenerkennung (YIN), Pitch-Shifter, Rauschentfernung, Lautheit (LUFS) & echte Spitzen, Noise-Gate, Tremolo, Phaser, Stereo-Breite, Bandsättigung, Knackser-Entfernung, spektrale Reparatur
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
- [x] **v0.8 – Sicherheit & Hilfe (Sprint 1):** automatische Sicherung mit Wiederherstellung, Marker in Editor und Mischpult, Hilfe-Menü mit Handbuch (PDF) und Tastenkürzeln, Update-Hinweis
- [x] **v0.9 – Lautheit & Export (Sprint 2):** LUFS-Messung und -Normalisierung, FLAC/OGG/M4A, Einzelspuren (Stems), Ton aus Videos
- [x] **v0.10 – Mischpult-Komfort (Sprint 3):** Fade-Griffe am Clip, Mehrfachauswahl, Kopieren/Einfügen von Clips, Spuren sortieren, Farben, Spurhöhe
- [x] **v0.11 – Presets & Effekte (Sprint 4):** eigene und fertige Presets, Noise-Gate, Tremolo, Phaser, Stereo-Verbreiterung, Bandsättigung
- [x] **v0.12 – Restaurierung (Sprint 5):** Knackser- und Klickentfernung, spektrale Reparatur
- [x] **v0.13 – Musik-Assistent (Sprint 6):** Tempo- und Tonart-Erkennung, Tempospur, Akkorde, Arpeggiator, Schlagzeug-Raster
- [x] **v0.14 – Notenblatt & Aufnahme (Sprint 7):** Notenblatt mit Druck/PDF, Aufnahme mit geringer Latenz und Mithören
- [ ] **v0.15 – Stimme & KI (Sprint 8):** De-Esser, Tonhöhe mit Formanterhalt, KI-Gesangstrennung
- [ ] **v1.0 – Feinschliff (Sprint 9):** Handbuch auf den neuen Stand, Gesamttest
- [ ] **Danach – Aussehen:** Gestaltung der Oberfläche (übernimmt der Auftraggeber zum Schluss)

Den Sprint-Plan mit Status gibt es auch als Board in Miro (*MusicWorker – Sprint-Plan*).

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

### v0.8 – Sprint-Plan, Sicherheit & Hilfe
- **Reihenfolge der Sprints:** Zuerst kommt, was jede Sitzung betrifft und Arbeit schützt (Sicherung, Marker, Hilfe),
  dann was jedes fertige Stück braucht (Lautheit, Formate), danach Arbeitsfluss, Effekte und Restaurierung.
  Komponieren (Musik-Assistent, Notenblatt) und Spezialthemen (geringe Latenz braucht ein Audio-Interface zum
  Testen) folgen. Stimme wurde als geringere Priorität gewünscht; die KI-Gesangstrennung ist am aufwendigsten
  und kommt deshalb zuletzt.
- **Automatische Sicherung alle 2 Minuten in einen eigenen Ordner** (`%LOCALAPPDATA%\MusicWorker\Wiederherstellung`),
  nie über die Dateien des Nutzers. Grund: Ein automatisches Überschreiben könnte einen gewollten alten Stand
  zerstören. Jede laufende Sitzung hat einen eigenen Unterordner, damit sich zwei geöffnete MusicWorker-Fenster
  nicht in die Quere kommen. Geschrieben wird nur neues oder geändertes Audio, und die Beschreibung erst zum
  Schluss (erst Hilfsdatei, dann umbenennen) – so ist eine Sicherung nie halb fertig.
- **Nach einem Absturz genügt eine einzige Frage** („Wiederherstellen?“). Wiederhergestellte Arbeit gilt als
  ungespeichert; ein Mischpult-Projekt behält seinen ursprünglichen Speicherort, sodass Strg+S wie gewohnt dorthin
  speichert. Wer „Nein“ wählt, verwirft die Sicherung – sonst würde die Frage bei jedem Start wiederkommen.
- **Marker werden in WAV-Dateien als „cue“-Marker gespeichert** (Namen im üblichen „labl“-Format). Grund: Das ist
  der verbreitete Standard, den auch andere Audioprogramme lesen. MP3 kann keine Marker aufnehmen; im Mischpult
  stehen sie in der Projektdatei.
- **Marker wandern beim Schneiden mit:** Dahinterliegende rücken nach, Marker in einem gelöschten Bereich
  verschwinden, bei einer Tempoänderung wandern sie anteilig mit. Alle Marker-Änderungen sind rückgängig machbar
  und unterbrechen die Wiedergabe nicht – so kann man beim Anhören mit **M** Stellen markieren.
- **Tasten M und Bild↑/Bild↓:** „M“ für Marker ist in vielen Musikprogrammen üblich, Bild↑/↓ erinnert ans
  Kapitel-Springen. Im Noten-Reiter bleibt M ein Ton der Computer-Klaviatur.
- **Handbuch als PDF neben dem Programm, geöffnet mit dem PDF-Programm von Windows.** Grund: Funktioniert ohne
  Internet, lässt sich drucken, und MusicWorker braucht keinen eigenen PDF-Betrachter. Das Handbuch beschreibt
  noch den Stand 0.7; es wird in Sprint 9 einmal komplett auf den neuen Stand gebracht, statt nach jedem Sprint
  teilweise. Die neuen Tasten stehen schon in der Tastenkürzel-Übersicht.
- **Update-Prüfung höchstens einmal am Tag, nur ein Abruf der öffentlichen Release-Liste bei GitHub** – ohne
  persönliche Daten, abschaltbar. MusicWorker installiert nichts selbst, sondern bietet das Setup zum Herunterladen
  an. Grund: Du behältst die Kontrolle, und weil das Setup nicht signiert ist, soll Windows' Sicherheitsabfrage
  bewusst bestätigt werden.

### v0.9 – Lautheit & Export
- **Lautheit nach ITU-R BS.1770-4 / EBU R128** (K-Gewichtung, 400-ms-Blöcke, Gates bei -70 LUFS und -10 LU,
  Lautheitsumfang nach EBU Tech 3342, echte Spitze mit vierfacher Überabtastung). Grund: Genau so messen Spotify,
  YouTube, Apple und der Rundfunk. Zur Kontrolle habe ich die Messung mit dem Referenzwerkzeug ffmpeg verglichen –
  die Werte stimmen auf 0,1 LU überein.
- **Beim Normalisieren auf Lautheit begrenzt ein vorausschauender Begrenzer nur die Spitzen**, statt das Ziel zu
  verfehlen. Grund: Bei leisen, dynamischen Aufnahmen wäre -14 LUFS sonst oft nur mit Übersteuerung erreichbar.
  Wird stärker als 6 dB begrenzt, weist MusicWorker darauf hin, weil es dann gepresst klingen kann.
- **Ziele als Auswahlliste mit Klartext** („Spotify, YouTube (-14 LUFS)“) plus eigener Wert. Grund: Einsteiger
  kennen die Plattform, nicht die Zahl.
- **FLAC mit eigenem Kodierer und Dekodierer** (feste Prädiktoren, Rice-Kodierung, Stereo-Dekorrelation, MD5).
  Grund: Keine zusätzliche Bibliothek nötig, funktioniert auf jedem Windows. Geprüft mit dem offiziellen
  `flac`-Werkzeug (Prüfsummen korrekt, Dateigröße wie dessen Standardstufe); der Dekodierer liest auch Dateien
  anderer Programme bitgenau.
- **OGG Vorbis mit den freien Bibliotheken „.NET Ogg Vorbis Encoder“ und „NVorbis“ (MIT)**. Die Kodier-Bibliothek
  arbeitet nur in Stereo und in einem bestimmten Qualitätsbereich zuverlässig – deshalb wird immer in Stereo mit
  etwa 160 kbit/s gespeichert (sehr gute Qualität; Mono wird auf beide Kanäle verteilt). Außerdem verschluckt sie
  die ersten 1024 Samples; MusicWorker stellt deshalb genau so viel Stille voran, sodass die Datei exakt die
  richtige Länge und Lage hat. Andere Abtastraten als 44,1/48 kHz werden vorher umgerechnet.
- **M4A/AAC über den in Windows eingebauten Kodierer** (192 kbit/s). Grund: Bereits vorhanden, gute Qualität,
  keine Lizenzfragen.
- **Das zuletzt gewählte Exportformat wird gemerkt** – Editor und Mischpult teilen sich die Einstellung.
- **Einzelspuren sind immer gleich lang und beginnen beim Projektanfang.** Grund: So passen sie in jedem anderen
  Programm sofort zusammen. „Wie im Mix“ enthält Fader, Panorama, Effekte und den Hall-/Echo-Anteil der Spur, aber
  keine Master-Effekte – so ergeben alle Dateien zusammen wieder den Mix (per Test abgesichert, auch mit
  Auto-Absenkung). Stummgeschaltete Spuren werden übersprungen, weil sie bewusst nicht zum Stück gehören.
- **Ton aus Videos über Windows Media Foundation.** Grund: MP4/MOV/WMV kann Windows von Haus aus; für MKV gibt es
  einen Hinweis auf die kostenlose Erweiterung, falls Windows das Format nicht kennt.

### v0.10 – Mischpult-Komfort
- **Fade-Griffe als kleine weiße Quadrate oben an den Clip-Ecken**, die Kurve als abgedunkelte Fläche. Grund: So
  funktioniert es in den meisten Musikprogrammen; man sieht sofort, wie lang die Blende ist. Die Blende hat dieselbe
  Form wie die automatische Überblendung (gleiche Leistung), damit beides gleich klingt. Notenclips haben keine
  Griffe, weil ihr Klang am Ende bewusst ausklingt.
- **Klick ins Leere setzt weiterhin die Abspielposition, Ziehen ins Leere zieht einen Auswahlrahmen.** Grund: Die
  vertraute Bedienung bleibt, und der Rahmen braucht keine Zusatztaste.
- **Eingefügte Clips landen auf ihrer ursprünglichen Spur** (fehlt sie, auf der Spur mit derselben Nummer), Noten
  nur auf Notenspuren. Nach dem Einfügen steht die Abspielposition am Ende – so reiht mehrmaliges Strg+V Teile
  lückenlos aneinander (z. B. einen Refrain wiederholen).
- **Spurhöhe pro Spur plus ein Regler für alle.** Grund: Wichtige Spuren (z. B. Gesang) lassen sich groß anzeigen,
  der Rest bleibt kompakt. Höhe und Farbe werden im Projekt gespeichert, zählen aber nicht als „Änderung am Klang“.
- **Spuren werden durch Ziehen am Spurnamen umsortiert** (dazu „Nach oben/unten“ im Rechtsklick-Menü). Das
  Umsortieren ist rückgängig machbar; Auto-Absenkung und Gruppen bleiben an der richtigen Spur.

### v0.11 – Presets & neue Effekte
- **Zwei Arten von Presets: „Klang-Presets“ für alle Effekte auf einmal und Presets je Effekt.** Grund: Ein
  „Podcast-Klang“ besteht aus mehreren Effekten (Gate, EQ, Kompressor) – das soll ein einziger Klick sein. Wer nur
  den Hall wechseln will, nimmt das Menü am Hall. Ein Klang-Preset schaltet erst alle Effekte aus und stellt den
  EQ gerade; so klingt es immer gleich, egal was vorher eingestellt war.
- **Laden eines Effekt-Presets schaltet den Effekt ein.** Grund: Man will das Ergebnis sofort hören.
- **Eigene Presets liegen in `%APPDATA%\MusicWorker\presets.json`** und sind im Menü mit ★ markiert. Grund: Eine
  Datei lässt sich leicht sichern oder auf einen anderen Rechner mitnehmen; die fertigen Presets bleiben davon
  getrennt und können nicht versehentlich überschrieben werden.
- **Das Noise-Gate kommt ganz an den Anfang der Kette**, die Bandsättigung direkt hinter den EQ, Phaser, Tremolo
  und Stereo-Breite nach Chorus/Flanger und vor Echo/Hall. Grund: So arbeiten Studios üblicherweise – das Gate soll
  nur das Rauschen der Aufnahme sehen, nicht den Hall; Hall und Echo kommen zuletzt, damit sie natürlich ausklingen.
- **Stereo-Breite gleicht die Lautstärke aus**, wenn sie breiter macht. Grund: Sonst würde „breiter“ einfach
  „lauter“ bedeuten und man könnte den Klang nicht fair vergleichen.
- **Die Bandsättigung bleibt auch bei viel Drive unter 0 dB.** Leise Stellen werden etwas lauter, Spitzen weich
  abgerundet – das ist der gewünschte „dichtere“ Klang, ohne zu übersteuern.

### v0.12 – Restaurierung
- **Knackser werden über eine lineare Vorhersage gefunden** (das Modell „errät“ jedes Sample aus den vorherigen; wo
  es plötzlich weit daneben liegt, sitzt ein Knackser). Die Schwelle richtet sich nach der typischen Abweichung im
  jeweiligen Abschnitt – so werden leise und laute Passagen gleich behandelt. Grund: Ein fester Pegel-Schwellwert
  würde in lauten Stellen Musik und in leisen Stellen nichts erwischen.
- **Reparierte Stellen werden aus beiden Richtungen vorhergesagt und überblendet.** Grund: Nur von vorne
  vorhergesagt passt das Ende oft nicht zum Folgenden; aus beiden Richtungen entsteht ein nahtloser Übergang.
- **Spektrale Reparatur macht nur leiser, nie lauter.** Grund: Störungen sollen verschwinden, aber es sollen keine
  neuen Klänge entstehen. „Ersetzen“ nimmt den Klang von 0,3 s davor und danach als Vorbild, mit weichen Rändern in
  Zeit und Frequenz, damit keine Kanten hörbar sind.
- **Frequenzbereich per Strg+Ziehen im Spektrogramm.** Grund: Normales Ziehen markiert weiter nur die Zeit wie
  gewohnt; wer gezielt eine Störung „ausschneiden“ will, kann sie im Spektrogramm einrahmen.
- **Neues Menü „Reparieren“**, in das auch die Rauschentfernung umgezogen ist. Grund: Alles, was Aufnahmen
  rettet, ist an einer Stelle zu finden.

### v0.13 – Musik-Assistent
- **Tempo-Erkennung über eine „Einsatzkurve“ und Kammfilter.** MusicWorker misst, wo neue Klänge einsetzen
  (Bass-Bereich doppelt gewichtet, weil Bass-Trommel und Bass den Grundschlag tragen), und prüft für jedes Tempo
  zwischen 60 und 180 BPM, wie gut ein gleichmäßiges Raster auf diese Einsätze passt. Grund: Das ist robust und
  braucht keine Zusatzbibliothek.
- **Doppelt oder halb so schnell wird gezielt entschieden:** Liegt zwischen den Schlägen fast genauso viel wie auf
  ihnen, ist das schnellere Raster der eigentliche Grundschlag. Grund: Die häufigste Fehlerquelle bei der
  Tempo-Erkennung ist die Oktave (60 statt 120 BPM). Weil auch Menschen hier verschieden empfinden, bietet der
  Dialog im Mischpult zusätzlich „halb“ und „doppelt“ an.
- **Tonart nach Krumhansl-Schmuckler:** Die Verteilung der zwölf Tonstufen wird mit typischen Dur- und
  Moll-Profilen verglichen. Grund: bewährtes, nachvollziehbares Verfahren. Moll-Tonarten werden in deutscher
  Schreibweise klein geschrieben („a-Moll“, „Es-Dur“).
- **Tempowechsel gelten ab Taktanfängen** und werden in Ticks (musikalischer Zeit) gespeichert. Grund: Dann bleiben
  Noten, Raster und Metronom immer im Takt; Audioaufnahmen bleiben unverändert an ihrer Stelle (sie werden
  durch einen Tempowechsel nicht gedehnt). Ein Notenclip, der verschoben wird, wird bei Tempowechseln neu
  berechnet, weil seine Noten dann in einem anderen Tempo liegen können.
- **Akkordfolgen werden „eng geführt“:** Für jeden Akkord wird die Umkehrung gewählt, die am nächsten am vorigen
  liegt. Grund: So klingt eine Begleitung wie von einem Pianisten gespielt statt in großen Sprüngen.
- **Arpeggiator und Akkorde verändern echte Noten** (kein Live-Effekt). Grund: Das Ergebnis ist sofort im
  Noten-Editor sichtbar, lässt sich weiter bearbeiten, als MIDI exportieren und mit Strg+Z zurücknehmen.
- **Schlagzeug-Raster als eigenes Fenster** mit 16 Sechzehnteln pro 4/4-Takt (bei anderen Taktarten passend mehr
  oder weniger Spalten). Grund: Ein Klick-Raster ist für Rhythmen viel schneller als einzelne Noten zu zeichnen;
  die Schläge werden als General-MIDI-Schlagzeugnoten eingefügt und sind danach normale Noten.

### v0.14 – Notenblatt & Aufnahme mit geringer Latenz
- **Das Notenblatt wird selbst gezeichnet** (Linien, Notenköpfe, Schlüssel und Vorzeichen als Formen) statt mit einer
  Notensatz-Schriftart. Grund: Es soll auf jedem Windows-PC gleich aussehen, ohne dass eine besondere Schrift
  installiert sein muss. Die Zeichenlogik liegt im Kern und kann WPF (Bildschirm, Drucker) und SVG (Tests,
  Vorschau) bedienen – so ließ sich das Notenbild während der Entwicklung auch ohne Windows prüfen.
- **Bewusst einfache Notation:** Zeiten werden auf Sechzehntel gerundet, gleichzeitig beginnende Noten werden zum
  Akkord, Noten über Taktstriche bekommen Haltebögen, Synkopen werden am Schlag geteilt. Grund: Das Notenblatt
  soll gut lesbar sein; eingespielte Noten sind nie ganz exakt. Triolen und mehrere unabhängige Stimmen in einem
  System werden (noch) nicht dargestellt.
- **Klaviersystem automatisch:** Liegt eine Spur sowohl hoch als auch tief, wird sie auf Violin- und Bassschlüssel
  verteilt (Trennung beim eingestrichenen C). Grund: So sehen Klavierstimmen aus wie in gedruckten Noten.
- **PDF direkt aus MusicWorker** (jede Seite als scharfes Bild mit 300 dpi). Grund: Nicht jeder Rechner hat einen
  PDF-Drucker, und so braucht es keine Zusatzbibliothek. Drucken geht über den normalen Windows-Druckdialog.
- **Tonart des Notenblatts:** Wurde im Projekt eine Tonart erkannt, wird sie genommen, sonst wird sie aus den Noten
  bestimmt (bei Dur/paralleler Moll-Tonart entscheidet der erste Akkord). Sie lässt sich im Fenster ändern.
- **Treiberwahl mit Rückfall:** ASIO → WASAPI → Standard. Grund: Treiber verhalten sich je nach Gerät sehr
  unterschiedlich; MusicWorker soll niemals stumm bleiben, sondern dann eben mit etwas mehr Verzögerung spielen
  und das in der Statuszeile sagen.
- **Mithören über den Fader der scharfgeschalteten Spur.** Grund: Kein zusätzlicher Regler nötig, und man hört sich
  so laut, wie die Spur später auch klingt. Damit man auch ohne laufende Wiedergabe mithören kann, öffnet
  MusicWorker dafür einen eigenen leisen Ausgang, der beim Abspielen automatisch wieder freigegeben wird
  (ASIO und WASAPI exklusiv erlauben nur einen Ausgang).
- **Geräte mit anderer Abtastrate** (z. B. Mikrofon mit 48 kHz in einem 44,1-kHz-Projekt) werden beim Aufnehmen
  hochwertig umgerechnet (gefensterte Sinc-Interpolation). Grund: Im geteilten WASAPI-Modus liefert Windows das
  Format des Geräts, nicht das des Projekts.

## Verwendete Bibliotheken

- [NAudio](https://github.com/naudio/NAudio): Audio-Ein-/Ausgabe und Dateiformate (MIT-Lizenz)
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet): MVVM-Grundbausteine (MIT-Lizenz)
- [MeltySynth](https://github.com/sinshu/meltysynth): SoundFont-Synthesizer in reinem C# (MIT-Lizenz)
- [.NET Ogg Vorbis Encoder](https://github.com/SteveLillis/.NET-Ogg-Vorbis-Encoder): OGG-Vorbis-Kodierer (MIT-Lizenz)
- [NVorbis](https://github.com/NVorbis/NVorbis): OGG-Vorbis-Dekodierer (MIT-Lizenz)
