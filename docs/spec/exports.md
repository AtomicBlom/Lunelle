# Spec: song exports (format 2)

Status: **draft**. Builds on [project.md](project.md) and [cues.md](cues.md).

## 1. What an export is

A song that arranges itself from cues never plays the same way twice in a game. Its composer still needs fixed
recordings of it: for a soundtrack album, a trailer, or a game that would rather stream audio than run Lunelle. An
**export** is a scripted run of a song: where it starts, what its cues and macros are and when they change, and how
it ends. It renders to a file, **the same every time**.

**A song can have several exports.** One song written with past, present, good-future and bad-future arrangements
has four, each with its own script.

## 2. In the song

```json
{
  "format": "lunelle-song/3",
  "cues": { "era": "Present", "intensity": 0.3 },
  "exports": [
    {
      "name": "good-future",
      "title": "Stardust Speedway (Good Future)",
      "about": "The album version: builds at bar 17, and the bridge comes in on the second loop",
      "seed": 1,
      "cues": { "era": "GoodFuture", "intensity": 0.3 },
      "events": [
        { "at": "17:1", "cue": "intensity", "to": 0.8, "over": "2 bars" },
        { "at": "section lift", "track": "bass", "macro": "bright", "to": 1, "over": "4 beats" },
        { "at": "section lift#2", "jump": "bridge" }
      ],
      "end": { "at": "section outro end", "fade": "0s", "tail": "4s" }
    }
  ]
}
```

