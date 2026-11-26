using System.Text;

using BinaryVibrance.Lunelle.Chip;
using BinaryVibrance.Lunelle.Format;

namespace BinaryVibrance.Lunelle.Music;

/// <summary>
/// A music project of files alone, with no code, in format 2 (docs/spec/project.md): a <c>lunelle.meta.json</c>, its
/// songs, patches, kits, patterns and cue files by their paths, and the libraries it references, Lunelle's own
/// (<c>lunelle:</c>) among them. Every song is resolved as it loads (<see cref="Resolver"/>): its sounds found, its base
/// chains applied, its patterns written in, its cues imported. Everything is read through an <see cref="IProjectSource"/>,
/// so a folder on disk, a <c>.lunelle</c> file, embedded resources or a browser's storage load alike. What a song names as
/// code (a part, a placement, a hook) has none here: a part or a placement plays a <see cref="Rest"/>, a hook doesn't
/// run, and <see cref="CodeIn"/> says which.
/// </summary>
/// <remarks>
/// An entry's id is its path below its kind's folder (<c>Patches/bass/sub.patch.json</c> is <c>bass/sub</c>), and that
/// is its name here. A file that can't be read or resolved is left out and said in <see cref="Problems"/>, so one broken
/// file doesn't close the rest.
/// </remarks>
public sealed class MusicFolder
{
	/// <summary>The manifest at a project's root.</summary>
	public const string ProjectFileName = EntryIds.Manifest;

	/// <summary>The format the manifest says it is.</summary>
	public const string ProjectFormat = EntryIds.ManifestFormat;

	private readonly Dictionary<string, ResolvedSong> _songs = new(StringComparer.Ordinal);
	private readonly Dictionary<string, CueBase> _cues = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Macro> _macros = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Lazy<DrumKit>> _kitCache = new(StringComparer.Ordinal);
	private readonly List<string> _problems = [];

	private MusicFolder(LoadedProject project)
	{
		Project = project;
		Resolver = new Resolver(project);
		Bindings = new SongBindings
		{
			Patch = name => Find(EntryKind.Patch, name) is { } patch ? BuildPatch(patch) : null,
			Kit = name => Find(EntryKind.Kit, name) is { } kit ? BuildKit(kit, "") : null,
			Cue = name => Resolver.FindCue(project.Root, name, out _) is { } found ? CueFor(found.Cue, found.Owner) : null,
			Macro = name => _macros.TryGetValue(name, out var macro) ? macro : null,
			Part = _ => Rest.Bar,
			Placement = (_, section, track) => section.Play(track, Rest.Bar),
			Hook = (_, _, _) => { },
		};
	}

	/// <summary>The project and its libraries as loaded.</summary>
	public LoadedProject Project { get; }

	/// <summary>The resolver its songs, patches and kits were resolved with.</summary>
	public Resolver Resolver { get; }

	/// <summary>The project's name (its id as a library), from its manifest.</summary>
	public string? Name => Project.Root.Manifest.Name.Length > 0 ? Project.Root.Manifest.Name : null;

	/// <summary>Its title for people: the manifest's <c>"title"</c>, else its name.</summary>
	public string? Title => Project.Root.Manifest.Title ?? Name;

	/// <summary>
	/// The cues its songs can listen to: the project's own, by id, then those its songs import from libraries, by the
	/// name each is known by at run time (<c>game:intensity</c>).
	/// </summary>
	public IReadOnlyList<CueBase> Cues { get; private set; } = [];

	/// <summary>Its macros by the names its songs give them.</summary>
	public IReadOnlyDictionary<string, Macro> Macros => _macros;

	/// <summary>Its songs by id, in the manifest's order and then by id, each built afresh whenever asked.</summary>
	public IReadOnlyList<(string Name, Func<Song> Build)> Songs { get; private set; } = [];

	/// <summary>Its own patches by id, each resolved and built afresh whenever asked.</summary>
	public IReadOnlyList<(string Name, Func<Patch> Build)> Patches { get; private set; } = [];

	/// <summary>Its own kits by id, each loaded once, on first asking (a sampled kit decodes all its takes).</summary>
	public IReadOnlyList<(string Name, Func<DrumKit> Build)> Kits { get; private set; } = [];

	/// <summary>What binds the names a song written in the project's own context uses: its patches, kits, cues and macros, code as rests.</summary>
	public SongBindings Bindings { get; }

	/// <summary>Each problem found loading and resolving it, as a line naming its file.</summary>
	public IReadOnlyList<string> Problems => _problems;

	/// <summary>Each problem found loading and resolving it, with where in its file.</summary>
	public IReadOnlyList<FormatProblem> Diagnostics { get; private set; } = [];

	/// <summary>The song <paramref name="song"/>'s path in the project, or null.</summary>
	public string? SongPath(string song) => Project.Root.Entry(EntryKind.Song, song)?.Path;

