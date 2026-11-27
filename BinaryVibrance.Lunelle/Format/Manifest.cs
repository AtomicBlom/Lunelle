#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>
/// A project's <c>lunelle.meta.json</c> (project.md §3): its name, the libraries it draws on, its macros, the order of
/// its songs, and its album. What it can't use is said in <see cref="Problems"/>; the rest is taken.
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class Manifest
{
	private static readonly string[] Keys =
		{ "format", "name", "title", "version", "about", "author", "licence", "libraries", "macros", "songs", "album" };

	/// <summary>The project's name, an id segment: what it is known by when it is a library.</summary>
	public string Name { get; private set; } = "";

	/// <summary>Its name for people, or null.</summary>
	public string? Title { get; private set; }

	/// <summary>Its version, as semver, or null.</summary>
	public string? Version { get; private set; }

	/// <summary>What it is, or null.</summary>
	public string? About { get; private set; }

	/// <summary>Who made it, or null.</summary>
	public string? Author { get; private set; }

	/// <summary>Its licence: an SPDX id or free text, or null.</summary>
	public string? Licence { get; private set; }

	/// <summary>The libraries it references, by the local name its references use.</summary>
	public IReadOnlyList<LibraryReference> Libraries { get; private set; } = System.Array.Empty<LibraryReference>();

	/// <summary>Its macros: each name its songs use, the number it turns (0 to 7), and what it is for.</summary>
	public IReadOnlyList<(string Name, int Number, string? About)> Macros { get; private set; } = System.Array.Empty<(string, int, string?)>();

	/// <summary>The songs to list first, in order, by id.</summary>
	public IReadOnlyList<string> Songs { get; private set; } = System.Array.Empty<string>();

	/// <summary>The album's title, or null for none.</summary>
	public string? AlbumTitle { get; private set; }

	/// <summary>The album's tracks: each an export's full id, <c>song/export</c>.</summary>
	public IReadOnlyList<(string Track, JNode At)> AlbumTracks { get; private set; } = System.Array.Empty<(string, JNode)>();

	/// <summary>The manifest's JSON as read.</summary>
	public JObject Json { get; private set; } = new();

	/// <summary>What couldn't be taken from it, each against the manifest.</summary>
	public List<FormatProblem> Problems { get; } = new();

	/// <summary>The manifest in <paramref name="text"/>; <paramref name="file"/> is how its problems name it.</summary>
	public static Manifest Read(string text, string file = EntryIds.Manifest)
	{
		var manifest = new Manifest();
		void Problem(string message, JNode? at, bool warning = false) =>
			manifest.Problems.Add(new FormatProblem(file, message, at?.Start ?? 0, at?.Length ?? 0, warning));
		JNode root;
		try
		{
			root = JReader.Parse(text);
		}
		catch (FormatError error)
		{
			manifest.Problems.Add(new FormatProblem(file, error.Message, error.Start, error.Length));
			return manifest;
		}
		if (root is not JObject json)
		{
			Problem("a manifest is an object: { \"format\": \"lunelle/2\", \"name\": \"…\" }", root);
			return manifest;
		}
		manifest.Json = json;
		var format = json["format"]?.Str;
		if (format != EntryIds.ManifestFormat)
		{
			Problem(format is null
				? $"a manifest says its \"format\": \"{EntryIds.ManifestFormat}\""
				: $"it says \"format\": \"{format}\"; this reads \"{EntryIds.ManifestFormat}\"", json["format"] ?? json);
			return manifest;
		}
		foreach (var member in json.Members)
			if (System.Array.IndexOf(Keys, member.Key) < 0)
				manifest.Problems.Add(new FormatProblem(file, $"\"{member.Key}\" isn't something a manifest says ({string.Join(", ", Keys)})", member.KeyStart, member.Key.Length + 2));

		var name = json["name"]?.Str;
		if (name is null)
			Problem("a manifest has a \"name\", the project's id as a library (\"showcase\")", json);
		else if (EntryIds.SegmentProblem(name) is { } bad)
			Problem($"its \"name\" isn't an id: {bad}", json["name"]);
		else
			manifest.Name = name;
		manifest.Title = Text("title");
		manifest.Version = Text("version");
		manifest.About = Text("about");
		manifest.Author = Text("author");
		manifest.Licence = Text("licence");
		if (manifest.Version is { } version && !SemVer.TryParse(version, out _))
			Problem($"\"version\": \"{version}\" isn't a version (\"1.2.0\")", json["version"]);

		var libraries = new List<LibraryReference>();
		if (json["libraries"] is { } librariesNode)
		{
			if (librariesNode is not JObject librariesObject)
				Problem("\"libraries\" is an object of names: { \"lunelle\": { \"version\": \"1\" } }", librariesNode);
			else
				foreach (var member in librariesObject.Members)
				{
					if (EntryIds.SegmentProblem(member.Key) is { } badName)
					{
						Problem($"the library '{member.Key}' isn't named as an id: {badName}", member.Value);
						continue;
					}
					var path = member.Value["path"]?.Str;
					var pinned = member.Value["version"]?.Str;
					if (member.Value is not JObject || (path is null && pinned is null))
					{
						Problem($"the library '{member.Key}' gives a \"path\" or a \"version\"", member.Value);
						continue;
					}
					if (pinned is not null && !SemVer.TryParse(pinned, out _))
					{
						Problem($"the library '{member.Key}' has \"version\": \"{pinned}\", which isn't a version (\"1\", \"1.2\", \"1.2.0\")", member.Value["version"]);
						continue;
					}
					foreach (var key in ((JObject)member.Value).Members)
						if (key.Key != "path" && key.Key != "version")
							manifest.Problems.Add(new FormatProblem(file, $"the library '{member.Key}' says \"{key.Key}\", which a library doesn't (\"path\", \"version\")", key.KeyStart, key.Key.Length + 2));
					libraries.Add(new LibraryReference(member.Key, path, pinned, member.Value));
				}
		}
		manifest.Libraries = libraries;

		var macros = new List<(string, int, string?)>();
		if (json["macros"] is { } macrosNode)
		{
			if (macrosNode is not JObject macrosObject)
				Problem("\"macros\" is an object of names: { \"bright\": 0 }", macrosNode);
			else
				foreach (var member in macrosObject.Members)
				{
					var value = member.Value is JObject described ? described["number"] : member.Value;
					var about = member.Value is JObject withAbout ? withAbout["about"]?.Str : null;
					if (value?.Num is not { } number || !int.TryParse(number, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id) || id < 0 || id > 7)
					{
						Problem($"macro '{member.Key}' turns a number from 0 to 7: \"{member.Key}\": 0, or {{ \"number\": 0, \"about\": \"…\" }}", member.Value);
						continue;
					}
					var taken = macros.FindIndex(m => m.Item2 == id);
					if (taken >= 0)
					{
						Problem($"macro '{member.Key}' is {id}, which '{macros[taken].Item1}' is already", member.Value);
						continue;
					}
					macros.Add((member.Key, id, about));
				}
		}
		manifest.Macros = macros;

		var songs = new List<string>();
		foreach (var song in json["songs"]?.Items ?? System.Array.Empty<JNode>())
		{
			if (song.Str is not { } id || EntryIds.IdProblem(id) is not null)
				Problem("\"songs\" lists songs by id (\"aurora\")", song);
			else if (songs.Contains(id))
				Problem($"\"songs\" lists '{id}' twice", song);
			else
				songs.Add(id);
		}
		manifest.Songs = songs;

		if (json["album"] is JObject album)
		{
			manifest.AlbumTitle = album["title"]?.Str;
			var tracks = new List<(string, JNode)>();
			foreach (var track in album["tracks"]?.Items ?? System.Array.Empty<JNode>())
			{
				if (track.Str is { } id && id.LastIndexOf('/') > 0)
					tracks.Add((id, track));
				else
					Problem("an album's tracks are exports, each \"song/export\"", track);
			}
			manifest.AlbumTracks = tracks;
		}
		else if (json["album"] is { } notAlbum)
			Problem("\"album\" is { \"title\": \"…\", \"tracks\": [\"song/export\", …] }", notAlbum);
		return manifest;

		string? Text(string key)
		{
			var node = json[key];
			if (node is null)
				return null;
			if (node.Str is null)
				Problem($"\"{key}\" is text", node);
			return node.Str;
		}
	}
}

/// <summary>
/// A library a manifest references (project.md §4): by the local name its references use, from a <see cref="Path"/>
/// (a folder or a <c>.lunelle</c> file, relative to the manifest) or provided by the host at a <see cref="Version"/>.
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class LibraryReference
{
	/// <summary>A library reference.</summary>
	public LibraryReference(string localName, string? path, string? version, JNode at)
	{
		LocalName = localName;
		Path = path;
		Version = version;
		At = at;
	}

	/// <summary>The name this project's references give it (<c>lunelle</c> in <c>lunelle:keys/piano</c>).</summary>
	public string LocalName { get; }

	/// <summary>Where it is, relative to the manifest, or null for one the host provides.</summary>
	public string? Path { get; }

	/// <summary>The version: the least the host must provide, or what a path library is expected to be.</summary>
	public string? Version { get; }

	/// <summary>Where it is written in the manifest.</summary>
	public JNode At { get; }
}
