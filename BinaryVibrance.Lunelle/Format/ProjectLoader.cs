#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>
/// Loads a project and every library it reaches (project.md §2 to §4): the manifest, each entry's file by its path, the
/// cue files, and each library by its path or from the host. What can't be loaded is a problem against the file it is
/// in; only what it touches is left out.
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
static class ProjectLoader
{
	/// <summary>The formats each kind's files say they are.</summary>
	public static string Format(EntryKind kind) => kind switch
	{
		EntryKind.Song => "lunelle-song/3",
		EntryKind.Patch => "lunelle-patch/2",
		EntryKind.Kit => "lunelle-kit/1",
		_ => "lunelle-pattern/1",
	};

	/// <summary>
	/// The project in <paramref name="source"/>, with its libraries: from their paths, or from <paramref name="host"/>
	/// (a library the host provides by name: Lunelle's own, <c>lunelle</c>), which may be null for none.
	/// </summary>
	public static LoadedProject Load(IProjectSource source, Func<string, IProjectSource?>? host = null)
	{
		var problems = new List<FormatProblem>();
		var root = Load(source, "", null, host, problems, new List<string>()) ?? new ProjectLibrary(source, new Manifest(), "", null);
		return new LoadedProject(root, problems);
	}

	private static ProjectLibrary? Load(IProjectSource source, string key, ProjectLibrary? parent, Func<string, IProjectSource?>? host,
		List<FormatProblem> problems, List<string> route)
	{
		string Label(string path) => key.Length == 0 ? path : key + ":" + path;
		if (!source.Paths.Contains(EntryIds.Manifest))
		{
			problems.Add(new FormatProblem(Label(EntryIds.Manifest), $"{source.Location} has no {EntryIds.Manifest} at its root, so it isn't a project"));
			return null;
		}
		Manifest manifest;
		try
		{
			manifest = Manifest.Read(source.ReadText(EntryIds.Manifest), Label(EntryIds.Manifest));
		}
		catch (IOException ex)
		{
			problems.Add(new FormatProblem(Label(EntryIds.Manifest), $"can't be read: {ex.Message}"));
			return null;
		}
		problems.AddRange(manifest.Problems);
		var library = new ProjectLibrary(source, manifest, key, parent);
		ReadEntries(library, problems);
		ReadCues(library, problems);

		route.Add(source.Location);
		foreach (var reference in manifest.Libraries)
		{
			var childKey = key.Length == 0 ? reference.LocalName : key + ":" + reference.LocalName;
			void Problem(string message, bool warning = false) =>
				problems.Add(new FormatProblem(Label(EntryIds.Manifest), message, reference.At.Start, reference.At.Length, warning));
			IProjectSource? found;
			if (reference.Path is { } path)
			{
				found = source.Open(path);
				if (found is null)
				{
					Problem($"the library '{reference.LocalName}' isn't found at \"{path}\"");
					library.Libraries[reference.LocalName] = null;
					continue;
				}
			}
			else
			{
				found = host?.Invoke(reference.LocalName);
				if (found is null)
				{
					Problem($"the library '{reference.LocalName}' isn't one this provides: give its \"path\"");
					library.Libraries[reference.LocalName] = null;
					continue;
				}
			}
			var at = route.IndexOf(found.Location);
			if (at >= 0)
			{
				Problem($"the library '{reference.LocalName}' leads back to a project that leads to it: {string.Join(" → ", route.Skip(at).Concat(new[] { found.Location }))}");
				library.Libraries[reference.LocalName] = null;
				continue;
			}
			var child = Load(found, childKey, library, host, problems, route);
			library.Libraries[reference.LocalName] = child;
			if (child is null || reference.Version is not { } wanted)
				continue;
			SemVer.TryParse(wanted, out var want);
			var has = child.Manifest.Version is { } given && SemVer.TryParse(given, out var version) ? version : (SemVer?)null;
			if (reference.Path is null)
			{
				if (has is not { } provided || !want.AcceptedBy(provided))
				{
					Problem($"the library '{reference.LocalName}' is wanted at {wanted} or later in {want.Major}.x; this provides {child.Manifest.Version ?? "no version"}");
					library.Libraries[reference.LocalName] = null;
				}
			}
			else if (has is not { } pathVersion || pathVersion.Text != wanted)
				Problem($"the library '{reference.LocalName}' is pinned at {wanted}, but is {child.Manifest.Version ?? "unversioned"}: its sounds may differ", warning: true);
		}
		route.RemoveAt(route.Count - 1);

		foreach (var song in manifest.Songs)
			if (library.Entry(EntryKind.Song, song) is null)
				problems.Add(new FormatProblem(Label(EntryIds.Manifest), $"\"songs\" lists '{song}', which isn't a song here",
					manifest.Json["songs"]?.Start ?? 0, manifest.Json["songs"]?.Length ?? 0));
		return library;
	}