	/// <summary>The patch <paramref name="patch"/>'s path in the project, or null (for one in a library too).</summary>
	public string? PatchPath(string patch) => Project.Root.Entry(EntryKind.Patch, patch)?.Path;

	/// <summary>The kit <paramref name="kit"/>'s file's path in the project, or null.</summary>
	public string? KitPath(string kit) => Project.Root.Entry(EntryKind.Kit, kit)?.Path;

	/// <summary>Song <paramref name="song"/> resolved, or null for no such song.</summary>
	public ResolvedSong? Resolved(string song) => _songs.TryGetValue(song, out var resolved) ? resolved : null;

	/// <summary>The cues song <paramref name="song"/> imports, in order, as it plays them: none for no such song.</summary>
	public IReadOnlyList<(ImportedCue Import, CueBase Cue)> CuesOf(string song) =>
		_songs.TryGetValue(song, out var resolved) ? [.. resolved.Cues.Select(c => (c, CueFor(c.Definition, c.Owner)))] : [];

	/// <summary>
	/// What song <paramref name="song"/>'s file names as code, which plays nothing here: <c>"part Drums"</c>,
	/// <c>"placement Fill"</c>, <c>"hook OnBuilt"</c>. Empty for a song that is data throughout, or no song.
	/// </summary>
	public IReadOnlyList<string> CodeIn(string song) => _songs.TryGetValue(song, out var resolved) ? Code(resolved.Json) : [];

	/// <summary>The name its songs give macro <paramref name="id"/>, or null when they give it none.</summary>
	public string? MacroName(int id)
	{
		foreach (var (name, macro) in _macros)
			if (macro.Id == id)
				return name;
		return null;
	}

	/// <summary>The music in the folder at <paramref name="directory"/>, with Lunelle's own library.</summary>
	/// <exception cref="DirectoryNotFoundException">There is no such folder.</exception>
	public static MusicFolder FromDirectory(string directory) => Load(new DirectorySource(directory));

	/// <summary>
	/// The music among <paramref name="files"/> (paths relative to the project's root, <c>/</c> between folders), each read
	/// through <paramref name="read"/> by its path as given, with Lunelle's own library.
	/// </summary>
	public static MusicFolder Load(IEnumerable<string> files, Func<string, byte[]> read) => Load(new MemorySource(files, read));

	/// <summary>The music in <paramref name="source"/>, its host libraries from <paramref name="host"/> (Lunelle's own, by default).</summary>
	public static MusicFolder Load(IProjectSource source, Func<string, IProjectSource?>? host = null)
	{
		ArgumentNullException.ThrowIfNull(source);
		var folder = new MusicFolder(ProjectLoader.Load(source, host ?? BuiltInLibrary.Host));
		folder.Fill();
		return folder;
	}

	private void Fill()
	{
		var root = Project.Root;
		foreach (var (name, number, _) in root.Manifest.Macros)
			_macros[name] = new Macro(number);

		var cues = new List<CueBase>();
		foreach (var cue in root.Cues.Values.OrderBy(c => c.Id, StringComparer.Ordinal))
			cues.Add(CueFor(cue, root));

		var order = root.Manifest.Songs.Where(id => root.Entry(EntryKind.Song, id) is not null).ToList();
		order.AddRange(root.Entries(EntryKind.Song).Keys.Where(id => !order.Contains(id)).Order(StringComparer.Ordinal));
		var songs = new List<(string, Func<Song>)>();
		foreach (var id in order)
		{
			var resolved = Resolver.Song(root.Entry(EntryKind.Song, id)!);
			_songs[id] = resolved;
			foreach (var import in resolved.Cues)
				if (import.Owner != root && !cues.Contains(CueFor(import.Definition, import.Owner)))
					cues.Add(CueFor(import.Definition, import.Owner));
			songs.Add((id, () => BuildSong(resolved)));
		}
		Songs = songs;
		Cues = cues;

		Patches = [.. root.Entries(EntryKind.Patch).Keys.Order(StringComparer.Ordinal)
			.Select(id => (id, (Func<Patch>)(() => BuildPatch(Find(EntryKind.Patch, id) ?? throw new FormatException($"The patch '{id}' can't be resolved.")))))];
		Kits = [.. root.Entries(EntryKind.Kit).Keys.Order(StringComparer.Ordinal)
			.Select(id => (id, (Func<DrumKit>)(() => BuildKit(Find(EntryKind.Kit, id) ?? throw new FormatException($"The kit '{id}' can't be resolved."), ""))))];

		// Resolve every entry of the project's own once, so each problem is said as it loads
		foreach (var kind in new[] { EntryKind.Patch, EntryKind.Kit })
			foreach (var entry in root.Entries(kind).Values)
				Resolver.Resolve(entry);
		foreach (var pattern in root.Entries(EntryKind.Pattern).Values)
			Resolver.Pattern(pattern);

		var all = Project.Problems.Concat(Resolver.Problems).ToList();
		Diagnostics = all;
		_problems.AddRange(all.Select(p => p.ToString()));
	}

