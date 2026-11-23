# Lunelle

What [`BinaryVibrance.Lunelle`](../BinaryVibrance.Lunelle) adds over Sirrow: a 16-operator synthesizer chip with
patches, samples and drum kits, music written as code or as files and its sequencer, and measures for designing a
patch like a recording. It plays through [Sirrow](https://github.com/AtomicBlom/Sirrow)'s mixer, and is built from
Sirrow's effects and DSP building blocks, and rendered offline by its `OfflineSession` ([Sirrow's guide](https://github.com/AtomicBlom/Sirrow/blob/master/docs/effects.md)).
Beside it, `BinaryVibrance.LunelleStudio` is a window to write the music in (section 5).

The library depends only on `BinaryVibrance.Sirrow` and the BCL. An app or test project that wants it references it
itself.

---

## 1. Patches: `BinaryVibrance.Lunelle.Chip`

A `Patch` is an instrument as data: up to 16 operators, which of them are heard, and what the voice does after them.
It is immutable. Write one with `Patch.Build`, whose action adds operators to a `PatchBuilder` and routes them:

```csharp
var bell = Patch.Build("bell", p =>
{
	var modulator = p.Sine(3.5f, Env.Decay(0.4f));
	var carrier = p.Sine(1f, Env.Decay(1.5f)).ModulatedBy(modulator, index: 2f);
	p.Out(carrier, p.Sine(5f, Env.Decay(0.3f)).At(0.2f));
	p.Filter(Filter.LowPass(6000f, keyTrack: 0.5f));
	p.Send(ChipSend.Space, 0.3f);
});
```

Each operator method returns an `Op` to route: `ModulatedBy(op, index)`, `Feedback(radians)`, `DrivenBy(ops…)`,
`Detune(cents)`, `Fixed(hz)`, `Phase(cycles)` (where in its cycle a sine or a wave starts each note: several detuned
saws all starting at 0 start as one loud wave, a click, where each given its own start is apart from the first sample),
`KeyLevel(dbPerOctave)` and `KeyDecay(share)` (how it follows the keyboard, from middle C: quieter going up, and
ringing shorter, as struck and plucked things do), and `At(level, pan)` for `Out`.

**Operators heard apart make a patch wide.** `Out` takes each operator at a level and a pan (-1 left to 1 right, the
middle by default): two saws a few cents apart with one each side are a section, where both in the middle are one
player. A patch with any operator off the middle is two channels from its operators on, each through the filter and the
drive, and the voice's own pan (the patch's and the note's) leans the pair as a balance. One with none stays a single
channel placed by its pan, and costs what it did. An operator in the middle is as loud in either.

`Env.Adsr(a, d, s, r)`, `Env.Decay(seconds)` (struck: dying
60 dB over its time whether held or not) and `Env.Gate` are the envelopes; `Filter.LowPass`, `BandPass` and `HighPass`
the filters, with key tracking, velocity and an envelope of their own.

| Operator | What it makes |
|---|---|
| `Sine(ratio, envelope, level, velocity)` | A sine at `ratio` times the note, phase-modulated by any operators before it (FM), with feedback on itself |
| `Wave(waveform, ratio, …)` | A band-limited saw or pulse |
| `Noise(envelope, …, pink)` | White or pink noise |
| `Level(envelope, …)` | Its envelope as a level: a breath's pressure |
| `Sample(samples, …, oneShot)` | A recording from a `SampleSet`, pitched from its zone's root; one-shot plays to its end |
| `String(ratio, decaySeconds, damping, pick)` | A plucked string: a pluck (or its drivers) round a tuned delay line, ringing for `decaySeconds` |
| `Tube(ratio)` | A flute (the Synthesis Toolkit's): a jet driving a bore, blown by its drivers, a `Level` and a quiet `Noise` |
| `Modes(modes…)` | A struck or rung body: its drivers ring up to eight `Mode`s, each at a ratio of the note (`Mode.At`) or a fixed frequency (`Mode.Fixed`) for its own time |

After the operators, in order: the heard operators mixed, the filter, drive (a soft clip, 0 to 1), the gain (times the
velocity squared), pan, and the sends. A patch also sets vibrato (after a delay), a tremolo, a pitch drop at each
note's start (which bends every operator, a `Fixed` one too, so a kick's sine at 55 Hz falls to it), mono with glide,
and macros: eight per instrument, each moving a target (the cutoff, drive, gain, a send,
the vibrato's depth, an operator's level) from one value to another as the game turns it from 0 to 1. The pitch, the
filter's cutoff and the levels are worked out every 64 frames (1.3 ms), so a drum's falling pitch and a filter's sweep
move rather than step.

The builder's calls for these are `p.Filter(…)`, `p.Drive(amount)`, `p.Gain(db)`, `p.Pan(pan)`, `p.Send(send, level)`,
`p.Vibrato(rate, depth, delay)`, `p.Tremolo(rate, depth)`, `p.PitchDrop(semitones, seconds)`, `p.Mono(glideSeconds)` and
`p.Macro(macro, target, from, to, op)`. A patch hears at most `Patch.MaxOutputs` (32) operators through `Out`.

**A filter is swept by octaves** (`MacroTarget.FilterOctaves`): the cutoff moves from so many octaves at the macro's 0
to so many at its 1, which is even to the ear all the way. `FilterCutoff` multiplies the cutoff, so a macro turned
steadily does nearly all it will do in its first few steps: from 100 Hz to 6.4 kHz it is past 3 kHz at halfway.

**The routing rule: an operator is modulated or driven only by one made before it.** So the operators render in
order, each reading the blocks of those before. `Build` checks the rest too, and throws a `PatchException` naming the
patch: at most 16 operators; at least one heard; modulators and feedback on a `Sine` only, drivers on a `String`, a
`Tube` or `Modes` only; at most two strings and one tube a patch; a tube and modes driven; one to eight modes; a sample
with samples. It checks every number as well, so the voice never meets one it can't play: ratios, a filter's cutoff and
Q, a string's and a mode's decay above 0; levels, detune and feedback finite; times and sends 0 or more; velocity,
sustain, damping, drive and a tremolo's depth 0 to 1; a pick 0.02 to 0.5; a pan -1 to 1; a pitch drop over some time.
A `Patch` is made only by `Patch.Build`. A recording is checked where it enters a chip (a `SampleSet` or a `DrumKit`):
samples at a rate, and its loop inside them.

**A pluck is a shape, not noise.** A string with no drivers starts from one period of what a plucked string is: a
triangle with its peak at `pick` (a share of the string's length from the bridge, 0.02 to 0.5; a fifth by default),
which lacks the harmonics with a still point there, mixed toward its slope the harder the note is played (brighter),
with a little noise on top. Every pluck of a note is then as loud as the last; noise alone, over the few samples of a
high note's period, made one loud and the next thin by 7 dB.

**`Modes` is how bars, bells, skins and bodies are made.** Drive it with a knock (a `Sine` at a fixed frequency under
`Env.Decay` of a few milliseconds: higher is a harder mallet; give it a level near 0.1, since a mode rings up to many
times its knock) for a marimba (`Mode.At` 1, 4, 9.2), a free bar (1, 2.76, 5.40, 8.93) or a skin (1, 1.59, 2.14); or
drive it with a `String` and hear both, for the box an instrument is strung on (`Mode.Fixed` at the box's own
resonances, with short times and a tap of a hundredth or so: a mode driven without a pause builds up far more than one
struck once).

The tube is the touchiest. It must be blown at a sustain of 0.8 or more (a `Level` of 1 at an envelope's sustain of
0.8) to speak below E4; it eases its own pressure below G#4 so low notes stay in their register, and it compensates
its loop's filters' delay at each note, so it holds within 20 cents from G3 to C6.

**A patch as data, and as code.** A built `Patch` is read-only data: its `Operators` (`OperatorSpec`, whose
`Modulators` and `Drivers` name earlier operators by index), `Outputs`, `Filter`, the voice's settings and `Macros`.
`PatchSpec.From(patch)` copies it into lists and settings a tool can change, and `spec.ToPatch()` builds it again
through the builder, so every rule above is checked and a broken spec throws the same `PatchException`.
`PatchCode.Write(patch)` writes the `Patch.Build` property that builds it, as a person would have written it: every
operator up to the last that another reads gets a variable (so the builder makes them in the same order), defaults are
left off, `Env.Decay` and `Env.Gate` are recognised, and a macro is named by the function given (`Macros.Bright`) or
`new Macro(n)`. What it writes builds a patch equal to the one it was given. `SynthChip.Replace(instrument, patch)`
swaps an instrument's patch while it plays: notes sounding finish on the old one, and its macros, gain and mute carry
over. `AudioTap`, added to a bus with `AudioEngine.AddEffect`, keeps the latest frames through it for a tool to draw.

## 2. Samples and kits

`SampleData` is a mono recording at its own rate (`SampleData.From(AudioDecoder.Decode(bytes))`). A `SampleZone` says
which notes and velocities it plays for and its root; a `SampleSet` picks among zones by note and velocity, and among
alternate takes of the same range in turn.

**A recording with a loop sustains.** It plays to the loop's end and then round it for as long as the note is held, so
a short recording of a bowed note, an organ pipe or a pad lasts as long as it is asked to; the operator's envelope
ends it once the note is let go (a one-shot plays through the loop to its end). `recording.Looped(loopStart, loopEnd,
crossfadeFrames)` makes the loop, with a crossfade written into its last frames so its end arrives where its start
sets off from: no click. `recording.LoopedAt(hz, startSeconds, lengthSeconds)` finds the loop a steady note wants from
its pitch: the whole number of its periods nearest the length asked for, with a 20 ms crossfade. Choose a loop a whole
number of periods long: across one that isn't, the crossfade's two halves partly cancel and the level dips for its
length. Both copy the samples and leave the recording as it was.

A `DrumKit` is pieces, each sampled in velocity layers of alternate takes, or synthesised from a patch played at note
60 (`DrumKit.FromPatches`). `Kit` names the common pieces (`Kick`, `Snare`, `HatClosed`, `Crash` and the rest) so a
song written for one kit plays on another; any other name is allowed. A hit picks the first layer up to its velocity
and the next take in turn. A piece in a choke group above 0 silences the group's other pieces over 30 ms when hit: a
closed hi-hat the open one.

`DrumKit.Load(read, "kits/rock/kit.json")` reads a manifest and every file it names through `read`, relative to the
manifest's folder, so a kit loads from disk, embedded resources or a package alike. `tools/kit-cut.cs` writes these:

```json
{
  "name": "rock",
  "source": "Big Rusty Drums, Karoryfer Samples, CC0-1.0",
  "pieces": [
    { "piece": "kick", "gainDb": 0, "pan": 0, "choke": 0,
      "layers": [ { "upTo": 0.35, "takes": ["kick_1a.ogg", "kick_1b.ogg"] },
                  { "upTo": 1.0,  "takes": ["kick_2a.ogg", "kick_2b.ogg"] } ] }
  ]
}
```

A missing file, a layer with no takes, or layers out of rising `upTo` order throw `InvalidDataException` naming the
manifest and the piece.
## 3. The chip: `SynthChip`

A `SynthChip` is an `AudioSource` that plays instruments on a pool of voices:

```csharp
var chip = new SynthChip(new ChipOptions
{
	Voices = 32,
	Groups = [new("main"), new("guitars", () => new Distortion(cabinet: Cabinet.FourByTwelve))],
});
var bell = chip.AddInstrument(bellPatch);
var guitar = chip.AddInstrument(guitarPatch, group: 1);
var drums = chip.AddKit(kit);
Audio.Play(chip, music);

var note = chip.NoteOn(bell, 72, velocity: 0.7f);
chip.NoteOff(note);
chip.Play(guitar, 52, 0.8f, seconds: 0.5f);
chip.Hit(drums, Kit.Snare, 0.9f);
chip.SetMacro(guitar, new Macro(1), 0.6f);
```

- **Groups** each run a chain of effects over their instruments, then into the chip's master. **The sends** are
  shared by every patch: `ChipSend.Space` (a reverb by default), `ChipSend.Echo` (a delay, all wet), and
  `ChipSend.Key`, which is never heard: it is what keyed effects listen to (Sirrow's [`Compressor`](https://github.com/AtomicBlom/Sirrow/blob/master/docs/effects.md#compressor)). The master's own
  effects run last. Effects are factories, since each chip makes its own instances.
- **Stealing.** Past its voices a note takes the voice released longest, or else the oldest, fading it out over 128
  samples first. `Stats.StolenVoices` counts them. A note let go (`NoteOff`, `AllNotesOff`) while it waits for its
  stolen voice to fade never starts. When every voice is already fading out for another note, a new one is lost, and
  `Stats.DroppedNotes` counts it: raise `Voices` if that climbs.
- **Notes played for a time** (`Play(…, seconds)`, or a song's notes) are let go at their frame, not at the start of the
  block they end in, so a note shorter than a block still sounds.
- **Mono** patches have one voice: a new note moves it, gliding, with its envelopes left alone.
- Each call checks its arguments on the game thread, then queues an event the chip takes at its next block, so a note
  is heard within a block. Nothing allocates once the chip plays. A note is a MIDI number and may be fractional;
  velocity is 0 to 1, and the level goes as its square. `Hit` on a piece the kit hasn't throws, as a `Beat` naming one
  does. `AllNotesOff` lets go of everything.
- **Limits** (`ChipOptions`): `Voices` (32), `MaxInstruments` (64), and `EventCapacity` (1024) events waiting for the
  next block, past which a call throws rather than drop one.

**The cost**, measured on a Snapdragon X laptop in Release, ten seconds rendered:

| Load | Times real time |
|---|---|
| 32 voices of 16 plain sines, all heard | 5.8 |
| 16 voices of a 4-operator FM patch with feedback and a filter | 17 to 21 |
| An empty chip (its reverb and echo only) | 137 |

Feedback and the filter are each about a fifth of a voice's cost: both are recursions, each sample waiting on the one
before.

## 4. Music: `BinaryVibrance.Lunelle.Music`

Music written as code, played on a chip by a sequencer on the audio clock. A song is a method that builds new data each
time it is called, so `dotnet watch` can rebuild it and `MusicPlayer.Swap` play the new one without a break:

```csharp
static class Groove
{
	// What the game tells the song, 0 to 1, and a macro its guitar patch turns: made once, since a cue is known by its object
	public static readonly Cue Energy = new("energy");
	public static readonly Macro Drive = new(1);

	public static Song Build(DrumKit kit, Patch bass, Patch guitar)
	{
		var song = new Song("groove") { Tempo = 128, Scale = Scale.Minor(Note.E) };
		var guitars = song.Group("guitars", Fx.Distortion(24, cabinet: Cabinet.FourByTwelve));
		var drums = song.Track("drums", kit).Humanise(4, 0.1f);
		var low = song.Track("bass", bass).Octave(2);
		var rhythm = song.Track("rhythm", guitar, guitars).Octave(3);
		rhythm.Macro(Drive).Follow(Energy, 0.2f, 1f);

		var verse = song.Section("verse", 4, "i | VI | III | VII");
		verse.Play(drums, Beat.Eighths((Kit.Kick, "x... x.x."), (Kit.Snare, "..x. ..x."), (Kit.HatClosed, "xxxx xxxx")))
			.EndWith(Beat.Sixteenths((Kit.TomHigh, "xxxx .... .... ...."), (Kit.TomLow, ".... xxxx .... ...."), (Kit.Snare, ".... .... ..xX xxXX")));
		verse.Play(low, Line.Eighths("1 1 . 1 5, 1 . 1").OverChords());
		verse.Play(rhythm, Strum.Eighths("D.DU .UDU").Voicing(Voicing.Power)).When(Energy.Above(0.4f));

		var chorus = song.Section("chorus", 4, "VI VII | i | VI VII | i");
		chorus.Play(drums, Beat.Eighths((Kit.Kick, "x.x. x.x."), (Kit.Snare, "..X. ..X."), (Kit.Crash, "x... ....")));
		chorus.Play(low, Line.Eighths("1 - 1 - 1 1 5, 1").OverChords());
		verse.Exit(verse, Pass.Below(2));
		verse.Then(chorus);
		chorus.Then(verse);
		return song;
	}
}
```

The game passes the song its instruments (here as arguments, so the sample stands alone; a game's songs name its own
patches and kits directly) and sets the cue as it plays: `music.Set(Groove.Energy, 0.7f)`.

### Scales and chords

A `Scale` is a root and its steps: `Major`, `Minor`, `Dorian`, `Phrygian`, `Lydian`, `Mixolydian`, `HarmonicMinor`,
`PentatonicMajor`, `PentatonicMinor`, `Blues`, or `Scale.Of(root, name, steps…)`. Music is written in its degrees:
`Degree(1)` is the root, 8 the octave above, 0 the step below. A player plays a song in its own scale or another
(`Play(song, Scale.Major(Note.G))`), which moves every note and no drum hit.

A `Chord` is a Roman numeral, its case meaning nothing (the scale decides major or minor): `i`, `VI5`, `bVII`, `V7`,
`ivsus4`. Flats and sharps go first; the suffixes are `M`, `m`, `5`, `sus2`, `sus4`, `7`, `dim`. With none, the scale's
own triad on that degree, diminished included. A section's chords are bars separated by `|`, each one chord, or two or
four sharing the bar: `"i | VI | III VII | i"`.

### The notations

A mistake in any of them throws a `NotationException` when the part is placed, quoting the text and saying where:
`Line "1 2 3 4 5 6 7": bar 1 has 7 steps; a bar of 4/4 in eighths has 8 (column 13)`. Every bar must be whole.

**`Line`**: notes by degree, a step a token (`Quarters`, `Eighths`, `Sixteenths`, `Triplets`).

| Token | Meaning |
|---|---|
| `1` to `9` | That degree; 8 is the octave above 1 |
| `'` or `,` after it | Up or down an octave, each |
| `b` or `#` before it | Down or up a semitone, each |
| `^` first | An accent: velocity 1, where a note has 0.8 |
| `~` last | A slide: the note runs its full length into the next (a mono patch glides) |
| `/` between notes | A chord: `1/3/5` starts the notes together, and they share their holds |
| `-` | Hold the note (or chord) before a step more |
| `.` | Rest |
| `\|` | The end of a bar |

A note that doesn't slide sounds 90% of its length, so repeated notes are heard as several. `OverChords()` makes degree 1
the root of each bar's chord (a bass line that follows the chords); `Octave(n)` moves the whole line.

**`Beat`**: drums on a grid, a row a piece (`Eighths`, `Sixteenths`, `Triplets`): `x` a hit (0.75), `X` an accent (1),
`o` a ghost note (0.35), a digit `1` to `9` a hit at that many tenths (a crescendo a step at a time: a snare roll
`3333 4444 5555 6666 | 7777 8888 9999 XXXX`), `.` a rest; spaces and `|` are for reading. Every row is as long.

**`Strum`**: chords across a guitar's strings, a step a character (`Quarters`, `Eighths`, `Sixteenths`), each chord the
section's at that step.

| Character | Meaning |
|---|---|
| `D` | Down, low string to high (0.8) |
| `U` | Up, the top four strings, high to low (0.7) |
| `d`, `u` | The same, softer (0.5) |
| `x` | A muted chuck: every string for 30 ticks |
| `.` | Let ring |
| `_` | Stop the strings |

A strum rings until the next character that isn't `.`. `Voicing(Open)` lays the chord as a guitar plays it in the first
frets (`Power` and `Triad` too); `Voicing(Close)` stacks its notes up from the root in the track's octave, as a hand
plays it on keys, which with `Spread(0)` sounds as block chords; `Spread(ms)` sets the time between strings (12 ms).

### Sections, tracks and placements

A song sets its `Tempo` (120), `Meter` (4/4; set it before placing any part, since parts are checked against it), `Scale`,
`Voices` (32, its chip's), and the effects of its chip's sends and master (`SpaceEffects`, `EchoEffects`,
`MasterEffects`, as factories). It holds at most 64 tracks. Once a player has compiled it (`Play`, `Swap`) a song is
fixed: changing it, its tracks, sections, macros or placements throws, since what plays was made from it. Build a new
one to change it, as a rebuild does.

A track is a patch or a kit, with its octave (degree 1's, 4 unless set), level, pan, `Humanise(timingMs, velocity)` (each
note late by up to twice `timingMs`, `timingMs` on average, never early, the same every time), `Swing(ratio)` (0.667 a
triplet feel: each off-beat step of the grid moves late before anything is spread off it, so a strum's strings move
together) and macros, each following a cue or held. Out-of-range values for these throw rather than clamp. Tracks share effects through groups (`Fx` makes the effects). A section is some bars long over
its chords; `section.Play(track, part)` places a part in it, repeated through the section if shorter, cut at its end if
longer. A placement can be `Named`, play only `When` a condition holds, follow a cue's `Level`, and `EndWith` a fill that
takes the section's last bar each pass.

**A section can sweep a macro across itself:** `section.Sweep(track, macro, from, to)` moves one of the track's patch
macros from `from` at the section's first bar to `to` at its end, over whatever the track holds it at or has it follow.
It is how a build opens or thins a filter towards a drop. In every other section the macro is the track's own again (0
if the track never set it).

**Each section says where the song goes as it ends**, by its exits: `verse.Exit(chorus, Energy.Above(0.7f))` goes to the
chorus when the energy is up, `verse.Exit(verse, Pass.Below(2))` plays the verse once more, and `verse.Then(chorus)`, the
last, goes there otherwise. They are tried in order as the section ends, by the sequencer itself on the audio thread,
their conditions latched as a part's are; a section with none plays again. `Pass` is which pass of the section in a row
this is, 1 the first time, compared exactly (`Pass.Above`, `Below`, `Is`), so its `>` and `<` are strict where a cue's
hold at the level; a count below 1 is refused. A part can use it too, a fill on the fourth time round. `MusicPlayer.NextSection` and `NextExit` say where the section would go were it ending now, for a tool to
show. The song starts at its first section. A `Jump` lets go of what was sounding.

### Cues and conditions

A `Cue` is a level the game sets (`music.Set(Groove.Energy, 0.7f)`); a `Cue<T>` a choice of an enum's values
(`music.Set(Moods.Now, Mood.Calm)`). A cue is known by its object, so a game keeps its cues in static fields
and every song uses those; one a song doesn't use is remembered for the next. Conditions are `cue.Above(x)`,
`cue.Below(x)`, `choice.Is(value)`, combined with `&`, `|` and `!`. **`Above(x)` turns true at x and false again only
below x − 0.05** (`Below` mirrors it), so a part doesn't flap while a cue hovers.

**`OnBar()` against `OnPhrase()`.** An on-bar placement (the default) comes and goes at any bar line as its condition
changes. An on-phrase one is asked only where its part starts over, and once in plays that pass out: a four-bar melody
never starts halfway.

### The player

`MusicPlayer` plays one song at a time on a bus, a new one crossfading over the last; `Update()` once a frame.
**Starting a song without a stall:** `Prepare(song)` compiles it and makes its chip, needs no engine and may run on any
thread; `Play(prepared)` then starts it on the engine's. A game builds and prepares a level's song on a task as the level
loads, and plays it when it is ready. `Position`
says the section, bar and beat; `Jump`, `LoopSection`, `Mute`, `Solo`, `SetTrackGain` and `Force` (a placement held on or
off) steer it; `IsActive` and `TrackPeak` read it, for a workbench. **`Swap(song)`** takes the song rebuilt at the next
bar line, in the scale playing, carrying on in the section of the same name at the same bar (or the first section if that
name is gone), at the new tempo; the absolute bar keeps counting. It plays on a chip of its own, made from the rebuilt
song, so an edit to its groups, their effects or its voices is heard at once; the old chip lets go of its notes and rings
out under the new one for two seconds, fading over the last quarter.

The sequencer splits each block at every event, bar line and section end and hands the chip its notes between slices, so
timing is exact to the frame. It allocates nothing once playing. **Cost:** a song of eight tracks (a kit, a bass and six
four-operator parts in eighths) renders at 31× real time on a Snapdragon X laptop.

**Rendered ahead.** `new MusicPlayer(audio, bus, aheadSeconds: 0.1)` wraps each song's source in an `AheadSource`
([audio.md](audio.md), "Ahead of the speaker"), so the engine's worker renders it a tenth of a second ahead and the audio
thread only copies it: the same samples as live (`MusicPlayerTests`). The cost is latency. A cue or a command is heard
up to the lead later, so a part comes in at the bar line after the cue and the lead, and `Position`, `IsActive` and
`TrackPeak` run up to the lead ahead of the ear. A game wants it. A workbench passes 0 (the default), so what it
shows matches what is heard. Measured in a game playing eight-track songs over its own sound effects, it took the music
off the audio thread's deadline: the thread's load fell from 2.4% to 1.0% of it, its peak from 7% to 2%.

---

## 5. The workbench: `BinaryVibrance.LunelleStudio`

A window for writing music, kept apart from `BinaryVibrance.Lunelle` because it is UI (Vysara) and the synth is not. A song
stays in the game's own project, as files the workbench edits (section 6) and C# for what they can't say. The
workbench plays it, shows it, lets you steer it, and plays it again each time a file is saved.

Start it with `lunelle-studio`, the app that opens a music project and compiles it again in-process with Roslyn on
every save (below, "Opening a project"). From a checkout of LunelleStudio, with Lunelle's beside it:

```
dotnet run --project BinaryVibrance.LunelleStudio.App -- ../Lunelle/samples/HelloSynth
dotnet run --project BinaryVibrance.LunelleStudio.App -- new ../MyTunes        a project of your own, from HelloSynth, opened
```

[`samples/HelloSynth`](../samples/HelloSynth) is a music project to start from: a demo song, four patches, a kit, two
cues and a macro. An app can also host the workbench itself over a catalog of its own ("Hosting the workbench in an
app of your own", at the end of this section), though `lunelle-studio` is the way a game's music is written.

What it shows, one job at a time: a rail down the left gives the middle of the window to **Arrange**, **Patch**,
**Kit** or **Files**, and the keyboard docked along the bottom plays what that mode has chosen.
- **The transport.** The song chosen, play and stop, the position (section, bar, beat), the tempo and the scale (a
  click shows the song in the inspector), the loop on the section playing, and the host's own controls. At its right, the last build's status as a pill: red when
  the song didn't build, and a click opens its error over the view (the last song that built plays on).
- **The library** (the rail's first button): the catalog's songs, patches and kits as lists under their kinds, each row
  its kind's icon, filtered by the words typed; **+** on Songs and Patches makes a new file beside the project's others
  (a patch blank or a copy of the one open), listed once the project builds again (the generator lists every file's
  patch and song, `Patches.Files` and `SongFiles.All`), and then opened. It opens over the view and folds away once something is chosen;
  **Pin** keeps it open as a column. A patch or kit dragged out of it onto the board adds a track that plays it, named
  for it.
- **Arrange**, a board. The song's name above it shows the song in the inspector. Across the top each section's head: its name (chosen, it is shown in the inspector), a button
  that plays it from the next bar, a ruler of its bars with each bar's chords (a click opens a picker: degree, flat or
  sharp, kind, the bar split into two or four, a bar added or taken out of the progression), the bars that repeat the
  progression dimmed, the bar playing lit and a playhead across the section, and its exits with where it goes next
  marked (`MusicPlayer.NextExit`). Down the left each track's head: a stripe in its hue, mute, solo, gain and meter, and
  a click shows it in the inspector, a menu (right-click) renames it, gives it another instrument, opens that instrument or
  deletes it. A line with a **+** shows along a head's top or bottom edge as the pointer nears it, and adds a track
  there playing the patch or kit chosen from a list; **+ Add track** over the board adds the first. Both heads stay
  put over lanes that scroll both ways. Where a lane crosses a section, each part the track plays there: its title (a
  click opens it in the roll and the inspector), its condition on a chip (a click edits it), auto, on and off, and under
  them every pass of its part across the section in the track's hue, the pass playing lit, those to come dimmed, the
  fill marked, all faded while the condition doesn't hold (`MusicPlayer.SectionPatternOf`, `PositionOf`). A **+** in an
  empty cell while the pointer is over it, or a line with a **+** where the pointer nears the gap above, between or under
  the parts already there, adds one just there: a beat on a kit's track, otherwise a melody, a bass
  line over the chords (`overChords`, an octave down), block chords (a strum in the close voicing with no spread) or a
  strum. **The inspector** beside the board changes the song,
  section, track or part chosen: the song's tempo, key and scale; a section's name, bars, chords and
  exits (each a condition and where it goes, reordered, the last "otherwise"); a track's name and instrument, its patch
  opened in the designer from there, whose edits the song plays from the next bar once they pause, until the project
  builds again with them saved; a part's condition, the cue its volume follows, whether it waits for its phrase, its
  grid (quarter, eighth or sixteenth notes or triplets, its notes respaced to keep their timing, `RollEdits.Regrid`),
  whether a line follows the chords, its fill. A track and a part are deleted from a button at the inspector's foot. A condition is edited as rows (a cue or the pass, a comparison, a
  value) joined by all or any, or as text where rows can't hold it. Beside them **the cues**: a slider for a `Cue` with
  the levels parts and exits wait on marked over it and what waits on each, a button per value for a `Cue<T>`; one the
  song doesn't read is disabled. The part chosen is in the panel under the board, past a splitter.
- **The roll.** When the song has a file (section 6) and the part chosen is data there, it is edited below in place of
  its drawing. A line on rows by degree, three octaves of them to scroll through (the wheel, Shift for across), the part's
  notes brought into view as it opens, and under each chord of the section's first pass the rows its notes are on lit, its
  root brighter, as the line plays them (following the chords or not); a click on another row where notes start stacks a
  chord there (`1/3/5`), whose notes are moved, changed and deleted one at a time and lengthen together; a beat on a row for every piece of its kit: a click adds a note, a drag from a note's last step lengthens it, a drag
  elsewhere (or off its row) moves it, a right-click or a second click deletes it. A beat's rows: a click cycles a step
  through `x`, `X`, `o` and off. A strum: `D`, `U`, `d`, `u`, `x`, `_`. The roll's tools are glyphs over it, a hint under
  them saying what a click and a drag do for the part: a line's flat, natural, sharp and slide, then accent and delete for
  the note chosen, and a bar added or taken off the end, whole, so no edit can break a part's
  notation (`NotationText` keeps the text's own spacing and changes a step's token; `RollEdits` are the edits). An edit
  builds the song again from the file as edited (`SongFile.Load`, no compile) and is heard at the next bar; the file is
  written 0.4 s after the edits pause (`SongEditor`), which the next compile takes up. Undo and Redo (Ctrl Z, Ctrl Y)
  cover the session, a drag one edit; Revert puts the part back as the file had it when opened. A file changed
  by something else is read again, and the undo history cleared. A part that is code, or a hook's, says so.
  The patch designer writes its edits to the patch's `.patch.json` the same way, keeping the file's operator names.
- **Patch**, the patch designer. The patch chosen as data (`PatchSpec`): its operators drawn as a graph in render order
  (an operator a column right of the deepest it reads; modulation amber with its index, driving teal and dashed, the
  heard to Out), the voice after them as a chain, and a scope and spectrum of what the bus plays (an `AudioTap` on it).
  A patch is shared: every song with a track playing it plays its edits, so the designer names the songs it is used in.
  **Duplicate…** copies its file under a new name, and a track's inspector, where other songs play its patch too, offers
  **Make a copy for this song**, which copies it as `<patch>-<song>`, gives the track the copy once the project has built
  with it, and opens it, leaving the other songs as they were.
  Beside it an inspector: the operator chosen (heard and at what level and pan, pitch, its kind's settings, level and
  velocity, across the keyboard, modulated or driven only by an operator before it, the envelope; listen to it alone,
  delete it; a node dragged up or down its column moves it in the order, as far as what it reads allows), the voice, the macros with their live values, and the C# that builds it (`PatchCode`), copied
  through the host (`Workbench.CopyText`). Every edit builds the patch again as `Patch.Build` checks it and is heard
  from the next note (`SynthChip.Replace`); what the builder refuses is shown while the last that built plays on. A
  rebuild after a code change keeps the edits; Revert and "Hear its code" go back to the patch as its code has it. A
  catalog names its macros for the code (`WorkbenchCatalog.MacroName`).
- **Kit.** The kit chosen, a pad per piece; and the beat sketcher, a bar of sixteenths per piece (a click turns a step
  to `x`, `X`, `o` or off), looped through the sequencer on a player of its own and copied as `Beat.Sixteenths(…)`.
- **Eject and bring back.** In the roll, **Eject notation** makes the part's notation code (its condition and name stay
  data) and **Eject placement** the whole placement: the C# that makes it (`PartCode`) is added to the song's own half of
  its class and the file points at it (`"part": { "code": "Riff" }`, `"code": "Riff"`). In Patch, **Eject to code** does
  the same for a patch (`PatchCode`, a `partial` property). A piece that is code says where it is; **Bring back as data**
  writes what its code builds back to the file, if a file can say it, and takes its member out. The host does the C#
  (`IMusicCode`; `BinaryVibrance.LunelleStudio.Projects`' `MusicCode`, with Roslyn), and what is ejected plays once the project
  has compiled. A member renamed or taken out by hand fails the build at the generated call.
- **Files** (the rail's last button): every song's and patch's file and the C# that is theirs; the one chosen shown as it
  is, copied whole or a piece at a time: a C# file's class or any member (its indent taken off), a song's track, section
  or part, a patch's operator, each written alone as its file writes it.
- **The keyboard**, docked: three octaves round its octave (moved by Z and X or its buttons), what it plays, how hard, the
  chip's load. Its keys light in the track's hue as the track it plays sounds them in the song (`MusicPlayer.Sounding`),
  keys outside the song's scale are faded, and each C is named.
- **The command palette** (Ctrl K): the catalog, the song's sections and the commands, found by every word typed.

A host lays the window out at `WorkbenchView.FitScale(surface, displayScale)`: the display's scale, but never so large
that the layout is smaller than the workbench needs, so a 1280 by 800 window on a 200% display is laid out at 1.

**The catalog.** The workbench plays a `WorkbenchCatalog`: the songs, patches and kits, each a name and a factory, and
the cues. The factories matter. A build changes what a factory returns, so the workbench calls it again after every
build rather than keeping what it made. `lunelle-studio` makes the catalog from the project's `[MusicCatalog]` class.

What it was doing is kept and restored (`CaptureState`, `RestoreState`): the song, whether it played and in which
section, the loop, a scale played in place of the song's, the cues, the mutes, solos, gains and forced placements, and
the bench's patch, kit, octave and velocity, all by name, as JSON (`WorkbenchStateJson`).

**Keys:** `A W S E D F T G Y H U J K` play an octave of notes on what the keyboard plays, C
to C, as a piano's keys lie. `Z` and `X` move it down and up an octave. `Space` plays or stops, and `1` to `9` jump to a
section. `Ctrl K` opens the command palette; `Escape` closes the palette, the library over the view, or a build's error.
While a text field has the keys (the palette, the library's filter), the workbench leaves them alone.

### Opening a project: `lunelle-studio`

`BinaryVibrance.LunelleStudio.App` builds `lunelle-studio`, the workbench over any music project:

```
lunelle-studio <project.csproj | its folder> [--extension <csproj>]
                                                 the workbench over that project, with its extension
lunelle-studio new <folder>                      a project to start from (HelloSynth's, renamed), opened
```

Under `dotnet run` the arguments follow `--`, as above. A game can keep a one-line script beside its music that runs it
(RubberTrack's `studio.ps1`). It isn't a `dotnet tool`: it is a Kaldra app, with native libraries (SDL 3) and compiled
shaders beside it, built from the Kaldra it shares with the music it opens.

A music project is an ordinary class library whose assembly names its catalog:
`[assembly: MusicCatalog(typeof(Catalog))]` (`BinaryVibrance.Lunelle.Music`). The workbench reads the class's public static
`Songs`, `Patches` and `Kits` (lists of `(string Name, Func<T> Build)`), `Cues` (`IReadOnlyList<CueBase>`) and
`string? MacroName(int id)`, whichever it has. The attribute is the synth's, not the workbench's, so a game's audio
library carries it without referencing any UI.

`BinaryVibrance.LunelleStudio.Projects` does the work, with no window, so tests and other hosts use it as the app does:
- **`MusicProject.OpenAsync(csproj)`** asks a design-time build (`dotnet msbuild -t:PrepareResources;Compile` with
  the compiler's execution skipped, out of process, restoring first if the project never was, in the host's
  configuration when it says which) for the compiler's command line, and makes it a Roslyn project: sources,
  references, embedded resources, analyzers and generators, editorconfigs and options. It asks again only when a
  `.csproj`, `.props` or `.targets` changes or a file comes or goes.
- **A save** (150 ms after the last write) updates that file in the compilation, compiles it whole, generators included,
  emits it to memory with a portable PDB, and loads it into a new collectible `AssemblyLoadContext`. A whole compile has
  no rude edits: a new type, a changed lambda's captures or a type's changed shape all reload. The newest build waits in
  `TakeBuild()`; each frame, **`WorkbenchProject.Update()`** hands its catalog to the workbench
  (`Workbench.UseCatalog`, which keeps the song, the selections and the cue values by name, swapping the song at the
  next bar) or its errors to the build pill (`Workbench.ReportBuild`, "Songs/Tune.cs(12,5): error CS1002: ; expected"),
  and releases the build before it, whose context unloads once nothing playing refers to it.
- **Kaldra is shared, never loaded again.** A build's context resolves whatever the host can (Kaldra above all, whose types
  the workbench and the music must share) from the host's own context, and the rest of the project's references from
  their paths (a project reference from the assembly built beside its reference assembly). So every reference the host
  has loaded must be the very build it runs: a project built against another Kaldra, or a reference rebuilt since the
  workbench started, is refused with a message naming both files (`SharedReferences`, by module version id). A change
  to the project's own references (another project of the game's) needs the workbench restarted.
- **`ProjectTemplate.Create(folder)`** is what `new` writes: HelloSynth's C# and music files, its namespace renamed for
  the folder. Its `.csproj` references `BinaryVibrance.Sirrow.dll` and `BinaryVibrance.Lunelle.dll` from the workbench's own folder, and
  the music generator beside them as its analyzer, so they always match.
- **What it was doing** is kept beside the project, in `.lunelle-studio/workbench.json` (`WorkbenchStateFile.Beside`), restored
  once the first build has played.

**Extensions: a game's own tools.** A game that has more to offer than the music (its mix to play into, so a song
sounds as it will in the game; its own controls; a recorded car under the music) writes an extension, in an assembly
of its own: `[assembly: WorkbenchExtension(typeof(StudioExtras))]` on a public class implementing `IWorkbenchExtension`
(in `BinaryVibrance.LunelleStudio`, so it needs no Roslyn or MSBuild):

- `Output(AudioEngine)`: the bus the music plays into, or null for the workbench's own.
- `Catalog`: its own build of the music, to play at once while the project's first compile runs, or null to wait.
- `Attach(WorkbenchExtensionContext)`: its controls added to the transport's end, the command line the workbench didn't
  take (`--recording race.zip`, read with `Option`).
- `Update(seconds)` each frame; `MusicBuilt(Assembly)` with each new build of the music, for an extension that takes
  something of the music's own from the build (RubberTrack's engine bench takes its engine's numbers from it, so a
  change to them is heard as it is saved); `Dispose`.

The music project names its extension's project with an MSBuild property,
`<LunelleStudioExtension>../Game.Studio/Game.Studio.csproj</LunelleStudioExtension>`, or `--extension <csproj>` gives
one (`--extension none` for none). `lunelle-studio` builds it in its own configuration and loads it once into the
default context before the window opens, where the music's builds share what it references, as they share Kaldra; its
own references load from beside it. It is built without building its references again (`BuildProjectReferences`
false), taking them as they were last built, so it shares the very Kaldra the workbench's own build made: a shared
project built again is another build, even from the same sources, since its sources' paths are in it (a build started
from `c:\` rather than `C:\` compiles it again). Only an extension whose references were never built is built with
them. A game's script that starts the workbench builds the extension first, from the same path (RubberTrack's
`studio.ps1`), so its own references are up to date. It isn't reloaded: a change to its project's sources is said beside the build pill
("Game.Studio changed: restart to take it up"), and needs the workbench restarted. One built against
another build of what the workbench runs is refused, as a project is (`SharedReferences`; a library built for several
frameworks, Vysara, is compared only between builds for the same framework). `lunelle-studio` runs on the newest .NET
installed (`RollForward` `LatestMajor`), so it opens a project, and loads an extension, that targets a newer .NET than
Kaldra does.

RubberTrack's audio library (its songs, patches, kits and their samples) opens and compiles in about 1.7 s, and compiles
again after a save in 0.35 to 0.45 s on a Snapdragon X: well under a bar (2.2 s at 108 bpm).

### Hosting the workbench in an app of your own

An app can put the workbench in a window of its own over a catalog it makes itself, and run under `dotnet watch` for
hot reload in place of `lunelle-studio`'s compiles. It is the way to take the workbench somewhere `lunelle-studio`
can't go; for a game's own mix and controls beside the music, an extension (above) is simpler. Cut to what matters:

```csharp
// Program: SdlAudio.Register() before KaldraApplication.Run
protected override void LoadContent()
{
	var options = new AudioOptions();
	Audio = new AudioEngine(AudioOutput.Open(options), options);
	_ui = new UIHost();
	_painter = UIPainter.Create(Engine, _ui);
	_view = _ui.CreateRoot(UILayer.Default);
	_workbench = new Workbench(Audio, Audio.CreateBus("music"), MyCatalog.Make());
	if (_state.Load() is { } state)                  // a WorkbenchStateFile
		_workbench.RestoreState(state);              // where it was before a restart
	_view.Root.Content = new WorkbenchView(_workbench.ViewModel);
	PushInputLayer(new VysaraInputLayer(_ui));
	// ... a graph with the UI pass over the backbuffer
}

protected override void Update(FrameTime time)
{
	_workbench.HandleInput(Input);                   // the computer's keys
	_workbench.Update(time.DeltaSeconds);            // the player, the meters, and a rebuild after an edit
	// ... _state.Save(_workbench.CaptureState()) once a second: written only when it changed
}

// ... and Audio.Update() once a frame, Pause/Resume, SetFocused and Dispose (audio.md, "Turning it on")
```

`lunelle-studio`'s own app (`BinaryVibrance.LunelleStudio.App/WorkbenchApp.cs`) is this, with a `MusicProject` handing it
each build (`WorkbenchProject`). `WorkbenchView.TransportExtras` is a panel for the host's own controls, on a row of
its own across the transport bar once asked for, scrolling sideways if even that is too narrow. The workbench uses
only `BinaryVibrance.Lunelle`'s public API, so a game can build a tool of its own the
same way: a placement's `Section`, `Track`, `Condition` (whose `ToString` says it in words), `LevelCue` and
`IsOnPhrase`; a cue's `OptionNames` and `OptionValue`; the player's `Get` and `Set` for any cue, `SectionTick` for a
playhead, and `Pattern(placement)`, one pass of a part's notes as the sequencer plays them.

### What hot reload can and can't do

Under `dotnet watch`, in an app hosting the workbench itself. `lunelle-studio` has none of these limits: it compiles the
project whole and loads each build into a context of its own, whose static fields start afresh.

- **A song, a patch and a kit are built by a method or a property, every time.** A `static readonly` field is
  initialised once and hot reload never runs its initialiser again.
- Edits inside a method's body apply live: notes, chords, levels, conditions, a patch's numbers.
- Adding a method, a property, a track or a section usually applies live too.
- Some edits can't (a changed lambda capture, a changed type's shape). `dotnet watch` then asks to restart, or restarts
  by itself when `DOTNET_WATCH_RESTART_ON_RUDE_EDIT` is `true`. The app comes back playing where it was, from its state
  file.
- A song that fails to build after an edit shows its error in the banner, and the old one plays on. Fix it and save.
- A change to a `.xaml` view reloads the view (Vysara's own hot reload). The views are `ItemsControl`s bound to the view
  models, so a reload builds their rows again from what the view models hold.

After an edit, the song rebuilt is swapped in at the next bar (`MusicPlayer.Swap`), in the same section at the same bar.
The mutes, solos, gains and forced placements carry over by name.

---

## 6. Music as data

A patch or a song can be a file rather than code: JSON a tool writes and reads back exactly, which Lunelle's music
generator turns into the C# a person would have written as the project builds. The game builds and runs the C#, so
NativeAOT and the browser are as they were; the workbench edits the files.

**`lunelle-patch/1`** (`PatchFile`): a `PatchSpec` as JSON. Each operator is named and routed by name, and only what differs
from a new spec's defaults is written, so `Write` then `Read` gives back the same spec, float for float.

```json
{
  "format": "lunelle-patch/1",
  "name": "electric-piano",
  "about": "An electric piano: a tine's bell over a tone bar that barks when struck hard",
  "operators": [
    { "name": "strike", "kind": "sine", "ratio": 14, "envelope": "decay 0.06", "velocity": 0.8, "keyLevel": -4 },
    { "name": "tine", "kind": "sine", "envelope": "adsr 0.002 2.2 0 0.2", "level": 0.35, "modulatedBy": [{ "op": "strike", "index": 0.7 }] }
  ],
  "out": [{ "op": "tine" }],
  "sends": { "space": 0.2 },
  "tremolo": { "rate": 4.5, "depth": 0.15 }
}
```

A kind is `sine`, `saw`, `pulse`, `noise`, `level`, `string`, `tube` or `modes`; a sampled operator can't be data. An
envelope is `"gate"`, `"decay s"` or `"adsr a d s r"`; a mode `"at ratio seconds [level]"` or `"fixed hz seconds
[level]"`; a macro its number. `PatchSpec.About` carries `"about"`.

**`lunelle-song/2`** (`SongFile`): a song, its patches, kits, cues and macros named, written a part to a line:
`SongFile.Write(song, names)` from a built song (`SongNames` says what the catalog calls each patch, kit and macro, and
gives a group's effects, which a built song can't), `ToJson` to add what a built song can't say before writing (its
hooks), and `Load(json, bindings)` to build one at run time (`SongBindings` binds the names). A `loom-song/1` file,
whose song-wide `order` or `conduct` chose the next section, is refused, saying how to move it to each section's exits.
Its pieces:

| Key | What it says |
|---|---|
| `tempo`, `meter`, `scale`, `voices` | The song's settings, defaults left out: `"scale": "F# minor"` |
| `effects` | The space, echo and master sends' effects, if not the defaults |
| `groups` | `{ "name", "effects": [{ "fx": "compressor", "thresholdDb": -18, "ratio": 3 }] }`: an effect is one of `Fx`'s factories, its arguments by their parameter names |
| `tracks` | `{ "name", "patch" or "kit", "group", "octave", "level", "pan", "humanise": { "ms", "velocity" }, "swing": { "ratio", "step" }, "macros": [{ "macro", "follow", "from", "to" } or { "macro", "set" }] }` |
| `sections` | `{ "name", "about", "bars", "chords", "parts": [...], "sweeps": [{ "track", "macro", "from", "to" }], "exits": [{ "when": "intensity > 0.7", "then": "open" }, { "then": "cruise" }] }`: the exits tried in order as it ends, the last, with no `when`, always |
| a part | `{ "track", "name", ` a line `"line": "eighths", "notes", "overChords", "octave"`, a beat `"beat", "rows": { "kick": "x..." }`, a strum `"strum", "notes", "voicing", "spread"`, or code `"part": { "code": "Name" }`; then `"when"`, `"level"` (a cue), `"onPhrase"`, `"endWith"` (a part) `}`, or a whole placement as code `{ "track", "code": "Name" }` |
| `hooks` | `["OnBuilt"]`: code of the game's own, run with the song and every section once the rest is built |

A condition (`ConditionText`) is `"intensity > 0.6"`, `"intensity < 0.2"`, `"biome is Forest"` or the section's pass,
`"pass < 2"`, joined by `and`, `or`, `not` and brackets.

**The generator** (`BinaryVibrance.Lunelle.Music.Generator`) reads a project's `AdditionalFiles` ending `.patch.json` and
`.song.json`. Every patch file becomes a property of one `static partial class Patches` in the project's root namespace,
named for its file (`electric-piano.patch.json` is `Patches.ElectricPiano`), written as `PatchCode` writes it. Every song
file becomes `static partial class OpenRoad` (from `open-road.song.json`) with `public static Song Build()`, in the
namespace of its folder. A name in a song is a member of the project's classes: `Patches.UprightBass`, `Kits.General`,
`Cues.Intensity`, `Macros.Bright`; a choice cue's enum and an effect's argument types come from the compilation. What a
file says that can't be code is an error, `LUNELLEMUSIC001`, at its line in the file.

What a file can't say is code, declared by the generated class and written in the game's own half of it, so a missing or
renamed member fails the build: a part (`private static partial Part Name();`), a placement (`private static partial
Placement Name(Section section, SongTrack track);`), a patch (`public static partial Patch Name { get; }`), a hook
(`static partial void OnBuilt(Song song, Section verse, Section chorus);`).

```xml
<ItemGroup>
  <AdditionalFiles Include="Patches\*.patch.json;Songs\*.song.json" />
  <CompilerVisibleProperty Include="RootNamespace" />
  <CompilerVisibleProperty Include="ProjectDir" />
  <ProjectReference Include="..\Lunelle\BinaryVibrance.Lunelle.Music.Generator\BinaryVibrance.Lunelle.Music.Generator.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

The classes the names are found in can be others (`LunelleMusicPatches`, `LunelleMusicKits`, `LunelleMusicCues`,
`LunelleMusicMacros`, each made visible the same way). `MusicGeneratorTests` checks that the generated patch and song build
what `PatchFile.Load` and `SongFile.Load` build from the same files; RubberTrack moved its 48 patches and ten songs onto
the files and every render stayed bit-identical.

---

## 7. Taking a sound apart: `BinaryVibrance.Lunelle.Analysis`

For designing a patch like a recording, or judging a change to a mix without ears. Each takes one channel (or
interleaved stereo for a mix) and its rate, and, like Sirrow's `AudioAnalysis`, allocates.

| Call | What it measures |
|---|---|
| `HitMeasures.Onsets` | Where each hit starts: a jump of 9 dB in the loudest of the last 20 ms |
| `HitMeasures.Pitch` | The pitch under a frequency at given times, from its half-cycles: a kick's fall |
| `HitMeasures.Tones` | The tones standing out of the body (`Tone`: hertz, level) |
| `HitMeasures.Brightness` | The spectrum's centre over a window from a time: how the top fades |
| `HitMeasures.Flatness`, `BandEnvelope` | Each band's noisiness (1 noise, near 0 a tone) and its level over time |
| `HitMeasures.Tail` | How long until it stays under a share of its peak |
| `NoteMeasures.Partials` | A note's partials (`Partial`), its fundamental, and whether they are harmonics |
| `NoteMeasures.Ring` | How one partial rings (`PartialRing`): its peak and when, its fall early and late, how it beats |
| `NoteMeasures.Strike` | How much brighter the first 15 ms are than 60 ms in |
| `MixMeasures.Balance`, `Spread` | A mix's energy by band, its centre, its crest, how wide it is; a stretch in brief |

## 8. Not built yet

- **Music in the browser costs too much to play.** There the mix runs on the frame thread, and a song of eight tracks
  costs several milliseconds of each frame; rendering it ahead on a worker (`AudioOptions.BrowserThread`) has hung the
  page. The chip, effects and offline rendering work there; a browser game should budget for music or leave it out.
- **No tempo change within a song,** and no meter change: a song has one of each. A swap may bring another.
- **The workbench is not a package.** An app references its project. The roll edits a part only where its song has a
  file; a song that is code alone is edited in code.
- **No vectorised inner loops.** The oversampler's half-band filter and the voices' operators are scalar; making them
  wide changes the order of their sums, and so the bits a render gives.
- **Measured on one machine.** The costs quoted here are a Snapdragon X laptop's. A denormal costs little on ARM and a
  great deal on x64; the chip and its effects keep out of that range (envelopes go idle, filters flush once a block),
  but nothing here was timed on x64 or in WebAssembly.
