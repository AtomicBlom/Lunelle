#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>
/// A patch or kit resolved (overrides.md §5): its base chain applied all the way down, every reference it makes
/// rewritten to a canonical key (<see cref="ProjectLibrary.CanonicalKey"/>), a kit's samples to located paths
/// (<see cref="Resolver.Locate"/>), and which layer last set each path.
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class ResolvedEntry
{
	/// <summary>A resolved entry.</summary>
	public ResolvedEntry(string key, EntryFile file, JObject json, IReadOnlyList<EntryFile> layers, Dictionary<string, string> provenance)
	{
		Key = key;
		File = file;
		Json = json;
		Layers = layers;
		Provenance = provenance;
	}

	/// <summary>Its canonical key: <c>lead</c>, <c>lunelle:keys/piano</c>, or <c>~lead</c> for a song's own.</summary>
	public string Key { get; }

	/// <summary>The top layer's file: the entry asked for.</summary>
	public EntryFile File { get; }

	/// <summary>The result, as a whole file of its kind would hold it, with <c>"name"</c> its id.</summary>
	public JObject Json { get; }

	/// <summary>Its layers, the whole one at the bottom first.</summary>
	public IReadOnlyList<EntryFile> Layers { get; }

	/// <summary>For each path a layer above the bottom changed, the canonical key of the layer that last did.</summary>
	public Dictionary<string, string> Provenance { get; }

	/// <summary>The patches a kit's pieces play, by canonical key.</summary>
	public Dictionary<string, ResolvedEntry> Patches { get; } = new(StringComparer.Ordinal);

	/// <summary>Whether it was resolved without errors: one with errors isn't played.</summary>
	public bool Ok { get; set; } = true;
}

/// <summary>
/// A song resolved: its JSON with every patch and kit its tracks play rewritten to a canonical key, every pattern a part
/// plays written in, and <c>"name"</c> its id; the patches and kits by those keys; and the cues it imports, in order.
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class ResolvedSong
{
	/// <summary>A resolved song.</summary>
	public ResolvedSong(EntryFile file, JObject json)
	{
		File = file;
		Json = json;
	}

	/// <summary>Its file.</summary>
	public EntryFile File { get; }

	/// <summary>The song as <c>SongFile</c> reads it.</summary>
	public JObject Json { get; }

	/// <summary>The patches it plays (its tracks' and its kits' pieces'), by canonical key.</summary>
	public Dictionary<string, ResolvedEntry> Patches { get; } = new(StringComparer.Ordinal);

	/// <summary>The kits it plays, by canonical key.</summary>
	public Dictionary<string, ResolvedEntry> Kits { get; } = new(StringComparer.Ordinal);

	/// <summary>The cues it imports, in the order written.</summary>
	public List<ImportedCue> Cues { get; } = new();

	/// <summary>Its exports, checked: each a scripted run of it to render (exports.md).</summary>
	public List<ExportSpec> Exports { get; } = new();

	/// <summary>The cue a reference names among those it imports (<c>lava/heat</c>, <c>game:mood</c>), or null.</summary>
	public ImportedCue? Cue(string reference)
	{
		foreach (var cue in Cues)
			if (cue.Reference == reference)
				return cue;
		return null;
	}
}

/// <summary>A cue a song imports (cues.md §3): as the song names it, its definition, and the value it is previewed at.</summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class ImportedCue
{
	/// <summary>An imported cue.</summary>
	public ImportedCue(string reference, CueDefinition definition, ProjectLibrary owner, JNode? preview)
	{
		Reference = reference;
		Definition = definition;
		Owner = owner;
		Preview = preview;
	}

	/// <summary>The cue as the song names it: <c>lava/heat</c>, <c>game:mood</c>.</summary>
	public string Reference { get; }

	/// <summary>Its definition.</summary>
	public CueDefinition Definition { get; }

	/// <summary>The project or library it is defined in.</summary>
	public ProjectLibrary Owner { get; }

	/// <summary>The value the song previews it at as written (a level, an option's name), or null for its initial.</summary>
	public JNode? Preview { get; }

	/// <summary>The name it is known by at run time (<see cref="ProjectLibrary.RuntimeCueName"/>).</summary>
	public string RuntimeName => Owner.RuntimeCueName(Definition.Id);

	/// <summary>The preview value: a level, or a choice's option index.</summary>
	public double PreviewValue =>
		Definition.IsChoice
			? (Preview?.Str is { } option && Definition.OptionIndex(option) is var i and >= 0 ? i : Definition.InitialOption)
			: (Preview is JNumber n ? n.Value : Definition.InitialLevel);
}
