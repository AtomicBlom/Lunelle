using System.Text.Json;

using BinaryVibrance.Lunelle.Chip;

namespace BinaryVibrance.Lunelle.Music;

/// <summary>
/// A music project of files alone, with no code: its songs (<c>*.song.json</c>), patches (<c>*.patch.json</c>), kits
/// (<c>kit.json</c> or <c>*.kit.json</c>) and, in <c>lunelle-project.json</c> at its root, the cues and macros its songs
/// name, which a project with code declares in C#. Everything is read through a function given a path, so a folder on
/// disk, a zip or a browser's storage load alike. What a song file names as code (a part, a placement, a hook) has none
/// here: a part or a placement plays a <see cref="Rest"/>, a hook doesn't run, and <see cref="CodeIn"/> says which.
/// </summary>
/// <remarks>
/// A song or patch is named as the generator names it from the same files: a patch by its file
/// (<c>electric-piano.patch.json</c> is "electric-piano"), a song and a kit by the <c>"name"</c> inside. A file that can't be
/// read is left out and said in <see cref="Problems"/>, so one broken file doesn't close the rest.
/// </remarks>
public sealed class MusicFolder
{
	/// <summary>The file at a project's root that declares its cues and macros.</summary>
	public const string ProjectFileName = "lunelle-project.json";

	/// <summary>The format <see cref="ProjectFileName"/> says it is.</summary>
	public const string ProjectFormat = "lunelle-project/1";

	private readonly Dictionary<string, string> _songText = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _songPath = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _patchText = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _patchPath = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Lazy<DrumKit>> _kits = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _kitPath = new(StringComparer.Ordinal);
	private readonly Dictionary<string, IReadOnlyList<string>> _code = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Macro> _macros = new(StringComparer.Ordinal);
	private readonly List<CueBase> _cues = [];
	private readonly List<string> _problems = [];

	private MusicFolder()
	{
		Bindings = new SongBindings
		{
			Patch = name => _patchText.TryGetValue(name, out var text) ? PatchFile.Load(text) : null,
			Kit = name => _kits.TryGetValue(name, out var kit) ? kit.Value : null,
			Cue = name => _cues.Find(c => c.Name == name),
			Macro = name => _macros.TryGetValue(name, out var macro) ? macro : null,
			Part = _ => Rest.Bar,
			Placement = (_, section, track) => section.Play(track, Rest.Bar),
			Hook = (_, _, _) => { },
		};
	}

	/// <summary>The project's name, from <see cref="ProjectFileName"/>, or null when it gives none.</summary>
	public string? Name { get; private set; }

	/// <summary>The cues its songs listen to, as <see cref="ProjectFileName"/> declares them.</summary>
	public IReadOnlyList<CueBase> Cues => _cues;

	/// <summary>Its macros by the names its songs give them.</summary>
	public IReadOnlyDictionary<string, Macro> Macros => _macros;

	/// <summary>Its songs by name, each built afresh from its file whenever asked.</summary>
	public IReadOnlyList<(string Name, Func<Song> Build)> Songs { get; private set; } = [];

	/// <summary>Its patches by name, each built afresh from its file whenever asked.</summary>
	public IReadOnlyList<(string Name, Func<Patch> Build)> Patches { get; private set; } = [];

	/// <summary>Its kits by name, each loaded once, on first asking (a sampled kit decodes all its takes).</summary>
	public IReadOnlyList<(string Name, Func<DrumKit> Build)> Kits { get; private set; } = [];

	/// <summary>What binds the names in its song files: the folder's patches, kits, cues and macros, code as rests.</summary>
	public SongBindings Bindings { get; }

	/// <summary>Each file that couldn't be read, and why, as a line naming it.</summary>
	public IReadOnlyList<string> Problems => _problems;

	/// <summary>The song file named <paramref name="song"/>'s path as the folder was given it, or null.</summary>
	public string? SongPath(string song) => _songPath.GetValueOrDefault(song);

	/// <summary>The patch file named <paramref name="patch"/>'s path as the folder was given it, or null.</summary>
	public string? PatchPath(string patch) => _patchPath.GetValueOrDefault(patch);

	/// <summary>The kit named <paramref name="kit"/>'s manifest path as the folder was given it, or null.</summary>
	public string? KitPath(string kit) => _kitPath.GetValueOrDefault(kit);

