# Spec: the project and the `.lunelle` file (format 2)

Status: **draft**. Part of format 2, with [overrides.md](overrides.md), [cues.md](cues.md), [patterns.md](patterns.md) and
[exports.md](exports.md). Nothing
here is built yet. Format 1 has no users, so format 2 doesn't read it: the samples and presets are converted once,
by hand or by script.

## 1. What a project is

A project is a set of music entries (songs, patches, kits, patterns and cues), plus a manifest that names the
project and the libraries it draws on. It exists in two forms that hold the same thing:

| Form | Is | For |
|---|---|---|
| **Packed**: one `.lunelle` file | A zip | Passing music around: what a player opens, what users share, what the browser imports and exports |
| **Unpacked**: a folder | The zip's contents, as files | Authoring a game's music: git for the JSON, Git LFS for the samples |

**Unzipping a `.lunelle` gives a working project folder, and zipping a project folder (§6) gives a `.lunelle`.**
Readers make no other distinction: everything is read through "list the paths" and "read the bytes at a path", so a
folder, a zip, embedded resources or a browser's storage all load the same way (as `MusicFolder.Load(paths, read)`
does today).

`.lunelle` always means the packed file. The unpacked form is recognised by its manifest, `lunelle.meta.json`.

**A library is just a project.** Any project can be referenced by another as a library (§4). Lunelle's built-in
sounds are a project too.

## 2. Layout

```
lunelle.meta.json              the manifest (§3)
Songs/**/*.song.json           songs
Patches/**/*.patch.json        patches
Kits/**/*.kit.json             kits, as one file each (of patches, or naming samples elsewhere in Kits/)
Kits/**/kit.json               kits, as a folder each; the folder holds its samples, licence and notes
Patterns/**/*.pattern.json     patterns (patterns.md)
Cues/**/*.cues.json            cue definitions (cues.md)
Libraries/<name>@<version>/    bundled libraries (§6), each a project of its own
```

- **Anything else is kept but not read.** That includes a root `README.md` or `LICENSE`, a kit folder's
  `LICENSE.txt` and `SOURCE.md`, and its samples. Packing carries all of it.