	// A patch or kit a reference names in the project's own context, resolved; null when there is none or it failed
	private ResolvedEntry? Find(EntryKind kind, string reference) =>
		Resolver.Find(Project.Root, kind, reference, out _) is { } file ? Resolver.Resolve(file) : null;

	private Song BuildSong(ResolvedSong resolved)
	{
		var song = resolved.File.Id;
		var bindings = new SongBindings
		{
			Patch = key => resolved.Patches.TryGetValue(key, out var patch) ? BuildPatch(patch) : null,
			Kit = key => resolved.Kits.TryGetValue(key, out var kit) ? BuildKit(kit, key.StartsWith('~') ? song : "") : null,
			Cue = reference =>
			{
				if (resolved.Cue(reference) is { } import)
					return CueFor(import.Definition, import.Owner);
				if (Resolver.FindCue(resolved.File.Owner, reference, out _) is not null)
					throw new FormatException($"Song '{song}' reads the cue '{reference}', which it doesn't import: add it to the song's \"cues\".");
				return null;
			},
			Macro = name => _macros.TryGetValue(name, out var macro) ? macro : null,
			Part = _ => Rest.Bar,
			Placement = (_, section, track) => section.Play(track, Rest.Bar),
			Hook = (_, _, _) => { },
		};
		return SongFile.Load(resolved.Json.ToString(), bindings);
	}

	private static Patch BuildPatch(ResolvedEntry patch)
	{
		if (!patch.Ok)
			throw new FormatException($"The patch '{patch.Key}' can't be built: its file has errors.");
		try
		{
			return PatchFile.Load(patch.Json.ToString());
		}
		catch (Exception ex) when (ex is FormatException or PatchException && patch.Layers.Count > 1)
		{
			// A derived patch's error may come from any layer: name them all (overrides.md §5)
			throw new FormatException($"The patch '{patch.Key}', made of {string.Join(" → ", patch.Layers.Select(l => l.Key))}, can't be built: {ex.Message}", ex);
		}
	}

	// A kit, loaded once per key (a song's own kit once per song), its takes read from wherever each layer named them
	private DrumKit BuildKit(ResolvedEntry kit, string song)
	{
		if (!kit.Ok)
			throw new FormatException($"The kit '{kit.Key}' can't be built: its file has errors.");
		var cacheKey = song + "|" + kit.Key;
		if (!_kitCache.TryGetValue(cacheKey, out var made))
		{
			var manifest = Encoding.UTF8.GetBytes(kit.Json.ToString());
			made = new Lazy<DrumKit>(() => DrumKit.Load(path =>
			{
				if (path == "kit.json")
					return manifest;
				var (library, inside) = Resolver.Unlocate(path);
				return (Resolver.Library(library) ?? throw new KeyNotFoundException($"No library holds {path}.")).Source.ReadBytes(inside);
			}, "kit.json", key => kit.Patches.TryGetValue(key, out var patch) ? BuildPatch(patch) : null));
			_kitCache[cacheKey] = made;
		}
		return made.Value;
	}

	// One runtime cue for each name: a library's cue reached by two routes is one cue
	private CueBase CueFor(CueDefinition definition, ProjectLibrary owner)
	{
		var name = owner.RuntimeCueName(definition.Id);
		if (_cues.TryGetValue(name, out var cue))
			return cue;
		cue = definition.IsChoice
			? new ChoiceCue(name, [.. definition.Options.Select(o => o.Name)], definition.Options[definition.InitialOption].Name)
			: new Cue(name, (float)definition.InitialLevel);
		_cues[name] = cue;
		return cue;
	}

	// What a song names as code: each part, placement and hook
	private static List<string> Code(JObject song)
	{
		var code = new List<string>();
		void Part(JNode part)
		{
			if (part["part"]?["code"]?.Str is { } name)
				code.Add($"part {name}");
			if (part["endWith"] is { } fill)
				Part(fill);
			foreach (var item in part["sequence"]?.Items ?? [])
				Part(item);
		}
		foreach (var section in song["sections"]?.Items ?? [])
			foreach (var part in section["parts"]?.Items ?? [])
			{
				if (part["code"]?.Str is { } placement)
					code.Add($"placement {placement}");
				else
					Part(part);
			}
		foreach (var hook in song["hooks"]?.Items ?? [])
			if (hook.Str is { } name)
				code.Add($"hook {name}");
		return [.. code.Distinct(StringComparer.Ordinal)];
	}
}