	/// <summary>
	/// What song <paramref name="song"/>'s file names as code, which plays nothing here: <c>"part Drums"</c>,
	/// <c>"placement Fill"</c>, <c>"hook OnBuilt"</c>. Empty for a song that is data throughout, or no song.
	/// </summary>
	public IReadOnlyList<string> CodeIn(string song) => _code.GetValueOrDefault(song) ?? [];

	/// <summary>The name its songs give macro <paramref name="id"/>, or null when they give it none.</summary>
	public string? MacroName(int id)
	{
		foreach (var (name, macro) in _macros)
			if (macro.Id == id)
				return name;
		return null;
	}

	/// <summary>The music in the folder at <paramref name="directory"/>, every file under it.</summary>
	/// <exception cref="DirectoryNotFoundException">There is no such folder.</exception>
	public static MusicFolder FromDirectory(string directory)
	{
		ArgumentNullException.ThrowIfNull(directory);
		var root = Path.GetFullPath(directory);
		if (!Directory.Exists(root))
			throw new DirectoryNotFoundException($"There is no folder {root}.");
		var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
			.Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
			.Where(f => !f.StartsWith("bin/", StringComparison.Ordinal) && !f.StartsWith("obj/", StringComparison.Ordinal));
		return Load(files, path => File.ReadAllBytes(Path.Combine(root, path)));
	}

	/// <summary>
	/// The music among <paramref name="files"/> (paths relative to the project's root, <c>/</c> between folders), each read
	/// through <paramref name="read"/> by its path as given.
	/// </summary>
	public static MusicFolder Load(IEnumerable<string> files, Func<string, byte[]> read)
	{
		ArgumentNullException.ThrowIfNull(files);
		ArgumentNullException.ThrowIfNull(read);
		var folder = new MusicFolder();
		var all = files.Select(f => f.Replace('\\', '/')).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

		string? Text(string path)
		{
			try
			{
				return System.Text.Encoding.UTF8.GetString(read(path));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KeyNotFoundException or ArgumentException)
			{
				folder._problems.Add($"{path}: can't be read: {ex.Message}");
				return null;
			}
		}

		if (all.Contains(ProjectFileName) && Text(ProjectFileName) is { } project)
			folder.ReadProject(project);

		var patches = new List<(string, Func<Patch>)>();
		foreach (var path in all.Where(f => f.EndsWith(".patch.json", StringComparison.OrdinalIgnoreCase)))
		{
			if (Text(path) is not { } text)
				continue;
			var name = Stem(path);
			try
			{
				PatchFile.Read(text);
			}
			catch (Exception ex) when (ex is FormatException or PatchException or JsonException or ArgumentException)
			{
				folder._problems.Add($"{path}: {ex.Message}");
				continue;
			}
			if (!folder._patchText.TryAdd(name, text))
			{
				folder._problems.Add($"{path}: another patch file is named '{name}' too ({folder._patchPath[name]}); this one is left out.");
				continue;
			}
			folder._patchPath[name] = path;
			patches.Add((name, () => PatchFile.Load(text)));
		}
		folder.Patches = patches;

		var kits = new List<(string, Func<DrumKit>)>();
		foreach (var path in all.Where(f => Path.GetFileName(f) is var file
			&& (file.Equals("kit.json", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".kit.json", StringComparison.OrdinalIgnoreCase))))
		{
			if (Text(path) is not { } text)
				continue;
			string? name;
			try
			{
				using var document = PatchFile.Parse(text);
				name = PatchFile.String(document.RootElement, "name");
			}
			catch (Exception ex) when (ex is JsonException or FormatException)
			{
				folder._problems.Add($"{path}: {ex.Message}");
				continue;
			}
			if (string.IsNullOrWhiteSpace(name))
			{
				folder._problems.Add($"{path}: a kit has a \"name\".");
				continue;
			}
			if (folder._kits.ContainsKey(name))
			{
				folder._problems.Add($"{path}: another kit is named '{name}' too ({folder._kitPath[name]}); this one is left out.");
				continue;
			}
			var kitPath = path;
			var kit = new Lazy<DrumKit>(() => DrumKit.Load(read, kitPath, folder.Bindings.Patch));
			folder._kits[name] = kit;
			folder._kitPath[name] = path;
			kits.Add((name, () => kit.Value));
		}
		folder.Kits = kits;

		var songs = new List<(string, Func<Song>)>();
		foreach (var path in all.Where(f => f.EndsWith(".song.json", StringComparison.OrdinalIgnoreCase)))
		{
			if (Text(path) is not { } text)
				continue;
			string? name;
			List<string> code;
			try
			{
				using var document = PatchFile.Parse(text);
				name = PatchFile.String(document.RootElement, "name");
				code = Code(document.RootElement);
			}
			catch (Exception ex) when (ex is JsonException or FormatException)
			{
				folder._problems.Add($"{path}: {ex.Message}");
				continue;
			}
			if (string.IsNullOrWhiteSpace(name))
			{
				folder._problems.Add($"{path}: a song file has a \"name\".");
				continue;
			}
			if (!folder._songText.TryAdd(name, text))
			{
				folder._problems.Add($"{path}: another song is named '{name}' too ({folder._songPath[name]}); this one is left out.");
				continue;
			}
			folder._songPath[name] = path;
			folder._code[name] = code;
			songs.Add((name, () => SongFile.Load(text, folder.Bindings)));
		}
		folder.Songs = songs;
		return folder;
	}