- **Never part of a project, and never packed:** `.lunelle-studio/` (the workbench's state, the transaction log),
  `bin/`, `obj/`, `.git/`, and `.gitattributes`/`.gitignore`.
- **A `.lunelle` holds no code.** In a project with code, `*.cs` and `*.csproj` files sit beside the music, but
  packing leaves them out (§6).

### 2.1 Entry identity

**An entry's id is its path below its kind's folder, without the kind's suffix**, with `/` between folders:

| File | Kind | Id |
|---|---|---|
| `Patches/electric-piano.patch.json` | patch | `electric-piano` |
| `Patches/bass/sub.patch.json` | patch | `bass/sub` |
| `Kits/breaks.kit.json` | kit | `breaks` |
| `Kits/drums/rock/kit.json` | kit | `drums/rock` |
| `Songs/world-1/overworld.song.json` | song | `world-1/overworld` |

- **No `"name"` inside an entry identifies it.** Format 1 named a song and a kit by its `"name"`, and a patch by its
  file; format 2 always uses the path. An optional `"title"` is shown to people instead of the id.
- **Folders are the groups a library browser shows.** Moving a file renames the entry. The studio does that as one
  transaction that updates every reference.
- **Each segment is lowercase `a-z`, `0-9` and `-`,** starting with a letter. That keeps ids identical on every
  file system, and lets the generator map them to C# identifiers (§7).
- **Ids are unique per kind.** A patch and a kit may share an id. Two entries of one kind with the same id, such as
  `Kits/rock.kit.json` and `Kits/rock/kit.json`, are an error.
- **No id may also be a folder of the same kind.** `Patches/bass.patch.json` beside `Patches/bass/sub.patch.json` is
  an error, because the generator would need both `Patches.Bass` and a `Patches.Bass` class.

### 2.2 References

One entry refers to another by id. Which project the id belongs to is given by a prefix:

| Written | Means |
|---|---|
| `electric-piano` | This project's entry |
| `lunelle:keys/electric-piano` | The entry `keys/electric-piano` in the library this project calls `lunelle` (§4) |

- `:` separates the library's name from the id. `/` is only ever a folder inside an id.
- **The kind always comes from where the reference is written**, so a track's `"patch"` names a patch. References
  never name the kind themselves.
- **An entry inside a library resolves its references in that library**, against that library's own manifest. So
  `lunelle:` written inside the game's library means the library the *game library* calls `lunelle`.
- A song-local definition (overrides.md §4) is found before the project's entry of the same id.

**The paths a file names are relative to that file's folder**, such as a kit's samples. They may not leave the
project's root, and in a bundled library they may not leave the library's folder. A path that does is an error
(`MusicArchive` refuses the same today).

## 3. The manifest: `lunelle.meta.json`

```json
{
  "format": "lunelle/2",
  "name": "showcase",
  "title": "Showcase",
  "version": "1.0.0",
  "about": "Five songs of music files alone, arranged by their cues as they play.",
  "author": "BinaryVibrance",
  "licence": "CC-BY-4.0",
  "libraries": {
    "lunelle": { "version": "1" },
    "game": { "path": "../GameSounds" }
  },
  "macros": {
    "bright": { "number": 0, "about": "Opens the filter: duller at 0, brighter at 1" },
    "drive": 1
  },
  "songs": ["aurora", "back-porch", "watchtower"]
}
```

| Key | Required | Is |
|---|---|---|
| `format` | yes | `"lunelle/2"`. Readers refuse any other major version, saying which they read. |
| `name` | yes | The project's identity when it is used as a library, written as one id segment (§2.1). |
| `title` | no | Its name for people. |
| `version` | no | The project's own version, as [semver](https://semver.org). It is required of a library that others pin by version (§4.2). |
| `about` | no | A description (§5). |
| `author`, `licence` | no | For players, albums and libraries. `licence` is an SPDX id or free text. A kit's own licence stays in its folder. |
| `libraries` | no | The libraries this project references, by the local name used in its references (§4). |
| `macros` | no | The macro names its songs use, and the number each turns (`0`–`7`): as a bare number, or as an object with `number` and `about`. |
| `songs` | no | An order for players: the listed songs first, then every other song by id. Listing a song that doesn't exist is an error. |
| `album` | no | A soundtrack made of song exports ([exports.md](exports.md) §8). |

**Not in the manifest:**
- **Cues.** They are entries under `Cues/` (cues.md), so a project with hundreds of cues doesn't funnel every change
  through one file.
- **The entries themselves.** They are found by the layout (§2), so adding a song never touches the manifest.
- **Anything about the workbench.**
- **Code-mode settings.** The extension is named by the `.csproj`.

Unknown keys are an error. That catches a typo now and keeps the names free for a later version.

## 4. Libraries

```json
"libraries": {
  "lunelle": { "version": "1" },
  "game": { "path": "../GameSounds" },
  "chiptune": { "path": "Libraries/chiptune@2.1.0", "version": "2.1.0" }
}
```

The key is the **local name** this project's references use (§2.2). It is any id segment, chosen by the project.
It needn't match the library's own `name`.

### 4.1 Sources

Each entry gives exactly one source:

| Source | Resolves to |
|---|---|
| `"path": "<relative path>"` | A project folder, or a `.lunelle` file, relative to this manifest. Packing writes `Libraries/…` paths (§6). |
| `"version": "<major>[.<minor>[.<patch>]]"` alone | A library the host provides with this `name`. Today that is only Lunelle's built-in library, `lunelle`. |

**A path that leaves the project** (`../GameSounds`) is allowed in the unpacked form. That is how several music
projects in one game share a game library. A packed file never has one, because packing bundles every library (§6).

### 4.2 Versions

- `version` on a host-provided library is a **minimum within a major version**. `"1"` accepts any 1.x the host
  provides, and `"1.2"` any 1.x from 1.2. Hosts provide one version per major. If the host lacks that major, or
  provides an older minor, that is an error naming both versions.
- `version` beside `path` pins the version the project expects. If the library's own `version` differs, the project
  gets a **warning**, because a library's sounds can change between versions.
- **Lunelle's built-in library promises that a released entry never changes its sound within a major version.**
  Fixes and new sounds are added as new entries; changing a released one needs a new major version. A game that
  wants its sounds frozen whatever happens copies the library in (as a `path` library), which packing does anyway.

### 4.3 Resolution

- Libraries form a graph: a library may reference libraries of its own. Each library resolves its references
  against its own `libraries` (§2.2).
- **A cycle is an error**, naming the chain.
- **A project may only refer to the libraries it lists itself.** To use `lunelle:` sounds directly, the project
  lists `lunelle` too, even when its game library already does.
- **A library may be reached by more than one route.** Two routes to the same library `name` at different versions
  are allowed. Each is resolved where it is referenced, and packing bundles both (§6).
- A library that can't be found (a missing path, or a host with no such library) is an error. Entries that
  reference it get errors, and the rest of the project still loads.

## 5. `about`

**Every entry, cue, cue option, macro and manifest can have an `"about"`**: one or more sentences, in plain text,
saying what it is for. The name `about` is already what songs, sections and patches use for this.

- The studio shows it: as a cue's tooltip, under an entry in the library, and on a macro's knob.
- In a project with code, the generator writes it as the XML doc comment on the generated member, so a game's
  programmer sees it in IntelliSense.
- **Files have no comments.** The studio rewrites these files, which would lose them silently. `about` is how
  notes are kept, and it survives every save.

## 6. Packing and unpacking

### 6.1 Packing a folder into a `.lunelle`

1. **Start from the project's files**: everything under the root, except what §2 excludes. A code file (`*.cs`,
   `*.csproj`, `Directory.*.props`/`.targets`) is left out with a note. A song naming code (a part, placement or hook
   as code) still packs: it plays a rest there, as `MusicFolder` does today, and the note lists each one.
