# Spec: cues (format 2)

Status: **implemented** (format-2 branch). Builds on [project.md](project.md).

## 1. What changes from format 1

- **Cues are entries under `Cues/`**, not a list in the project file. A game with a hundred minigames has hundreds of
  cues, and they shouldn't all funnel through one file.
- **Cues have namespaces** (`lava/heat`), which the studio's cue panel shows as a tree.
- **Songs import the cues they use**, each with the value the studio previews it at. The studio shows a song only
  its imported cues. A song reading a cue it didn't import is an error.
- **Cues are data in both modes.** A project with code no longer declares cues in C#: the generator writes them from
  the cue files (§6).
- **Every cue and option can have an `about`** (project.md §5).

What stays the same: a cue is a level from 0 to 1 or a choice among named options, and the game sets it. Conditions
(`"lava/heat > 0.6"`, `"lava/phase is Race"`), and `Above`/`Below`'s 0.05 hold, are unchanged.

## 2. Cue files

```json
{
  "format": "lunelle-cues/1",
  "namespace": "lava",
  "about": "The lava minigame: the floor fills with lava as the round goes on",
  "cues": [
    { "name": "heat", "initial": 0, "about": "0 at the countdown, 1 once the floor is all lava" },
    {
      "name": "phase",
      "options": [
        "Countdown",
        { "name": "Race", "about": "From the whistle until someone reaches the top" },
        "Finish"
      ],
      "initial": "Countdown"
    }
  ]
}
```

- **A file is any `Cues/**/*.cues.json`.** Its name and folder are free: they are only for keeping cues tidy and
  merges small.
- **`namespace`** is optional, made of id segments (project.md §2.1) joined by `/`. A cue's id is
  `<namespace>/<name>`, or just `<name>` with no namespace. One namespace may be spread over several files.
- **A cue's `name`** is one id segment.
- **Cue ids are unique across the project.** The same id in two files is an error on both.
- **A level cue** has `initial` from 0 to 1 (default 0).
- **A choice cue** has `options`: two or more, each a name or `{ "name", "about" }`. Option names are C#-style
  identifiers (`Countdown`, `GoodFuture`), unique within the cue, because they become enum members (§6). `initial`
  names an option (default: the first one).
- **A variation's name may not equal a cue's id**, as today, so a condition is never ambiguous.
- **Library cues**: a song can import `game:intensity` from a library (project.md §2.2). At runtime a cue is known
  by **its library's own `name` plus its id** (`game` + `intensity`), never the local name used to reference it. Two
  music projects that both reference the game library therefore share one `intensity`, which the game sets once.

### 2.1 Option values

**A choice's value at runtime is its option's index**, as in format 1. Option `i` is `i`, as an enum numbered from 0.

**Files never write the index.** Conditions, song imports and exports name options. So **reordering options changes
nothing in any file.** It does change the generated enum's values (§6), which matters if the game stores them, for
example in a save game. The studio warns in a project with code.

**Renaming an option** changes every condition, song import and export naming it. The studio does that as one
transaction.

## 3. Importing cues into a song

```json
{
  "format": "lunelle-song/3",
  "cues": {
    "intensity": 0.6,
    "lava/heat": 0.2,
    "lava/phase": "Countdown",
    "game:mood": null
  }
}
```

- **`cues` maps each imported cue to its preview value.** That is a level from 0 to 1, an option's name, or `null`
  for the cue's own `initial`.
- **The order written is the order the studio shows them.** The studio writes the map in the order the user
  arranges it.
- **Every cue the song reads must be imported**:
  - in a part's `when` or `level`;
  - in an exit's `when`;
  - in a track macro's `follow`;
  - in a section sweep or anything else that names a cue.

  Reading one that isn't imported is an error, naming the place. The studio prevents this case: choosing a cue in a
  condition imports it.
- **Importing a cue the song never reads is allowed.** It's shown, dimmed, in the studio, as `CueViewModel.IsUsed`
  does today. It may be there to preview or to export with (exports.md).

### 3.1 What the preview value means

**It is the value the studio starts the song at, and nothing more.** At runtime the game owns its cues: playing a
song doesn't set any cue, and a cue keeps whatever the game last set it to, across songs. The preview value never
reaches a game's build.

An export (exports.md) has starting values of its own. It takes the preview values only where it gives none.

## 4. Macros

Macros stay in the manifest (project.md §3). They are few, they belong to patches rather than to songs, and they
aren't imported. A track macro that `follow`s a cue needs that cue imported (§3).

## 5. Errors

| Error | Reported on |
|---|---|
| A cue id defined twice | Each file defining it |
| A bad `namespace` or `name`, or an option name that isn't a C# identifier | The cue's file |
| A choice with fewer than two options, a repeated option, or an `initial` naming no option | The cue's file |
| A level's `initial` outside 0–1 | The cue's file |
| A song importing a cue that doesn't exist, or giving a preview value of the wrong kind | The song's `cues` |
| A song reading a cue it didn't import | The place that reads it |
| A variation whose name is a cue's id | The song |

## 6. In a project with code

The generator writes one static class per namespace under `Cues`, with an enum per choice cue:

```csharp
// generated from Cues/lava.cues.json
public static partial class Cues
{
	public static partial class Lava
	{
		/// <summary>0 at the countdown, 1 once the floor is all lava</summary>
		public static readonly Cue Heat = new("lava/heat");

		public enum PhaseOption
		{
			Countdown,
			/// <summary>From the whistle until someone reaches the top</summary>
			Race,
			Finish,
		}

		public static readonly Cue<PhaseOption> Phase = new("lava/phase");
	}
}
```

- **The C# names come from the ids**, by segment, as for every entry (project.md §7).
- **The cue's runtime name is its full id, plus its library's `name` for library cues**, so the same cue resolves
  the same way in both modes.
- **A game that wants to use an enum of its own** for a choice binds it in the cue file:
  `"enum": "MyGame.Biome"`. The generator checks that the enum has exactly the options, by name, and uses its values.
  A project without code ignores `enum`, and uses the indexes (§2.1).
- **A game no longer declares cues in C# for songs to use.** A song naming a cue that isn't in a cue file is an error
  (§5), in both modes.

## 7. Test cases

1. **Ids.** `namespace: "lava"` with `heat` gives `lava/heat`. The same namespace over two files merges. A
   duplicate id in two files is an error on both.
2. **Imports.** A song whose condition reads `lava/heat` without importing it is an error at the condition. Once
   imported, it loads. An imported cue the song never reads loads, reported as unused.
3. **Preview values.** `null` previews at the cue's `initial`. `"Race"` on a level cue is an error. Playing the song in
   a player doesn't change any cue's value.
4. **Option order.** Reordering `phase`'s options in its file leaves every song playing the same in a project of
   files alone, rendered and compared.
5. **Library cues.** Two projects referencing one game library by different local names share one `intensity` at
   runtime.
6. **Both modes.** The generated `Cues` classes and enums match §6. Songs play the same with code and without,
   rendered and compared. A bound `enum` with a missing or extra member is an error.

## 8. Out of scope

- A song setting cues when it starts. If wanted, that's an explicit, separate setting, specified later.
- Cue types other than a level and a choice (a number with a range other than 0–1, a boolean).
- Cues scoped to one song: every cue is project-wide (or library-wide), and a song only imports it.

## Open questions

- Should a namespace's `about` (here from the file) live in one place if the namespace spans several files? As
  written, each file may say it, and the studio shows the first it finds.
- Is a boolean cue worth having, as a choice of `Off`/`On`, or is that a level?
