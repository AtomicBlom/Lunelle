#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>
/// One project as format 2 loads it: the project opened, or a library it reaches. Its manifest, its entries by kind and
/// id (their JSON as read, unresolved), its cues, and the libraries its own manifest names. A library reached by two
/// routes is loaded once for each, each knowing its route (<see cref="Key"/>).
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class ProjectLibrary
{
	private readonly Dictionary<string, EntryFile>[] _entries =
		{ new(StringComparer.Ordinal), new(StringComparer.Ordinal), new(StringComparer.Ordinal), new(StringComparer.Ordinal) };

	internal ProjectLibrary(IProjectSource source, Manifest manifest, string key, ProjectLibrary? parent)
	{
		Source = source;
		Manifest = manifest;
		Key = key;
		Parent = parent;
	}

	/// <summary>Where its files are.</summary>
	public IProjectSource Source { get; }

	/// <summary>Its manifest.</summary>
	public Manifest Manifest { get; }

	/// <summary>
	/// Its route from the project opened, as the local names that reach it joined by <c>:</c>: <c>""</c> for the project
	/// itself, <c>lunelle</c>, <c>game:lunelle</c> for the library its game library calls <c>lunelle</c>. An entry's
	/// canonical key is this, a <c>:</c> and its id (just its id in the project itself).
	/// </summary>
	public string Key { get; }

	/// <summary>The library that names this one, or null for the project opened.</summary>
	public ProjectLibrary? Parent { get; }

	/// <summary>Its name, as its manifest gives it.</summary>
	public string Name => Manifest.Name;

	/// <summary>The libraries its manifest names, by local name; null for one that couldn't be loaded.</summary>
	public Dictionary<string, ProjectLibrary?> Libraries { get; } = new(StringComparer.Ordinal);

	/// <summary>Its cues, by id.</summary>
	public Dictionary<string, CueDefinition> Cues { get; } = new(StringComparer.Ordinal);

	/// <summary>Every path in it that holds code, which a <c>.lunelle</c> leaves out.</summary>
	public List<string> Code { get; } = new();

	/// <summary>Its entries of <paramref name="kind"/>, by id.</summary>
	public IReadOnlyDictionary<string, EntryFile> Entries(EntryKind kind) => _entries[(int)kind];

	internal Dictionary<string, EntryFile> EntriesToFill(EntryKind kind) => _entries[(int)kind];

	/// <summary>The entry of <paramref name="kind"/> with <paramref name="id"/> here, or null.</summary>
	public EntryFile? Entry(EntryKind kind, string id) => _entries[(int)kind].TryGetValue(id, out var entry) ? entry : null;

	/// <summary>How a problem names a file of this library: its path, after <c>key:</c> in a library.</summary>
	public string FileLabel(string path) => Key.Length == 0 ? path : Key + ":" + path;

	/// <summary>An entry's canonical key: its id here, or <c>key:id</c> in a library.</summary>
	public string CanonicalKey(string id) => Key.Length == 0 ? id : Key + ":" + id;

	/// <summary>The name a cue of this library is known by at run time: its id here, or <c>name:id</c> in a library (cues.md §2).</summary>
	public string RuntimeCueName(string id) => Parent is null ? id : Name + ":" + id;
}

/// <summary>An entry's file as a project holds it: its kind and id, where it is, and its JSON as read.</summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class EntryFile
{
	/// <summary>An entry's file.</summary>
	public EntryFile(ProjectLibrary owner, EntryKind kind, string id, string path, JObject json)
	{
		Owner = owner;
		Kind = kind;
		Id = id;
		Path = path;
		Json = json;
	}

	/// <summary>The project or library it is in.</summary>
	public ProjectLibrary Owner { get; }

	/// <summary>Its kind.</summary>
	public EntryKind Kind { get; }

	/// <summary>Its id in its project.</summary>
	public string Id { get; }

	/// <summary>Its path in its project.</summary>
	public string Path { get; }

	/// <summary>Its JSON as read.</summary>
	public JObject Json { get; }

	/// <summary>The folder it is in, ending in <c>/</c>, which the paths it names are relative to.</summary>
	public string Folder
	{
		get
		{
			var slash = Path.LastIndexOf('/');
			return slash < 0 ? "" : Path.Substring(0, slash + 1);
		}
	}

	/// <summary>Its canonical key (<see cref="ProjectLibrary.CanonicalKey"/>).</summary>
	public string Key => Owner.CanonicalKey(Id);

	/// <summary>How a problem names its file.</summary>
	public string Label => Owner.FileLabel(Path);

	/// <summary>Its title for people: its <c>"title"</c>, else its id's last segment.</summary>
	public string Title => Json["title"]?.Str ?? (Id.LastIndexOf('/') is var slash and >= 0 ? Id.Substring(slash + 1) : Id);
}
