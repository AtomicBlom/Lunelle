#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>
/// How a file's path names an entry (project.md §2.1): its path below its kind's folder, without the kind's suffix, so
/// <c>Patches/bass/sub.patch.json</c> is the patch <c>bass/sub</c>. Each segment is lowercase <c>a-z</c>, <c>0-9</c>
/// and <c>-</c>, starting with a letter. A reference to an entry is its id, or <c>library:id</c> for one in a library
/// (§2.2).
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
static class EntryIds
{
	/// <summary>The manifest's name, at a project's root.</summary>
	public const string Manifest = "lunelle.meta.json";

	/// <summary>The format a manifest says it is.</summary>
	public const string ManifestFormat = "lunelle/2";

	/// <summary>Where a project's bundled libraries are, each a project of its own.</summary>
	public const string Libraries = "Libraries/";

	/// <summary>The folder cue files are in, and their suffix.</summary>
	public const string CuesFolder = "Cues/";

	/// <summary>A cue file's suffix.</summary>
	public const string CuesSuffix = ".cues.json";

	/// <summary>The folder a kind's entries are in: <c>Songs/</c>, <c>Patches/</c>, <c>Kits/</c>, <c>Patterns/</c>.</summary>
	public static string Folder(EntryKind kind) => kind switch
	{
		EntryKind.Song => "Songs/",
		EntryKind.Patch => "Patches/",
		EntryKind.Kit => "Kits/",
		_ => "Patterns/",
	};

	/// <summary>A kind's file suffix: <c>.song.json</c>, <c>.patch.json</c>, <c>.kit.json</c>, <c>.pattern.json</c>.</summary>
	public static string Suffix(EntryKind kind) => kind switch
	{
		EntryKind.Song => ".song.json",
		EntryKind.Patch => ".patch.json",
		EntryKind.Kit => ".kit.json",
		_ => ".pattern.json",
	};

	/// <summary>What a kind is called in a message: "song", "patch", "kit", "pattern".</summary>
	public static string Noun(EntryKind kind) => kind.ToString().ToLowerInvariant();

	/// <summary>
	/// Whether a path is never part of a project (§2): the workbench's own folder, a build's output, git's files. They
	/// are neither read nor packed.
	/// </summary>
	public static bool LeftOut(string path) =>
		path.StartsWith(".lunelle-studio/", StringComparison.Ordinal) || path.StartsWith("bin/", StringComparison.Ordinal)
		|| path.StartsWith("obj/", StringComparison.Ordinal) || path.StartsWith(".git/", StringComparison.Ordinal)
		|| path == ".gitattributes" || path == ".gitignore" || path.IndexOf("/.git/", StringComparison.Ordinal) >= 0;

	/// <summary>Whether a path is code, which a <c>.lunelle</c> never holds (§2): C#, a project, MSBuild's files.</summary>
	public static bool IsCode(string path)
	{
		var name = FileName(path);
		return path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
			|| path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
			|| (name.StartsWith("Directory.", StringComparison.Ordinal)
				&& (name.EndsWith(".props", StringComparison.Ordinal) || name.EndsWith(".targets", StringComparison.Ordinal)));
	}

	/// <summary>
	/// The entry a file at <paramref name="path"/> (relative to its project's root, <c>/</c> between folders) holds, by
	/// kind and id; or null when it holds none (a sample, a cue file, a readme, anything in <c>Libraries/</c>). The id
	/// isn't checked here: <see cref="IdProblem"/> does that.
	/// </summary>
	public static (EntryKind Kind, string Id)? Entry(string path)
	{
		foreach (var kind in new[] { EntryKind.Song, EntryKind.Patch, EntryKind.Kit, EntryKind.Pattern })
		{
			var folder = Folder(kind);
			if (!path.StartsWith(folder, StringComparison.Ordinal))
				continue;
			var rest = path.Substring(folder.Length);
			var suffix = Suffix(kind);
			if (rest.EndsWith(suffix, StringComparison.Ordinal) && rest.Length > suffix.Length)
				return (kind, rest.Substring(0, rest.Length - suffix.Length));
			if (kind == EntryKind.Kit && (rest.EndsWith("/kit.json", StringComparison.Ordinal)))
				return (kind, rest.Substring(0, rest.Length - "/kit.json".Length));
			return null;
		}
		return null;
	}

	/// <summary>Whether <paramref name="path"/> is a cue file: <c>Cues/**/*.cues.json</c>.</summary>
	public static bool IsCueFile(string path) =>
		path.StartsWith(CuesFolder, StringComparison.Ordinal) && path.EndsWith(CuesSuffix, StringComparison.Ordinal);

	/// <summary>What is wrong with <paramref name="id"/> as an id, or null when nothing is.</summary>
	public static string? IdProblem(string id)
	{
		if (id.Length == 0)
			return "an id can't be empty";
		foreach (var segment in id.Split('/'))
			if (SegmentProblem(segment) is { } problem)
				return $"'{id}' isn't an id: {problem}";
		return null;
	}

	/// <summary>What is wrong with <paramref name="segment"/> as one segment of an id (or a library's name), or null.</summary>
	public static string? SegmentProblem(string segment)
	{
		if (segment.Length == 0)
			return "a segment between '/'s is empty";
		if (segment[0] < 'a' || segment[0] > 'z')
			return $"'{segment}' starts with something other than a lowercase letter";
		foreach (var c in segment)
			if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-')
				return $"'{segment}' holds '{c}': ids are lowercase letters, digits and '-'";
		return null;
	}

	/// <summary>
	/// A reference split into its library's local name (null for this project) and the id: <c>lunelle:keys/piano</c> is
	/// <c>("lunelle", "keys/piano")</c>, <c>lead</c> is <c>(null, "lead")</c>. Null when it isn't a reference.
	/// </summary>
	public static (string? Library, string Id)? Reference(string reference)
	{
		var colon = reference.IndexOf(':');
		if (colon < 0)
			return IdProblem(reference) is null ? (null, reference) : null;
		var library = reference.Substring(0, colon);
		var id = reference.Substring(colon + 1);
		return SegmentProblem(library) is null && IdProblem(id) is null ? (library, id) : null;
	}

	/// <summary>The C# name of an id's segment: <c>electric-piano</c> is <c>ElectricPiano</c>, <c>808</c> isn't one (an id starts with a letter).</summary>
	public static string Pascal(string segment)
	{
		var text = new System.Text.StringBuilder();
		var upper = true;
		foreach (var c in segment)
		{
			if (c == '-' || c == '_' || c == ' ')
			{
				upper = true;
				continue;
			}
			text.Append(upper ? char.ToUpperInvariant(c) : c);
			upper = false;
		}
		return text.ToString();
	}

	private static string FileName(string path)
	{
		var slash = path.LastIndexOf('/');
		return slash < 0 ? path : path.Substring(slash + 1);
	}
}