2. **Bundle every library the project references**, at `Libraries/<name>@<version>/`, using the library's own
   `name` and `version`, or `Libraries/<name>/` if it has no version. A library bundles only:
   - the entries the project actually uses, including everything those entries in turn use: bases, a kit's
     patches, a pattern's patterns, a kit's samples, licence and notes;
   - its manifest, with `songs` removed and `libraries` reduced to the ones still used.

   Its own libraries are bundled the same way, side by side under the top-level `Libraries/` (never nested), and its
   manifest's `path`s are rewritten to point at them.
3. **Rewrite the manifest's library entries** to `{ "path": "Libraries/<name>@<version>", "version": "<version>" }`.
4. **Write the zip deterministically**, so packing the same project twice gives the same bytes:
   - entries sorted by path (ordinal), with `/` separators;
   - a fixed timestamp (1980-01-01);
   - JSON compressed with Deflate, and samples (`.ogg`, `.flac`, `.wav`) stored, since they are compressed already;
   - no zip comment and no extra fields.

**An unused library entry is never bundled.** A `.lunelle` of one song that uses three Lunelle patches carries those
three patches, not the whole built-in library.

### 6.2 Unpacking

Unpacking extracts the zip as it is. The result is a working project, with its libraries bundled under `Libraries/`.

The round trips, exactly:
- **`pack(unpack(f)) == f`**, byte for byte.
- **`unpack(pack(p))`** is `p` minus what §6.1 step 1 leaves out, with each library rewritten as its bundled copy.
  The songs, patches, kits, patterns, cues and manifest fields are otherwise unchanged.

### 6.3 Reading a packed file directly

A player or the studio may read a `.lunelle` without extracting it, because everything is read by path (§1). The
studio edits a packed file through its transaction log, and saving writes the zip again (§6.1, step 4).