	// Each entry's file, by its path; two of one id, or an id that is also a folder, are left out
	private static void ReadEntries(ProjectLibrary library, List<FormatProblem> problems)
	{
		var found = new Dictionary<(EntryKind, string), List<string>>();
		foreach (var path in library.Source.Paths)
		{
			if (EntryIds.LeftOut(path) || path.StartsWith(EntryIds.Libraries, StringComparison.Ordinal))
				continue;
			if (EntryIds.IsCode(path))
			{
				library.Code.Add(path);
				continue;
			}
			if (EntryIds.Entry(path) is not { } entry)
				continue;
			if (EntryIds.IdProblem(entry.Id) is { } bad)
			{
				problems.Add(new FormatProblem(library.FileLabel(path), $"its path names it as the {EntryIds.Noun(entry.Kind)} '{entry.Id}': {bad}"));
				continue;
			}
			if (!found.TryGetValue((entry.Kind, entry.Id), out var paths))
				found[(entry.Kind, entry.Id)] = paths = new List<string>();
			paths.Add(path);
		}
		foreach (var pair in found)
		{
			var (kind, id) = pair.Key;
			if (pair.Value.Count > 1)
			{
				foreach (var path in pair.Value)
					problems.Add(new FormatProblem(library.FileLabel(path), $"{string.Join(" and ", pair.Value)} are both the {EntryIds.Noun(kind)} '{id}': only one may be"));
				continue;
			}
			var folder = id + "/";
			var inside = found.Keys.FirstOrDefault(k => k.Item1 == kind && k.Item2.StartsWith(folder, StringComparison.Ordinal));
			if (inside.Item2 is not null)
			{
				problems.Add(new FormatProblem(library.FileLabel(pair.Value[0]),
					$"the {EntryIds.Noun(kind)} '{id}' is also a folder of {EntryIds.Noun(kind)}s ('{inside.Item2}'): rename one"));
				continue;
			}
			var file = pair.Value[0];
			JNode json;
			try
			{
				json = JReader.Parse(library.Source.ReadText(file));
			}
			catch (FormatError error)
			{
				problems.Add(new FormatProblem(library.FileLabel(file), error.Message, error.Start, error.Length));
				continue;
			}
			catch (IOException ex)
			{
				problems.Add(new FormatProblem(library.FileLabel(file), $"can't be read: {ex.Message}"));
				continue;
			}
			if (json is not JObject obj)
			{
				problems.Add(new FormatProblem(library.FileLabel(file), $"a {EntryIds.Noun(kind)} file is an object", json.Start, json.Length));
				continue;
			}
			var format = obj["format"]?.Str;
			if (format != Format(kind))
			{
				problems.Add(new FormatProblem(library.FileLabel(file),
					$"a {EntryIds.Noun(kind)} file says \"format\": \"{Format(kind)}\"{(format is null ? "" : $"; this says \"{format}\"")}",
					obj["format"]?.Start ?? 0, obj["format"]?.Length ?? 0));
				continue;
			}
			library.EntriesToFill(kind)[id] = new EntryFile(library, kind, id, file, obj);
		}
	}

	private static void ReadCues(ProjectLibrary library, List<FormatProblem> problems)
	{
		var defined = new Dictionary<string, List<CueDefinition>>(StringComparer.Ordinal);
		foreach (var path in library.Source.Paths.Where(EntryIds.IsCueFile).OrderBy(p => p, StringComparer.Ordinal))
		{
			string text;
			try
			{
				text = library.Source.ReadText(path);
			}
			catch (IOException ex)
			{
				problems.Add(new FormatProblem(library.FileLabel(path), $"can't be read: {ex.Message}"));
				continue;
			}
			foreach (var cue in CueDefinition.Read(text, library.FileLabel(path), problems))
			{
				if (!defined.TryGetValue(cue.Id, out var list))
					defined[cue.Id] = list = new List<CueDefinition>();
				list.Add(cue);
			}
		}
		foreach (var pair in defined)
		{
			if (pair.Value.Count > 1)
			{
				foreach (var cue in pair.Value)
					problems.Add(new FormatProblem(cue.File, $"the cue '{pair.Key}' is defined in {string.Join(" and ", pair.Value.Select(c => c.File).Distinct())}: only one may define it",
						cue.At.Start, cue.At.Length));
				continue;
			}
			library.Cues[pair.Key] = pair.Value[0];
		}
	}
}

/// <summary>A project loaded with its libraries, and every problem met in loading them.</summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class LoadedProject
{
	/// <summary>A loaded project.</summary>
	public LoadedProject(ProjectLibrary root, List<FormatProblem> problems)
	{
		Root = root;
		Problems = problems;
	}

	/// <summary>The project opened.</summary>
	public ProjectLibrary Root { get; }

	/// <summary>What couldn't be loaded, in every library.</summary>
	public List<FormatProblem> Problems { get; }

	/// <summary>The project and every library reached, by every route.</summary>
	public IEnumerable<ProjectLibrary> All
	{
		get
		{
			var stack = new Stack<ProjectLibrary>();
			stack.Push(Root);
			while (stack.Count > 0)
			{
				var library = stack.Pop();
				yield return library;
				foreach (var child in library.Libraries.Values)
					if (child is not null)
						stack.Push(child);
			}
		}
	}
}
