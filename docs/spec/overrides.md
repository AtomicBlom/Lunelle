# Spec: overrides (format 2)

Status: **implemented** (format-2 branch). Builds on [project.md](project.md) (ids, references, libraries).

## 1. What overrides are for

A sound can be adjusted in layers without copying it. Lunelle ships `lunelle:saw-lead`. A game makes its `lead`
from it with a darker filter, and one song makes `lead` brighter again for its finale. Each layer writes only what
it changes. When the layer below changes, every layer above picks the change up, except where it overrode that very
field.

This spec covers **patches** and **kits**. Patterns have transforms of their own (patterns.md). Songs aren't
overridden: a song is the top layer.

## 2. Derived entries

An entry with a `"base"` is **derived**. It is its base, resolved fully (§5), with its own changes applied:

```json
{
  "format": "lunelle-patch/2",
  "base": "lunelle:saw-lead",
  "about": "The game's lead: darker, and wider",
  "set": {
    "filter.cutoff": 1800,
    "operators[saw2].detune": 9,
    "sends.space": 0.3
  },
  "remove": ["operators[sub]"],
  "insert": [
    { "into": "operators", "before": "saw1",
      "item": { "name": "wobble", "kind": "sine", "ratio": 0.5, "envelope": "gate", "level": 0 } }
  ]
}
```

- **A derived entry has `base` and any of `set`, `remove` and `insert`, and nothing else** besides `format`,
  `about` and `title`. A file with `base` and the entry's own content (`operators` alongside `base`) is an error. So a
  file is always either whole or derived, and it is obvious which.
- **`base` is a reference** (project.md §2.2) to an entry of the same kind, whole or derived.
- **An entry with no `base` is whole**, as in format 1.

## 3. Paths

A path names one place in an entry: property names joined by `.`, with an item of a keyed array named by its key in
brackets.

```
filter.cutoff
sends.space
operators[tine].ratio
operators[tine].modulatedBy[strike].index
out[body].pan
macros[0/filterOctaves].to
pieces[kick].gainDb
```

### 3.1 Keyed arrays

Order matters in these arrays, but positions don't identify items, so they are addressed by key, never by index.
Each keyed array's key is fixed by the schema:

| Entry | Array | Key |
|---|---|---|
| patch | `operators` | `name` |
| patch | `operators[…].modulatedBy` | `op` |
| patch | `operators[…].drivenBy` | the item itself (an operator's name) |
| patch | `out` | `op` |
| patch | `macros` | `<macro>/<target>`, plus `/<op>` for a target on one operator: `0/filterOctaves`, `2/opLevel/tine` |
| kit | `pieces` | `piece` |
| kit | `pieces[…].layers` | `upTo`, written as it is in the file (`0.35`) |

**Every other array is a value**, replaced whole by a `set`. That includes an operator's `modes`, a layer's
`takes`, and a song group's `effects`.

**Keys are unique within their array.** Two operators named `saw` in one patch are an error in the patch, as
`PatchFile` already requires of operator names.

### 3.2 Valid paths

- **A path must name a property the schema has**, whether or not the base file writes it. Patch files leave
  defaults out, so `filter.q` is valid on a base that never mentions `q`. An unknown property is an error, so
  `filter.cutof` fails rather than doing nothing.
- **A path through a keyed item that isn't there** is a *dangling* override (§7).

## 4. Operations

They apply in this order: **`remove`, then `insert`, then `set`**, so a `set` can reach into an item this same layer
inserted.

| Operation | Written | Does |
|---|---|---|
| `set` | `{ "<path>": <value> }` | Puts the value there. The value is any JSON the schema allows at that path, and may be a whole object or array, which then replaces everything under the path. Setting a keyed item that doesn't exist (`operators[new]`) adds it at its array's end. |
| `remove` | `[ "<path>" ]` | Takes the property back to its default, as if the file had never written it. On a keyed item, removes the item. |
| `insert` | `[{ "into": "<array path>", "before": "<key>", "item": {…} }]` | Adds a keyed item before the item with that key, or at the end with no `before`. Its key comes from the item. |

**Operator order still matters.** An operator can only be modulated or driven by one before it, so an inserted
modulator goes before what it modulates. The resolved patch is then checked by `Patch.Build` (§5). An order that
breaks the routing rule is an error, which names every layer that changed the order.

**A `set` keeps a path's value pinned**, even when it equals the base's value today. If the base later changes, this
layer keeps its value. Only an explicit **revert** (the studio's) removes a `set`. Editing a field never removes one
by itself.

## 5. Resolution

```
resolve(entry):
    if entry has no base: return entry, checked
    base = resolve(entry.base)          # in the library or song that wrote the reference
    result = base
    apply entry.remove, then entry.insert, then entry.set, to result
    return result, checked
```

- **A chain is resolved all the way down every time.** Nothing is cached in the files.
- **A cycle (`a` based on `b` based on `a`) is an error**, naming the chain. A chain may be at most **16** deep; a
  longer one is an error, since it is almost certainly a mistake.
- **"Checked" means the result is built as format 1 builds an entry**: `PatchSpec` through `Patch.Build`, and a kit
  through `DrumKit.Load`'s rules. Any error is reported against the *top* entry, and names each layer that touched
  the failing field.
- **Kit samples stay relative to the file that names them.** A derived kit that sets a piece's `takes` names samples
  relative to its own folder. The base's samples stay relative to the base's folder.

### 5.1 Provenance

Resolution also returns, for every path in the result, **the layer that last set it**: the base itself, or one of
the layers above. This is what the studio needs to:

- show which fields of a patch are overridden, and at which layer;
- offer **revert**, which removes this layer's `set` or `remove` on that path, after which the value below shows
  through;
- show a dangling override (§7) where it is written.

A whole object or array put by a `set` (`"filter": {…}`) is attributed as one value to the layer that set it. Paths
under it report that layer.

## 6. Song-local overrides

A song can change its sounds for itself alone, with a `"patches"` and a `"kits"` map:

```json
{
  "format": "lunelle-song/3",
  "patches": {
    "lead": { "set": { "filter.cutoff": 4200 } },
    "lunelle:fm-keys": { "set": { "sends.echo": 0.25 } },
    "finale-lead": { "base": "lead", "set": { "operators[saw2].detune": 15 } }
  },
  "tracks": [
    { "name": "lead", "patch": "lead" },
    { "name": "keys", "patch": "lunelle:fm-keys" },
    { "name": "solo", "patch": "finale-lead" }
  ]
}
```

- **An entry in the map with no `base` overrides the entry of the same reference** for this song only. In the
  example, `lead` is the project's `lead` with a brighter filter, and `lunelle:fm-keys` is that library patch with
  more echo. Every track in this song naming `lead` or `lunelle:fm-keys` gets the song's version.
- **An entry with a `base` is a new derived entry, local to the song.** Its key must not be the id of a project
  entry (that would be ambiguous), and it can't carry a library prefix.
- **A song's track references find song-local entries first**, then the project's or library's entries
  (project.md §2.2).
- **Song-local overrides are not visible to any other song.** If two songs want the same change, it belongs in a
  project entry.
- In a project with code, the generator emits song-local patches as private members of the song's generated class.

## 7. Dangling overrides

A path that reaches through a keyed item the base no longer has is **dangling**. That happens when someone renamed
`saw2` to `saw-wide`, or removed `sub`, in a layer below. For example, `operators[saw2].detune` has nothing to apply
to.

- **A dangling override is a warning, never silently dropped.** It is skipped when resolving, and stays in the file.
  If the base gets the key back, it applies again.
- **The studio lists it** on the entry, offering to retarget it to another key or delete it.
- **A `remove` of something already gone** is dangling as well.
- **An unknown property** (§3.2) is not dangling but an error. Dangling is reserved for content that has moved.

## 8. One resolver

**The runtime and the generator must resolve identically**, or a project would play differently with code and
without it. So the resolver is **one implementation**:

- It is written in C# that compiles both for Lunelle's own target and for the generator's netstandard2.0. That means
  it uses no `System.Text.Json` and only the generator's JSON model, or a small JSON model of its own that both share,
  and is included in both as linked source files.
- **Its input is JSON trees, not built objects.** Paths are about files, and resolving before building means one
  code path checks the result (§5).
- **It is pure.** It does no I/O, and is given a function that loads an entry by reference. It has no clock and no
  randomness.

## 9. Errors

| Error | Reported on |
|---|---|
| `base` alongside the entry's own content | The derived file |
| `base` naming a missing entry, or an entry of another kind | The derived file |
| An unknown property in a path, or a value of the wrong type for the path | The path, in its file |
| A key used twice in a keyed array | The file holding the array |
| A cycle in the `base` chain, or a chain deeper than 16 | The top entry, naming the chain |
| The resolved entry failing `Patch.Build` or the kit's checks | The top entry, naming each layer that touched the failing field |
| A song-local derived entry named like a project entry, or with a library prefix | The song |

| Warning | Reported on |
|---|---|
| A dangling path | The path, in its file |

## 10. Test cases

1. **One layer.** `lunelle:saw-lead` with `set filter.cutoff` resolves to the base with only the cutoff changed,
   compared field by field and by an offline render.
2. **Three layers**: library, then project, then song. Each layer's `set` wins over the layers below. Provenance
   reports the right layer for every path.
3. **Base changes show through.** A field set in the base and not overridden above follows a change to the base. A
   field set above doesn't, even when its value equals the base's.
4. **Defaults.** `set filter.q` works on a base whose file never writes `q`. `remove filter.q` on a layer brings back
   the base's value, or the default if the base never wrote it.
5. **Keyed arrays.** Renaming `saw2` in the base makes `operators[saw2].detune` dangling: a warning, skipped, and
   kept in the file. Renaming it back applies it again.
6. **Insert and the routing rule.** Inserting a modulator before its target resolves. Inserting it after is an error
   naming the layer that did it.
7. **Cycles and depth.** `a` based on `b` based on `a` is an error naming both. A chain of 17 is an error.
8. **Song-local.** `lead` overridden in a song changes only that song's tracks. `finale-lead` derived in a song isn't
   visible to another song. A song-local derived entry named like a project patch is an error.
9. **The same in both modes.** Every derived patch in the test projects resolves to the same spec through the
   runtime and through the generator, and they render bit-identically (as `MusicGeneratorTests` checks today for
   format 1).
10. **Kits.** `set pieces[kick].gainDb` and `insert` of a new piece. A derived kit's new `takes` load relative to
    the derived file.

## 11. Out of scope

- Overriding songs, sections or parts. Patterns have their own transforms (patterns.md).
- Merging two bases. Each entry has one.
- Overrides by index, by value or by wildcard (`operators[*].level`).
- Expressions (`"cutoff": "base * 0.5"`). A value is a value.

## Open questions

- Should a song be able to override a *kit's piece patch* by its piece (`pieces[kick].patch` set to another patch)?
  The paths allow it, but is it what people will reach for, or will they derive a whole kit?
- Do patch `macros` need finer keys? Two macros on the same target and operator with different ranges aren't
  possible today, so `<macro>/<target>[/<op>]` should be unique, but that needs checking against `PatchSpec`.