	// lunelle-project/1: { "format", "name", "cues": [{ "name", "initial" } or { "name", "options": [...], "initial" }],
	// "macros": { "bright": 0 } }
	private void ReadProject(string text)
	{
		void Problem(string message) => _problems.Add($"{ProjectFileName}: {message}");
		JsonDocument document;
		try
		{
			document = PatchFile.Parse(text);
		}
		catch (JsonException ex)
		{
			Problem(ex.Message);
			return;
		}
		using (document)
		{
			var root = document.RootElement;
			if (PatchFile.String(root, "format") is var format && format != ProjectFormat)
			{
				Problem($"it says \"format\": \"{format}\"; this reads \"{ProjectFormat}\".");
				return;
			}
			Name = PatchFile.String(root, "name");
			foreach (var cue in PatchFile.Array(root, "cues"))
			{
				var name = PatchFile.String(cue, "name");
				if (string.IsNullOrWhiteSpace(name))
				{
					Problem("a cue has a \"name\".");
					continue;
				}
				if (_cues.Exists(c => c.Name == name))
				{
					Problem($"two cues are named '{name}'.");
					continue;
				}
				try
				{
					if (cue.TryGetProperty("options", out var options))
					{
						var names = options.ValueKind == JsonValueKind.Array
							? options.EnumerateArray().Select(o => o.GetString() ?? "").ToList()
							: throw new ArgumentException($"cue '{name}': its \"options\" are a list of names.");
						_cues.Add(new ChoiceCue(name, names, PatchFile.String(cue, "initial")));
					}
					else
						_cues.Add(new Cue(name, PatchFile.Float(cue, "initial") ?? 0f));
				}
				catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
				{
					Problem(ex.Message);
				}
			}
			if (root.TryGetProperty("macros", out var macros))
			{
				if (macros.ValueKind != JsonValueKind.Object)
				{
					Problem("its \"macros\" are an object of names and numbers: { \"bright\": 0 }.");
					return;
				}
				foreach (var macro in macros.EnumerateObject())
				{
					if (macro.Value.ValueKind != JsonValueKind.Number || !macro.Value.TryGetInt32(out var id) || id < 0 || id >= Macro.Count)
						Problem($"macro '{macro.Name}' is {macro.Value}, and a macro is a number from 0 to {Macro.Count - 1}.");
					else if (MacroName(id) is { } other)
						Problem($"macro '{macro.Name}' is {id}, which '{other}' is already.");
					else
						_macros[macro.Name] = new Macro(id);
				}
			}
		}
	}

	// What a song file names as code: each part, placement and hook
	private static List<string> Code(JsonElement root)
	{
		var code = new List<string>();
		void Part(JsonElement part)
		{
			if (PatchFile.Object(part, "part") is { } named && PatchFile.String(named, "code") is { } name)
				code.Add($"part {name}");
			if (PatchFile.Object(part, "endWith") is { } fill)
				Part(fill);
		}
		foreach (var section in PatchFile.Array(root, "sections"))
			foreach (var part in PatchFile.Array(section, "parts"))
			{
				if (PatchFile.String(part, "code") is { } placement)
					code.Add($"placement {placement}");
				else
					Part(part);
			}
		foreach (var hook in PatchFile.Array(root, "hooks"))
			code.Add($"hook {hook.GetString()}");
		return [.. code.Distinct(StringComparer.Ordinal)];
	}

	// "Patches/electric-piano.patch.json" is "electric-piano"
	private static string Stem(string path)
	{
		var name = Path.GetFileName(path);
		var at = name.IndexOf('.', StringComparison.Ordinal);
		return at < 0 ? name : name[..at];
	}
}
