# Lunelle

A synthesizer for .NET games: a 16-operator chip with patches as data, samples and drum kits, and music written as
code or as files, with a sequencer on the audio clock that a game steers through cues. It plays through
[Sirrow](https://github.com/AtomicBlom/Sirrow)'s mixer and is built from Sirrow's effects and DSP building blocks. Music is
written in [LunelleStudio](https://github.com/AtomicBlom/LunelleStudio). Lunelle was split out of Sirrow, and before
that out of the [Kaldra engine](https://github.com/AtomicBlom/Kaldra), once called Loom.

| Project | What it is |
|---|---|
| `BinaryVibrance.Lunelle` | The chip (`.Chip`), music and its player (`.Music`), and measures for designing patches (`.Analysis`). Its package carries the music generator. |
| `BinaryVibrance.Lunelle.Music.Generator` | The source generator that makes a project's patch and song files into C#. |
| `samples/HelloSynth` | A music project: a song, four patches and a kit. |

`docs/synth.md` is the guide.

## Building

Lunelle builds on a checkout of Sirrow beside it (`..\Sirrow`), or another named by `SirrowSource` (an environment
variable reaches every repository at once; `build/Sirrow.Source.props`). `SirrowSource=none` takes Sirrow's package
instead, at the version `Directory.Packages.props` pins.

```
git clone https://github.com/AtomicBlom/Sirrow ../Sirrow
dotnet build BinaryVibrance.Lunelle.slnx
dotnet run --project BinaryVibrance.Lunelle.Tests
```

## Licence

MIT (`LICENSE`).
