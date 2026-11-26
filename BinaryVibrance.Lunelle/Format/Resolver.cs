using System.Globalization;

#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>
/// Format 2's one resolver (overrides.md §8), compiled into Lunelle and into its generator alike, so a project plays the
/// same with code and without: it finds what a reference names (project.md §2.2), resolves a patch or kit's base chain
/// (overrides.md §5), and resolves a song: its own patches and kits (§6), the cues it imports (cues.md §3), and the
/// patterns its parts play, written in (patterns.md §3). It reads nothing but what the loaded project holds.
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class Resolver
{
	/// <summary>How deep a chain of bases, or of patterns in patterns, may go.</summary>
	public const int MaxDepth = 16;

	private static readonly string[] Transforms = { "velocity", "pieces", "octave", "overChords", "voicing", "spread" };

	private readonly Dictionary<string, ResolvedEntry?> _entries = new(StringComparer.Ordinal);
	private readonly Dictionary<string, PatternContent?> _patterns = new(StringComparer.Ordinal);

	/// <summary>A resolver over <paramref name="project"/>.</summary>
	public Resolver(LoadedProject project) => Project = project;

	/// <summary>The project it resolves in.</summary>
	public LoadedProject Project { get; }

	/// <summary>What resolving found wrong, each against the file it is in.</summary>
	public List<FormatProblem> Problems { get; } = new();

	/// <summary>A sample's located path: its library's key (<see cref="ProjectLibrary.Key"/>), <c>//</c>, its path there.</summary>
	public static string Locate(string libraryKey, string path) => libraryKey + "//" + path;

	/// <summary>A located path's library key and path.</summary>
	public static (string Library, string Path) Unlocate(string located)
	{
		var at = located.IndexOf("//", StringComparison.Ordinal);
		return at < 0 ? ("", located) : (located.Substring(0, at), located.Substring(at + 2));
	}

	/// <summary>The library with <paramref name="key"/> (<see cref="ProjectLibrary.Key"/>), or null.</summary>
	public ProjectLibrary? Library(string key) => Project.All.FirstOrDefault(l => l.Key == key);

	/// <summary>
	/// The entry of <paramref name="kind"/> that <paramref name="reference"/> names, as written in <paramref name="from"/>;
	/// or null, with why in <paramref name="why"/>.
	/// </summary>
	public EntryFile? Find(ProjectLibrary from, EntryKind kind, string reference, out string? why)
	{
		why = null;
		if (EntryIds.Reference(reference) is not { } parsed)
		{
			why = $"\"{reference}\" isn't a reference to a {EntryIds.Noun(kind)} (\"lead\", \"lunelle:keys/piano\")";
			return null;
		}
		var library = from;
		if (parsed.Library is { } name)
		{
			if (!from.Libraries.TryGetValue(name, out var found))
			{
				why = $"\"{reference}\" names the library '{name}', which {(from.Key.Length == 0 ? "the project" : $"'{from.Key}'")} doesn't list in its \"libraries\"";
				return null;
			}
			if (found is null)
			{
				why = $"\"{reference}\" is in the library '{name}', which couldn't be loaded";
				return null;
			}
			library = found;
		}
		var entry = library.Entry(kind, parsed.Id);
		if (entry is null)
			why = $"no {EntryIds.Noun(kind)} is '{reference}'";
		return entry;
	}

	/// <summary>The cue <paramref name="reference"/> names, as written in <paramref name="from"/>, and its library; or null with why.</summary>
	public (CueDefinition Cue, ProjectLibrary Owner)? FindCue(ProjectLibrary from, string reference, out string? why)
	{
		why = null;
		var colon = reference.IndexOf(':');
		var library = from;
		var id = reference;
		if (colon >= 0)
		{
			var name = reference.Substring(0, colon);
			id = reference.Substring(colon + 1);
			if (!from.Libraries.TryGetValue(name, out var found) || found is null)
			{
				why = $"the cue \"{reference}\" is in the library '{name}', which {(found is null && from.Libraries.ContainsKey(name) ? "couldn't be loaded" : "isn't listed in \"libraries\"")}";
				return null;
			}
			library = found;
		}
		if (library.Cues.TryGetValue(id, out var cue))
			return (cue, library);
		why = $"no cue is '{reference}'";
		return null;
	}

	/// <summary>The patch or kit <paramref name="entry"/> holds, resolved; null when it can't be (said in <see cref="Problems"/>).</summary>
	public ResolvedEntry? Resolve(EntryFile entry) => Resolve(entry, new List<string>(), entry);

	private ResolvedEntry? Resolve(EntryFile entry, List<string> chain, EntryFile top)
	{
		var key = entry.Key;
		if (_entries.TryGetValue(key, out var done))
			return done;
		if (chain.Contains(key))
		{
			Problems.Add(new FormatProblem(top.Label, $"its \"base\" leads round in a circle: {string.Join(" → ", chain.Concat(new[] { key }))}",
				top.Json["base"]?.Start ?? 0, top.Json["base"]?.Length ?? 0));
			return null;
		}
		if (chain.Count >= MaxDepth)
		{
			Problems.Add(new FormatProblem(top.Label, $"its chain of bases is more than {MaxDepth} deep: {string.Join(" → ", chain)}"));
			return null;
		}
		chain.Add(key);
		var schema = OverrideSchema.For(entry.Kind)!;
		var patches = new Dictionary<string, ResolvedEntry>(StringComparer.Ordinal);
		JObject result;
		List<EntryFile> layers;
		Dictionary<string, string> provenance;
		var ok = true;
		if (entry.Json["base"] is not { } baseNode)
		{
			result = (JObject)Prepare(entry.Json.Clone(), null, entry, patches, ref ok);
			layers = new List<EntryFile> { entry };
			provenance = new Dictionary<string, string>(StringComparer.Ordinal);
		}
		else
		{
			foreach (var member in entry.Json.Members)
				if (System.Array.IndexOf(Overrides.DerivedKeys, member.Key) < 0)
				{
					Problems.Add(new FormatProblem(entry.Label,
						$"a file with a \"base\" says only what it changes (\"set\", \"remove\", \"insert\"); \"{member.Key}\" belongs in a whole {EntryIds.Noun(entry.Kind)}",
						member.KeyStart, member.Key.Length + 2));
					ok = false;
				}
			string? why = null;
			var below = baseNode.Str is { } baseRef ? Find(entry.Owner, entry.Kind, baseRef, out why) : null;
			if (below is null)
			{
				Problems.Add(new FormatProblem(entry.Label, why ?? "\"base\" names a " + EntryIds.Noun(entry.Kind), baseNode.Start, baseNode.Length));
				chain.RemoveAt(chain.Count - 1);
				_entries[key] = null;
				return null;
			}
			var resolvedBase = Resolve(below, chain, top);
			if (resolvedBase is null)
			{
				chain.RemoveAt(chain.Count - 1);
				_entries[key] = null;
				return null;
			}
			layers = new List<EntryFile>(resolvedBase.Layers) { entry };
			// Counted from the layers, not the calls: a base resolved before is a whole chain all the same
			if (layers.Count - 1 > MaxDepth)
			{
				Problems.Add(new FormatProblem(entry.Label, $"its chain of bases is more than {MaxDepth} deep: {string.Join(" → ", layers.Select(l => l.Key))}"));
				chain.RemoveAt(chain.Count - 1);
				_entries[key] = null;
				return null;
			}
			result = (JObject)resolvedBase.Json.Clone();
			provenance = new Dictionary<string, string>(resolvedBase.Provenance, StringComparer.Ordinal);
			foreach (var pair in resolvedBase.Patches)
				patches[pair.Key] = pair.Value;
			var layer = PrepareLayer(entry.Json, entry, patches, ref ok);
			var before = Problems.Count;
			Overrides.Apply(result, layer, schema, entry.Label, key, Problems, provenance);
			ok &= Problems.Skip(before).All(p => p.IsWarning);
		}
		Finish(result, entry);
		var errors = Problems.Count;
		Overrides.CheckKeys(result, schema, entry.Label, Problems);
		ok &= Problems.Count == errors;
		var resolved = new ResolvedEntry(key, entry, result, layers, provenance) { Ok = ok };
		foreach (var pair in patches)
			resolved.Patches[pair.Key] = pair.Value;
		chain.RemoveAt(chain.Count - 1);
		_entries[key] = resolved;
		return resolved;
	}

	// A whole entry as a file of its kind holds it: its own format, its id as its "name", and nothing of a derived file's
	private static void Finish(JObject result, EntryFile top)
	{
		foreach (var key in new[] { "base", "set", "remove", "insert" })
			result.Remove(key);
		result.Set("format", new JString(ProjectLoader.Format(top.Kind)));
		if (top.Json["about"] is { } about)
			result.Set("about", about.Clone());
		if (top.Json["title"] is { } title)
			result.Set("title", title.Clone());
		result.Set("name", new JString(top.Id));
	}

	// A derived layer with every sample it names located and every patch canonical: its set, remove and insert
	private JObject PrepareLayer(JObject layer, EntryFile entry, Dictionary<string, ResolvedEntry> patches, ref bool ok)
	{
		var prepared = (JObject)layer.Clone();
		if (entry.Kind != EntryKind.Kit)
			return prepared;
		if (prepared["set"] is JObject sets)
			foreach (var member in sets.Members)
			{
				var last = member.Key.LastIndexOf('.');
				var property = last < 0 ? member.Key : member.Key.Substring(last + 1);
				var bracket = property.IndexOf('[');
				member.Value = Prepare(member.Value, bracket < 0 ? property : property.Substring(0, bracket), entry, patches, ref ok);
			}
		foreach (var insert in prepared["insert"]?.Items ?? System.Array.Empty<JNode>())
			if (insert is JObject obj && obj["item"] is { } item)
				obj.Set("item", Prepare(item, null, entry, patches, ref ok));
		return prepared;
	}

	// A kit's JSON (or a part of it under `property`) with its takes located and its pieces' patches canonical
	private JNode Prepare(JNode node, string? property, EntryFile entry, Dictionary<string, ResolvedEntry> patches, ref bool ok)
	{
		if (entry.Kind != EntryKind.Kit)
			return node;
		if (property == "takes" && node is JArray takes)
		{
			for (var i = 0; i < takes.Values.Count; i++)
				if (takes.Values[i].Str is { } take)
				{
					if (Normalize(entry.Folder + take) is { } path)
						takes.Values[i] = new JString(Locate(entry.Owner.Key, path)).At<JString>(takes.Values[i]);
					else
					{
						Problems.Add(new FormatProblem(entry.Label, $"the sample \"{take}\" is outside the project", takes.Values[i].Start, takes.Values[i].Length));
						ok = false;
					}
				}
			return node;
		}
		if (property == "patch" && node.Str is { } reference)
		{
			var patch = Find(entry.Owner, EntryKind.Patch, reference, out var why);
			var resolved = patch is null ? null : Resolve(patch);
			if (resolved is null)
			{
				Problems.Add(new FormatProblem(entry.Label, why ?? $"the patch '{reference}' can't be resolved", node.Start, node.Length));
				ok = false;
				return node;
			}
			patches[resolved.Key] = resolved;
			return new JString(resolved.Key).At<JString>(node);
		}
		if (node is JObject obj)
			foreach (var member in obj.Members)
				member.Value = Prepare(member.Value, member.Key, entry, patches, ref ok);
		else if (node is JArray array)
			for (var i = 0; i < array.Values.Count; i++)
				array.Values[i] = Prepare(array.Values[i], null, entry, patches, ref ok);
		return node;
	}

	/// <summary><paramref name="path"/> with its <c>.</c> and <c>..</c> taken out, or null when it leaves the root.</summary>
	public static string? Normalize(string path)
	{
		if (path.StartsWith("/", StringComparison.Ordinal) || path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0)
			return null;
		var parts = new List<string>();
		foreach (var part in path.Split('/'))
		{
			if (part.Length == 0 || part == ".")
				continue;
			if (part == "..")
			{
				if (parts.Count == 0)
					return null;
				parts.RemoveAt(parts.Count - 1);
			}
			else
				parts.Add(part);
		}
		return string.Join("/", parts);
	}

	/// <summary>
	/// <paramref name="song"/> resolved: its tracks' patches and kits (its own first), its imported cues, its parts'
	/// patterns written in. What is wrong is said in <see cref="Problems"/>; a track whose sound can't be resolved names
	/// nothing (its song then fails to build), a part whose pattern can't be is left out.
	/// </summary>
	public ResolvedSong Song(EntryFile song)
	{
		var json = (JObject)song.Json.Clone();
		var resolved = new ResolvedSong(song, json);
		void Problem(string message, JNode? at, bool warning = false) =>
			Problems.Add(new FormatProblem(song.Label, message, at?.Start ?? 0, at?.Length ?? 0, warning));

		var local = new Dictionary<string, ResolvedEntry?>(StringComparer.Ordinal);
		var localChain = new List<string>();
		ResolvedEntry? Sound(EntryKind kind, string reference, JNode at)
		{
			var map = json[kind == EntryKind.Patch ? "patches" : "kits"] as JObject;
			var localKey = (kind == EntryKind.Patch ? "patch " : "kit ") + reference;
			if (map?.Get(reference) is JObject layer && !localChain.Contains(localKey))
			{
				if (local.TryGetValue(localKey, out var made))
					return made;
				localChain.Add(localKey);
				made = Local(kind, reference, layer);
				localChain.RemoveAt(localChain.Count - 1);
				local[localKey] = made;
				return made;
			}
			var file = Find(song.Owner, kind, reference, out var why);
			if (file is null)
			{
				Problem(why!, at);
				return null;
			}
			return Resolve(file);
		}

		// A song's own sound (overrides.md §6): over the entry of the same reference, or derived from a base of its own
		ResolvedEntry? Local(EntryKind kind, string reference, JObject layer)
		{
			foreach (var member in layer.Members)
				if (System.Array.IndexOf(Overrides.DerivedKeys, member.Key) < 0 || member.Key == "format")
				{
					Problem($"the song's own {EntryIds.Noun(kind)} '{reference}' says only what it changes (\"base\", \"set\", \"remove\", \"insert\"), not \"{member.Key}\"",
						new JString(member.Key) { Start = member.KeyStart, Length = member.Key.Length + 2 });
					return null;
				}
			ResolvedEntry? below;
			if (layer["base"] is { } baseNode)
			{
				if (reference.IndexOf(':') >= 0 || song.Owner.Entry(kind, reference) is not null)
				{
					Problem($"the song's own {EntryIds.Noun(kind)} '{reference}' has a \"base\", so it is a new {EntryIds.Noun(kind)}: name it apart from the project's {EntryIds.Noun(kind)}s, with no library", layer);
					return null;
				}
				if (baseNode.Str is not { } baseRef)
				{
					Problem("\"base\" names a " + EntryIds.Noun(kind), baseNode);
					return null;
				}
				below = Sound(kind, baseRef, baseNode);
			}
			else
				below = Sound(kind, reference, layer);
			if (below is null)
				return null;
			var result = (JObject)below.Json.Clone();
			var provenance = new Dictionary<string, string>(below.Provenance, StringComparer.Ordinal);
			var patches = new Dictionary<string, ResolvedEntry>(below.Patches, StringComparer.Ordinal);
			var ok = below.Ok;
			var asEntry = new EntryFile(song.Owner, kind, song.Id, song.Path, layer);
			var prepared = PrepareLayer(layer, asEntry, patches, ref ok);
			var before = Problems.Count;
			Overrides.Apply(result, prepared, OverrideSchema.For(kind)!, song.Label, "~" + reference, Problems, provenance);
			ok &= Problems.Skip(before).All(p => p.IsWarning);
			result.Set("name", new JString(reference));
			var entry = new ResolvedEntry("~" + reference, below.File, result, below.Layers, provenance) { Ok = ok };
			foreach (var pair in patches)
				entry.Patches[pair.Key] = pair.Value;
			return entry;
		}

		var kitOf = new Dictionary<string, ResolvedEntry?>(StringComparer.Ordinal);
		foreach (var track in json["tracks"]?.Items ?? System.Array.Empty<JNode>())
		{
			if (track is not JObject trackObject)
				continue;
			var name = track["name"]?.Str ?? "";
			if (track["patch"] is { } patchNode && patchNode.Str is { } patchRef)
			{
				var patch = Sound(EntryKind.Patch, patchRef, patchNode);
				if (patch is not null)
				{
					resolved.Patches[patch.Key] = patch;
					trackObject.Set("patch", new JString(patch.Key).At<JString>(patchNode));
				}
			}
			else if (track["kit"] is { } kitNode && kitNode.Str is { } kitRef)
			{
				var kit = Sound(EntryKind.Kit, kitRef, kitNode);
				kitOf[name] = kit;
				if (kit is not null)
				{
					resolved.Kits[kit.Key] = kit;
					foreach (var pair in kit.Patches)
						resolved.Patches[pair.Key] = pair.Value;
					trackObject.Set("kit", new JString(kit.Key).At<JString>(kitNode));
				}
			}
		}
		json.Remove("patches");
		json.Remove("kits");

		if (json["cues"] is { } cuesNode)
		{
			if (cuesNode is not JObject cues)
				Problem("\"cues\" imports cues by id, each with the value to preview it at: { \"intensity\": 0.6 }", cuesNode);
			else
				foreach (var member in cues.Members)
				{
					var keyNode = new JString(member.Key) { Start = member.KeyStart, Length = member.Key.Length + 2 };
					if (FindCue(song.Owner, member.Key, out var why) is not { } found)
					{
						Problem(why!, keyNode);
						continue;
					}
					var value = member.Value;
					if (value is JNull)
						resolved.Cues.Add(new ImportedCue(member.Key, found.Cue, found.Owner, null));
					else if (found.Cue.IsChoice)
					{
						if (value.Str is { } option && found.Cue.OptionIndex(option) >= 0)
							resolved.Cues.Add(new ImportedCue(member.Key, found.Cue, found.Owner, value));
						else
							Problem($"the cue '{member.Key}' is previewed at one of its options ({string.Join(", ", found.Cue.Options.Select(o => o.Name))}), or null", value);
					}
					else if (value is JNumber number && number.Value >= 0 && number.Value <= 1)
						resolved.Cues.Add(new ImportedCue(member.Key, found.Cue, found.Owner, value));
					else
						Problem($"the cue '{member.Key}' is a level, previewed from 0 to 1, or null", value);
				}
		}

		// Each part playing a pattern takes the pattern's content in its place (patterns.md §3)
		var tracks = new Dictionary<string, JNode>(StringComparer.Ordinal);
		foreach (var track in json["tracks"]?.Items ?? System.Array.Empty<JNode>())
			if (track["name"]?.Str is { } trackName)
				tracks[trackName] = track;
		var meter = json["meter"]?.Str ?? "4/4";
		foreach (var section in json["sections"]?.Items ?? System.Array.Empty<JNode>())
		{
			if (section["parts"] is not JArray parts)
				continue;
			for (var i = parts.Values.Count - 1; i >= 0; i--)
			{
				if (parts.Values[i] is not JObject part)
					continue;
				var trackName = part["track"]?.Str;
				var track = trackName is not null && tracks.TryGetValue(trackName, out var t) ? t : null;
				var kit = trackName is not null && kitOf.TryGetValue(trackName, out var k) ? k : null;
				if (!WriteIn(part, track, kit, meter, song, true))
					parts.Values.RemoveAt(i);
				else if (part["endWith"] is JObject fill && !WriteIn(fill, track, kit, meter, song, false))
					part.Remove("endWith");
			}
		}
		json.Set("name", new JString(song.Id));
		return resolved;
	}

	// Writes a pattern a part plays into it, with its transforms; false when it can't be (said), so the part is left out
	private bool WriteIn(JObject part, JNode? track, ResolvedEntry? kit, string meter, EntryFile song, bool placed)
	{
		if (part["pattern"] is not { } patternNode)
			return true;
		void Problem(string message, JNode? at, bool warning = false) =>
			Problems.Add(new FormatProblem(song.Label, message, at?.Start ?? 0, at?.Length ?? 0, warning));
		foreach (var kind in new[] { "line", "beat", "strum", "notes", "rows", "sequence", "part" })
			if (part[kind] is not null)
			{
				Problem($"a part plays a \"pattern\" or its own \"{kind}\", not both", part);
				return false;
			}
		string? why = null;
		var file = patternNode.Str is { } reference ? Find(song.Owner, EntryKind.Pattern, reference, out why) : null;
		if (file is null)
		{
			Problem(why is null ? "\"pattern\" names a pattern" : $"{why}: the part plays nothing", patternNode);
			return false;
		}
		var content = Pattern(file, new List<string>());
		if (content is null)
		{
			Problem($"the pattern '{patternNode.Str}' can't be played: the part plays nothing", patternNode);
			return false;
		}
		var onKit = track?["kit"] is not null;
		if (track is not null && onKit != (content.Kind == "beat"))
		{
			Problem($"the pattern '{patternNode.Str}' is a {content.Kind}, which a {(onKit ? "kit" : "patch")} track can't play", patternNode);
			return false;
		}
		if (content.Meter != meter)
		{
			Problem($"the pattern '{patternNode.Str}' is in {content.Meter}, and the song is in {meter}", patternNode);
			return false;
		}
		var written = (JObject)content.Part.Clone();
		var transforms = new JObject();
		foreach (var name in Transforms)
			if (part[name] is { } value)
			{
				transforms.Set(name, value);
				part.Remove(name);
			}
		Transform(written, transforms, content.Kind, song, patternNode.Str!);
		part.Remove("pattern");
		foreach (var member in written.Members)
			part.Set(member.Key, member.Value);
		if (onKit && kit is not null)
		{
			var pieces = new HashSet<string>(kit.Json["pieces"]?.Items.Select(p => p["piece"]?.Str ?? "") ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
			foreach (var row in Rows(written))
				if (!pieces.Contains(row))
					Problem($"the pattern '{patternNode.Str}' plays '{row}', which the kit '{kit.File.Id}' hasn't: that row is silent here", patternNode, warning: true);
		}
		return true;
	}

	private static IEnumerable<string> Rows(JNode content)
	{
		if (content["rows"] is JObject rows)
			foreach (var member in rows.Members)
				yield return member.Key;
		foreach (var item in content["sequence"]?.Items ?? System.Array.Empty<JNode>())
			foreach (var row in Rows(item))
				yield return row;
	}

	// A reference's transforms applied to a pattern's content as written in (patterns.md §4)
	private void Transform(JObject content, JObject transforms, string kind, EntryFile song, string pattern)
	{
		void Problem(string message, JNode? at, bool warning = false) =>
			Problems.Add(new FormatProblem(song.Label, message, at?.Start ?? 0, at?.Length ?? 0, warning));
		foreach (var member in transforms.Members)
		{
			var allowed = member.Key switch
			{
				"velocity" => true,
				"pieces" => kind == "beat",
				"octave" or "overChords" => kind == "line",
				_ => kind == "strum",
			};
			if (!allowed)
				Problem($"\"{member.Key}\" doesn't change a {kind}", member.Value);
		}
		if (transforms["velocity"] is { } velocity)
		{
			if (velocity is not JNumber scale || scale.Value < 0 || scale.Value > 2)
				Problem("\"velocity\" scales the pattern's velocities, from 0 to 2", velocity);
			else
			{
				var current = content["velocity"] is JNumber had ? had.Value : 1.0;
				content.Set("velocity", new JNumber(current * scale.Value).At<JNumber>(velocity));
			}
		}
		if (transforms["pieces"] is JObject map && kind == "beat")
			Remap(content, map, pattern, Problem);
		foreach (var key in new[] { "octave", "overChords", "voicing", "spread" })
			if (transforms[key] is { } value)
				SetOnLeaves(content, key, value);
	}

	private static void Remap(JObject content, JObject map, string pattern, Action<string, JNode?, bool> problem)
	{
		var rows = Rows(content).ToList();
		foreach (var member in map.Members)
			if (!rows.Contains(member.Key))
				problem($"\"pieces\" maps '{member.Key}', which the pattern '{pattern}' doesn't play", member.Value, true);
		Rename(content, map);
	}

	private static void Rename(JObject content, JObject map)
	{
		if (content["rows"] is JObject rows)
		{
			var renamed = new JObject().At<JObject>(rows);
			foreach (var member in rows.Members)
			{
				var to = map.Get(member.Key);
				if (to is JNull)
					continue;
				renamed.Set(to?.Str ?? member.Key, member.Value);
			}
			content.Set("rows", renamed);
		}
		foreach (var item in content["sequence"]?.Items ?? System.Array.Empty<JNode>())
			if (item is JObject obj)
				Rename(obj, map);
	}

	private static void SetOnLeaves(JObject content, string key, JNode value)
	{
		if (content["sequence"] is JArray items)
		{
			foreach (var item in items.Values)
				if (item is JObject obj)
					SetOnLeaves(obj, key, value);
		}
		else
			content.Set(key, value.Clone());
	}

	/// <summary>A pattern's content as a part would write it in (patterns.md §2), or null when it can't be (said).</summary>
	public PatternContent? Pattern(EntryFile pattern) => Pattern(pattern, new List<string>());

	private PatternContent? Pattern(EntryFile pattern, List<string> chain)
	{
		var key = pattern.Key;
		if (_patterns.TryGetValue(key, out var done))
			return done;
		void Problem(string message, JNode? at) =>
			Problems.Add(new FormatProblem(pattern.Label, message, at?.Start ?? 0, at?.Length ?? 0));
		if (chain.Contains(key) || chain.Count >= MaxDepth)
		{
			Problem(chain.Contains(key)
				? $"its patterns lead round in a circle: {string.Join(" → ", chain.Concat(new[] { key }))}"
				: $"its patterns are nested more than {MaxDepth} deep", pattern.Json["sequence"]);
			return null;
		}
		chain.Add(key);
		var json = pattern.Json;
		var kind = json["kind"]?.Str;
		var meter = json["meter"]?.Str ?? "4/4";
		PatternContent? made = null;
		if (kind is not ("beat" or "line" or "strum"))
			Problem("a pattern's \"kind\" is \"beat\", \"line\" or \"strum\"", json["kind"] ?? json);
		else if (json["sequence"] is { } sequence)
		{
			foreach (var own in new[] { "step", "rows", "notes" })
				if (json[own] is not null)
					Problem($"a pattern has a \"sequence\" or content of its own (\"{own}\"), not both", json[own]);
			var items = new JArray().At<JArray>(sequence);
			var ok = true;
			foreach (var item in sequence.Items)
			{
				var sub = item["pattern"]?.Str is { } subRef ? Find(pattern.Owner, EntryKind.Pattern, subRef, out var why) : null;
				var content = sub is null ? null : Pattern(sub, chain);
				if (content is null)
				{
					if (sub is null)
						Problem(item["pattern"]?.Str is null ? "a sequence's item names a \"pattern\"" : $"{item["pattern"]!.Str}: no pattern is called that", item);
					ok = false;
					continue;
				}
				if (content.Kind != kind || content.Meter != meter)
				{
					Problem($"the pattern '{item["pattern"]!.Str}' is a {content.Kind} in {content.Meter}; this sequence is a {kind} in {meter}", item);
					ok = false;
					continue;
				}
				var written = (JObject)content.Part.Clone();
				var transforms = new JObject();
				foreach (var name in Transforms)
					if (item[name] is { } value)
						transforms.Set(name, value);
				Transform(written, transforms, kind!, pattern, item["pattern"]!.Str!);
				if (item["times"] is { } times)
				{
					if (times.Num is not { } count || !int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1)
					{
						Problem("\"times\" is a whole number, 1 or more", times);
						ok = false;
						continue;
					}
					if (n > 1)
						written.Set("times", times.Clone());
				}
				items.Values.Add(written);
			}
			if (ok && items.Values.Count > 0)
				made = new PatternContent(kind!, meter, new JObject().Set("sequence", items));
			else if (ok)
				Problem("a sequence lists patterns to play", sequence);
		}
		else
		{
			var content = new JObject();
			if (json["step"]?.Str is not { } step)
				Problem("a pattern has a \"step\" (\"eighths\")", json);
			else if (kind == "beat")
			{
				if (json["rows"] is JObject rows)
					made = new PatternContent(kind, meter, content.Set("beat", new JString(step)).Set("rows", rows.Clone()));
				else
					Problem("a beat has \"rows\", one a kit piece: { \"kick\": \"x... x...\" }", json);
			}
			else if (json["notes"]?.Str is null)
				Problem($"a {kind} has \"notes\"", json);
			else
			{
				content.Set(kind!, new JString(step)).Set("notes", json["notes"]!.Clone());
				foreach (var option in kind == "line" ? new[] { "overChords", "octave" } : new[] { "voicing", "spread" })
					if (json[option] is { } value)
						content.Set(option, value.Clone());
				made = new PatternContent(kind!, meter, content);
			}
		}
		chain.RemoveAt(chain.Count - 1);
		_patterns[key] = made;
		return made;
	}
}

/// <summary>A pattern's content as a part writes it in: its kind, its meter, and the part's keys (or a <c>"sequence"</c> of them).</summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class PatternContent
{
	/// <summary>A pattern's content.</summary>
	public PatternContent(string kind, string meter, JObject part)
	{
		Kind = kind;
		Meter = meter;
		Part = part;
	}

	/// <summary>"beat", "line" or "strum".</summary>
	public string Kind { get; }

	/// <summary>The meter its bars are counted in.</summary>
	public string Meter { get; }

	/// <summary>The keys a part takes for it.</summary>
	public JObject Part { get; }
}