| Key | Required | Is |
|---|---|---|
| `name` | yes | An id segment (project.md §2.1), unique in the song. The export's full id is `<song>/<name>`, such as `stardust-speedway/good-future`. |
| `title`, `about` | no | For the file's tags and the studio. |
| `seed` | no | The seed variations are drawn from (`MusicPlayer.Prepare(…, seed)`), default 0. Change it for different fills; keep it for the same ones. |
| `scale` | no | Play the song in another scale, as `MusicPlayer.Prepare(song, scale)` does. |
| `start` | no | The section to start at (default: the song's first). |
| `cues` | no | The starting value of imported cues (cues.md §3). Each cue not given here takes the song's preview value, and then the cue's `initial`. Every cue named must be imported by the song. |
| `events` | no | What changes, and when (§3). |
| `end` | yes | When and how it ends (§4). |
| `loop` | no | Loop points to write into the file (§5). |

## 3. Events

Each event has an `at` (§3.1) and one action:

| Action | Written | Does |
|---|---|---|
| Set a cue | `"cue": "intensity", "to": 0.8` | Sets it, as `MusicPlayer.Set` does. A choice names its option: `"to": "BadFuture"`. |
| Ramp a level cue | `"cue": "intensity", "to": 0.8, "over": "2 bars"` | Moves it linearly from its value at `at` to `to`. |
| Set or ramp a track's macro | `"track": "bass", "macro": "bright", "to": 1[, "over": …]` | Holds the track's macro at the value, over whatever it follows, until a later event, or the end. **This needs a new `MusicPlayer` call** (a held macro per track), since a game can't do this today. |
| Release a track's macro | `"track": "bass", "macro": "bright", "to": null` | Gives the macro back to what the song says (a cue it follows, or its own value). |
| Jump | `"jump": "bridge"` | Jumps to the section, as `MusicPlayer.Jump` does. |

- `over` is `"<n> bars"`, `"<n> beats"` or `"<n>s"`. A ramp ends exactly at its end. A later event on the same cue or
  macro takes over from wherever the ramp has got to.
- **Events at the same `at` apply in the order written.**
- A cue in an event must be imported by the song. A track and its macro must exist, and the track's patch must use
  that macro.

### 3.1 Positions

| Written | Is |
|---|---|
| `"17:1"`, `"17:2.5"` | Bar 17, beat 1 (or two and a half), **counting from the export's start**, bar 1 beat 1 being the first frame. Bars keep counting through every section and jump, as `MusicPlayer`'s absolute bar does. |
| `"section lift"` | The first time section `lift` starts. |
| `"section lift#3"` | The third time it starts. |
| `"section lift end"`, `"section lift#3 end"` | Where that pass of it ends. |

- **A section position is found by playing.** Since the run is deterministic (§6), it is always the same frame.
- **An event whose position is never reached** (the section never comes round, or comes round after `end`) is an
  error when rendering, naming the event. It isn't skipped, so a script never quietly does less than it says.
- **The positions are in the song's own time.** A change of tempo moves them with the music, not with the clock.

## 4. The end

```json
"end": { "at": "section outro end", "fade": "8s", "tail": "3s" }
```

| Key | Is |
|---|---|
| `at` | A position (§3.1): where the end begins. |
| `fade` | Default `"0s"`. If longer, the music plays on past `at`, fading to silence over that time. |
| `tail` | Default `"auto"`: after the music stops (at `at`, or at the fade's end), notes are let go and the render goes on for their release and the effects' tails, until the output stays below -90 dBFS for 100 ms, or 10 s at most. A time (`"3s"`) renders exactly that much. |

There is no "loop the song N times": a song's form is its sections' exits, so "the second time round" is
`"section outro#2 end"`, or a bar.

## 5. Loop points

```json
"loop": { "from": "section verse", "to": "section verse#2" }
```

- **This writes loop points into the file**, so a game streaming the export can loop it: in WAV's `smpl` chunk, and
  as `LOOPSTART`/`LOOPLENGTH` comments in Ogg Vorbis when that output exists.
- **The positions are frames in the rendered file.** `to` must be after `from` and before the end.
- **Lunelle doesn't make the loop seamless.** A loop between two starts of the same section, with the same cues, is
  close. But reverb and release tails from before `to` are cut at the seam, and a variation may pick differently on
  each pass. The studio shows the seam so the composer can hear it.

## 6. Determinism

**The same export of the same song renders the same samples, bit for bit, every time**, given:
- the same Lunelle build,
- the same resolved project (every entry the song uses, after overrides and patterns),
- the same sample rate,
- the same processor architecture.

How the renderer keeps that promise:
- It renders with **Sirrow's `OfflineSession`** (rendering on the calling thread, `AudioWork.Update`), never through
  a live device or on wall-clock time.
- **An event applies at its exact frame.** The renderer renders up to that frame, applies the event, and goes on. The
  event frames are the same every run, so the blocks are split at the same places every run.
- Ramps are updated at a fixed interval of 64 frames, the patch parameters' own rate, and applied in the same way.
- Randomness comes only from the seeds, and Lunelle has no other source.

**Not promised**:
- the same bits from x64 and Arm64. The JIT may fuse multiply-adds differently;
- the same bits from different Lunelle versions, since the DSP may change;
- the same bits as live playback in the studio, which splits blocks where the audio device asks.

Previewing an export live in the studio plays the same music, but isn't the file.

## 7. Output

- **WAV**, 48 kHz by default (44.1 kHz on request), 24-bit PCM by default (32-bit float on request).
- **Named** `<song>--<export>.wav`, with each `/` in an id written as `-`.
- **Tags** in a `LIST`/`INFO` chunk: `INAM` the title (the export's `title`, else the song's, else its id), `IART`
  the manifest's `author`, `ICMT` the export's `about`, `IPRD` the album (§8), `ITRK` its track number.
- Loop points as in §5.
- **Later**: FLAC and Ogg Vorbis (which need encoders, a new dependency), MP3, and **stems**: the same run written
  once per track or group, all the same length, summing to the mix.

## 8. Albums

An optional `album` in the manifest gathers exports into a soundtrack:

```json
"album": {
  "title": "Showcase: Original Soundtrack",
  "tracks": ["aurora/dawn-to-lift", "watchtower/main", "stardust-speedway/good-future"]
}
```

- **"Export album"** renders every track in order, numbered, tagged with the album's title, into one folder.
- Each track is an export's full id. A missing one is an error on the manifest.
- This adds `album` to the manifest's keys (project.md §3).

## 9. Where it runs

**The renderer is part of Lunelle**, not the studio:

```csharp
SongExport.Render(MusicProject project, string exportId, ExportOptions options, Stream output);
```

So the studio, a player, a test and a command-line tool all write the same bytes. It runs anywhere `OfflineSession`
does, including the browser, where it renders in chunks between frames so the page keeps drawing.

## 10. Errors

| Error | Reported on |
|---|---|
| A repeated export `name` in a song | The song |
| A cue not imported by the song, a missing track or macro, or a missing section in `start` or `jump` | The export, at the key |
| A malformed position, or an `over` that isn't bars, beats or seconds | The event |
| A position never reached | The event, when rendering |
| `loop.to` not after `loop.from`, or past the end | The export, when rendering |
| An album track naming no export | The manifest |

## 11. Test cases

1. **Determinism.** An export rendered twice on one machine gives identical bytes. With a different `seed`, the
   fills differ. With the same seed, they're the same.
2. **Event timing.** A cue set at `17:1` is in effect from that bar's first frame. A part conditioned on it comes in
   at bar 17, rendered and checked by onset (`HitMeasures.Onsets`).
3. **Ramps.** A level ramp of 2 bars reaches its target exactly at its end. A later event takes over mid-ramp.
4. **Sections.** `section lift#2` lands where the second pass of `lift` starts. One that's never reached is an error.
5. **The end.** `fade` brings the output to silence over its time. `tail: "auto"` stops after the effects ring out,
   and never later than 10 s.
6. **Loop points.** The `smpl` chunk holds the frames of `from` and `to`, as a reader such as `AudioDecoder` sees
   them.
7. **Macros.** A held macro overrides the cue it follows until it is released, after which the cue takes over
   again.
8. **Album.** Export album writes every track, numbered and tagged, in order.

## 12. Out of scope

- Mixing or mastering after rendering (normalising, limiting). The song's master effects are the mastering.
- Video or timecode syncing.
- Recording a live session into an export. That's the studio's (it writes `events` from what the user does), not
  the format's.

## Open questions

- Should `end` also allow `"after": "3:20"` in clock time, for a trailer cut to a fixed length?
- Should stems be decided now, so `events` can target a stem's mute?