**Packed files are read defensively.** An entry whose path is absolute, contains `..`, or has `\` is an error (as
`MusicArchive` does today). A zip with more than one `lunelle.meta.json` takes the one at its root. A zip with none
at its root, but exactly one in a single top-level folder, is read from that folder: that is a zipped project folder.

## 7. In a project with code

The `.csproj` names its manifest. Lunelle's MSBuild targets add the project's entries, and its libraries' entries,
as `AdditionalFiles` for the generator:

```xml
<ItemGroup>
  <LunelleProject Include="lunelle.meta.json" />
</ItemGroup>
```

- **The built-in library comes with the Lunelle package.** A `path` library is read from where it is.
- **The generator builds from the same resolution as the runtime** (overrides.md §6), so a project plays the same
  with code or without it.
- **Ids map to C# names by segment**: `bass/sub` is `Patches.Bass.Sub`, and `lunelle:keys/electric-piano` is
  `Patches.Lunelle.Keys.ElectricPiano`. Generated classes are nested and static.
- Only entries a song or the game actually uses are generated from a library, so a large library costs nothing it
  doesn't use. The game asks for more with `<LunelleUse Include="lunelle:…" />`.

The generator's handling of cues, macros and kits in format 2 is in cues.md and a later generator spec. This spec
only says where the files come from.

## 8. Errors

Each is reported against the file (and, where possible, the line) it is in. **Only the entries an error touches
fail; the rest of the project loads.** `MusicFolder.Problems` does this today.

| Error | Where |
|---|---|
| No `lunelle.meta.json` at the root | The project |
| A `format` that isn't `lunelle/2`, or a file of the wrong kind or version for its folder | The file |
| An unknown key in the manifest | The manifest |
| An id that breaks §2.1's rules, two entries with one id, or an id that is also a folder | Each file involved |
| A reference to a library that isn't listed, or to an entry that doesn't exist | The referencing file |
| A library that can't be found, a version that isn't provided, or a cycle | The manifest |
| A path leaving the project or the library | The file naming it |
| `songs` listing a song that doesn't exist | The manifest |

| Warning | Where |
|---|---|
| A `path` library's `version` differs from the pinned one | The manifest |
| Packing left out code, or a song names code that plays a rest | The pack's notes |

## 9. Test cases

An implementation passes at least these:

1. **Identity.** The table in §2.1 gives exactly those ids. `Kits/rock.kit.json` with `Kits/rock/kit.json` is an
   error on both files. `Patches/Bass.patch.json` breaks the naming rules.
2. **References.** `lead` and `lunelle:lead` resolve to different entries. An entry inside the game library that
   says `lunelle:x` resolves through the game library's `lunelle`, even when the project's `lunelle` is another
   version.
3. **Libraries.** A missing path library is an error on the manifest, and on each entry that uses it. Every other
   entry loads. A cycle A→B→A is an error naming both.
4. **Versions.** A host providing `lunelle` 1.4 accepts `"1"` and `"1.2"`, and refuses `"1.5"` and `"2"`.
5. **Packing is deterministic.** Packing the Showcase twice gives identical bytes.
6. **Round trips.** `pack(unpack(f)) == f` for the packed Showcase. Unpacking and loading it plays the same as the
   folder it came from (rendered offline and compared).
7. **Bundling.** A project using three `lunelle:` patches bundles exactly those three (and their bases), and none of
   the rest. A kit's samples, licence and notes come with it. Two versions of one library bundle side by side.
8. **Defensive reading.** A zip with `../evil.song.json`, an absolute path, or a `\` is refused. A zipped project
   folder (one top-level folder holding the manifest) opens.
9. **Exclusions.** `.lunelle-studio/`, `bin/`, `obj/`, `.git/`, `*.cs` and `*.csproj` never reach a `.lunelle`.

## 10. Out of scope

- A registry or download of libraries by version. A host provides what it ships, and everything else is a `path`.
- Reading format 1.
- Encryption or signing of `.lunelle` files.
- Code in a `.lunelle`.

## Open questions

- Is `name` + `version` enough identity for a library, or do two unrelated libraries both called `drums` need a
  publisher prefix as well?
