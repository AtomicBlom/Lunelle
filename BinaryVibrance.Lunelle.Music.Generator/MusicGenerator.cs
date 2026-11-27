using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

using BinaryVibrance.Lunelle.Music.Generator.Format;

namespace BinaryVibrance.Lunelle.Music.Generator;

/// <summary>
/// Turns a project's music into C# (format 2, docs/spec/project.md §7): its <c>lunelle.meta.json</c> and entries, and
/// its libraries' (Lunelle's own among them), resolved by the very resolver Lunelle reads files with, so a project plays
/// the same with code and without. Every patch the project has or plays is a property of the patches' class, nested by
/// its id (<c>Patches.Bass.Sub</c>, <c>Patches.Lunelle.Keys.ElectricPiano</c>); a kit of patches a property of the
/// kits'; every cue a property of the cues' class, with an enum for a choice; every macro the manifest names a property
/// of the macros'; every song a class of its own named for its file, in the namespace of its folder. What a file says
/// that can't be code is an error at its place in the file.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class MusicGenerator : IIncrementalGenerator
{
	internal static readonly DiagnosticDescriptor Problem = new(
		"LUNELLEMUSIC001", "A music file the generator can't make into code", "{0}", "BinaryVibrance.Lunelle.Music", DiagnosticSeverity.Error, isEnabledByDefault: true);

	internal static readonly DiagnosticDescriptor Warning = new(
		"LUNELLEMUSIC002", "A music file says something that may not be what was meant", "{0}", "BinaryVibrance.Lunelle.Music", DiagnosticSeverity.Warning, isEnabledByDefault: true);

	/// <inheritdoc/>
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var files = context.AdditionalTextsProvider
			.Where(t => t.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			.Select((t, cancel) => (t.Path, Text: t.GetText(cancel)))
			.Collect();
		var options = context.AnalyzerConfigOptionsProvider.Combine(context.CompilationProvider)
			.Select((pair, _) => MusicOptions.From(pair.Left.GlobalOptions, pair.Right.AssemblyName ?? "Music"));
		context.RegisterSourceOutput(files.Combine(context.CompilationProvider).Combine(options),
			(output, input) => Emit(output, input.Left.Left, input.Left.Right, input.Right));
	}

	private static void Emit(SourceProductionContext output, ImmutableArray<(string Path, SourceText? Text)> files, Compilation compilation, MusicOptions options)
	{
		var all = new Dictionary<string, SourceText>(StringComparer.OrdinalIgnoreCase);
		foreach (var (path, text) in files)
			if (text is not null)
				all[Path.GetFullPath(path)] = text;
		var rootDir = options.ProjectDir.Length > 0
			? AdditionalSource.Full(options.ProjectDir)
			: all.Keys.Where(p => Path.GetFileName(p) == EntryIds.Manifest).Select(p => Path.GetDirectoryName(p)!).OrderBy(p => p.Length).FirstOrDefault();
		if (rootDir is null || !all.ContainsKey(Path.Combine(rootDir, EntryIds.Manifest)))
		{
			// Music files with no manifest are format 1's, which format 2 doesn't read
			foreach (var pair in all.Where(f => f.Key.EndsWith(".song.json", StringComparison.OrdinalIgnoreCase) || f.Key.EndsWith(".patch.json", StringComparison.OrdinalIgnoreCase)).Take(1))
				if (pair is { Key: var path, Value: var text })
					output.ReportDiagnostic(Diagnostic.Create(Problem, Location.Create(path, new TextSpan(0, 0), text.Lines.GetLinePositionSpan(new TextSpan(0, 0))),
					$"{Path.GetFileName(path)}: a project's music needs a {EntryIds.Manifest} at its root (\"format\": \"{EntryIds.ManifestFormat}\"), beside its .csproj"));
			return;
		}

		var source = new AdditionalSource(rootDir, all);
		IProjectSource? Host(string name)
		{
			if (name != "lunelle" || options.LibraryDir.Length == 0)
				return null;
			var library = AdditionalSource.Full(options.LibraryDir);
			return all.ContainsKey(Path.Combine(library, EntryIds.Manifest)) ? new AdditionalSource(library, all) : null;
		}
		var project = ProjectLoader.Load(source, Host);
		var resolver = new Resolver(project);
		var context = new MusicContext(compilation, options, resolver);
		var root = project.Root;

		var order = root.Manifest.Songs.Where(id => root.Entry(EntryKind.Song, id) is not null).ToList();
		order.AddRange(root.Entries(EntryKind.Song).Keys.Where(id => !order.Contains(id)).OrderBy(id => id, StringComparer.Ordinal));
		var songs = order.Select(id => resolver.Song(root.Entry(EntryKind.Song, id)!)).ToList();
		foreach (var kind in new[] { EntryKind.Patch, EntryKind.Kit })
			foreach (var entry in root.Entries(kind).Values)
				resolver.Resolve(entry);
		foreach (var pattern in root.Entries(EntryKind.Pattern).Values)
			resolver.Pattern(pattern);

		var reporter = new Reporter(output, project);
		var broken = new HashSet<string>(StringComparer.Ordinal);
		foreach (var problem in project.Problems.Concat(resolver.Problems))
		{
			// A sound the files don't have may be the game's own, in C#
			if (problem.Missing is { } missing && missing.IndexOf(':') is var colon and > 0
				&& context.HasMember(missing.Substring(0, colon) == "patch" ? options.Patches : options.Kits, Code.Pascal(missing.Substring(colon + 1))))
				continue;
			reporter.Report(problem);
			if (!problem.IsWarning)
				broken.Add(problem.File);
		}
		// A song with an error isn't written, so the error is said once, where it is
		songs.RemoveAll(s => broken.Contains(s.File.Label));

		// Every patch and kit the project has or plays, by canonical key
		var patches = new SortedDictionary<string, ResolvedEntry>(StringComparer.Ordinal);
		var kits = new SortedDictionary<string, ResolvedEntry>(StringComparer.Ordinal);
		foreach (var entry in root.Entries(EntryKind.Patch).Values)
			if (resolver.Resolve(entry) is { Ok: true } patch)
				patches[patch.Key] = patch;
		foreach (var entry in root.Entries(EntryKind.Kit).Values)
			if (resolver.Resolve(entry) is { Ok: true } kit)
				kits[kit.Key] = kit;
		foreach (var song in songs)
		{
			foreach (var pair in song.Patches)
				if (!pair.Key.StartsWith("~", StringComparison.Ordinal) && pair.Value.Ok)
					patches[pair.Key] = pair.Value;
			foreach (var pair in song.Kits)
				if (!pair.Key.StartsWith("~", StringComparison.Ordinal) && pair.Value.Ok)
					kits[pair.Key] = pair.Value;
		}
		foreach (var kit in kits.Values.ToList())
			foreach (var pair in kit.Patches)
				patches[pair.Key] = pair.Value;

		var patchEmitter = new PatchEmitter(context);
		if (patches.Count > 0)
		{
			var tree = new ClassTree(options.Patches);
			foreach (var patch in patches.Values)
			{
				var path = MusicContext.MemberPath(patch.Key).Split('.');
				try
				{
					tree.Add(path, depth => patchEmitter.Property(patch.Json, path[path.Length - 1], new string(' ', 4 * depth)));
				}
				catch (FormatError error)
				{
					reporter.Report(patch.File, error);
				}
			}
			var listed = root.Entries(EntryKind.Patch).Keys.OrderBy(id => id, StringComparer.Ordinal).Where(id => patches.ContainsKey(id))
				.Select(id => $"        ({Code.Quote(id)}, () => {MusicContext.MemberPath(id)}),").ToList();
			tree.Root.Add(string.Join("\n", new[]
			{
				"    /// <summary>Every patch the project has as a file, by its id: a new file is listed here as it is added.</summary>",
				"    public static (string Name, global::System.Func<Patch> Build)[] Files =>",
				"    [",
			}.Concat(listed).Concat(new[] { "    ];" })));
			Write(output, $"{options.Patches}.g.cs", options, "patch", tree, reporter);
		}

		var kitTree = new ClassTree(options.Kits);
		foreach (var kit in kits.Values)
		{
			if (kit.Json["pieces"]?.Items.Any(p => p["layers"] is not null) == true)
				continue;
			var path = MusicContext.MemberPath(kit.Key).Split('.');
			if (context.HasMember(options.Kits, path[path.Length - 1]) && path.Length == 1)
				continue;
			try
			{
				var code = KitCode(kit, context);
				kitTree.Add(path, depth => Doc(kit.Json["about"]?.Str, depth).Concat(new[] { $"{new string(' ', 4 * depth)}public static DrumKit {path[path.Length - 1]} {{ get; }} = {code};" }));
			}
			catch (FormatError error)
			{
				reporter.Report(kit.File, error);
			}
		}
		if (!kitTree.Empty)
			Write(output, $"{options.Kits}.g.cs", options, "kit", kitTree, reporter);

		// Cues: the project's own, and those its songs import from libraries
		var cueTree = new ClassTree(options.Cues);
		var cues = new List<(ProjectLibrary Owner, CueDefinition Cue)>();
		cues.AddRange(root.Cues.Values.OrderBy(c => c.Id, StringComparer.Ordinal).Select(c => (root, c)));
		foreach (var import in songs.SelectMany(s => s.Cues))
			if (import.Owner != root && !cues.Any(c => c.Owner.Key == import.Owner.Key && c.Cue.Id == import.Definition.Id))
				cues.Add((import.Owner, import.Definition));
		foreach (var (owner, cue) in cues)
		{
			var path = MusicContext.CuePath(owner, cue.Id).Split('.');
			try
			{
				var lines = CueCode(owner, cue, path[path.Length - 1], context);
				cueTree.Add(path, depth => lines.Select(l => new string(' ', 4 * depth) + l));
			}
			catch (FormatError error)
			{
				reporter.Report(owner, cue.File, error);
			}
		}
		if (!cueTree.Empty)
			Write(output, $"{options.Cues}.g.cs", options, "cue", cueTree, reporter);

		if (root.Manifest.Macros.Count > 0)
		{
			var macroTree = new ClassTree(options.Macros);
			foreach (var (name, number, about) in root.Manifest.Macros)
				macroTree.Add(new[] { Code.Identifier(Code.Pascal(name)) },
					depth => Doc(about, depth).Concat(new[] { $"{new string(' ', 4 * depth)}public static Macro {Code.Identifier(Code.Pascal(name))} {{ get; }} = new({number});" }));
			Write(output, $"{options.Macros}.g.cs", options, "macro", macroTree, reporter);
		}

		var songEmitter = new SongEmitter(context);
		var songList = new List<string>();
		foreach (var song in songs)
		{
			context.Song = song;
			var file = song.File;
			try
			{
				var own = new List<string>();
				foreach (var pair in song.Patches.Where(p => p.Key.StartsWith("~", StringComparison.Ordinal)))
					own.AddRange(patchEmitter.Property(pair.Value.Json, MusicContext.OwnMember(pair.Key), "    ").Select(l => l.Replace("public static", "private static")));
				foreach (var pair in song.Kits.Where(p => p.Key.StartsWith("~", StringComparison.Ordinal)))
				{
					if (pair.Value.Json["pieces"]?.Items.Any(p => p["layers"] is not null) == true)
						throw new FormatError($"the song's own kit '{pair.Key.Substring(1)}' is sampled, which a song's code can't hold: make it the project's kit, loaded in C#", file.Json["kits"]);
					own.Add($"    private static DrumKit {MusicContext.OwnMember(pair.Key)} {{ get; }} = {KitCode(pair.Value, context)};");
				}
				foreach (var pair in song.Kits.Where(k => !k.Key.StartsWith("~", StringComparison.Ordinal)))
					if (pair.Value.Json["pieces"]?.Items.Any(p => p["layers"] is not null) == true
						&& !context.HasMember(options.Kits, MusicContext.MemberPath(pair.Key)))
						throw new FormatError($"the kit '{pair.Key}' is sampled: a project with code loads it in C# as {options.Kits}.{MusicContext.MemberPath(pair.Key)} (DrumKit.Load)", file.Json["tracks"]);
				var space = options.NamespaceOf(Path.Combine(rootDir, file.Path.Replace('/', Path.DirectorySeparatorChar)));
				var type = Code.Identifier(Code.Pascal(SongEmitter.StemOf(file.Path)));
				var text = songEmitter.Class(song.Json, Path.GetFileName(file.Path), space, own);
				output.AddSource($"{space}.{type}.g.cs", text.Replace("\r\n", "\n"));
				songList.Add($"        ({Code.Quote(file.Id)}, global::{space}.{type}.Build),");
			}
			catch (FormatError error)
			{
				reporter.Report(file, error);
			}
		}
		context.Song = null;
		if (songList.Count == 0)
			return;
		var list = new StringBuilder();
		list.AppendLine("// <auto-generated/>");
		list.AppendLine("// Made from the project's songs by Lunelle's music generator");
		list.AppendLine("#nullable enable");
		list.AppendLine("using BinaryVibrance.Lunelle.Music;");
		list.AppendLine();
		list.AppendLine($"namespace {options.RootNamespace};");
		list.AppendLine();
		list.AppendLine("/// <summary>Every song the project has, by its id, in the manifest's order: a new file is listed here as it is added.</summary>");
		list.AppendLine("static class SongFiles");
		list.AppendLine("{");
		list.AppendLine("    public static (string Name, global::System.Func<Song> Build)[] All =>");
		list.AppendLine("    [");
		foreach (var line in songList)
			list.AppendLine(line);
		list.AppendLine("    ];");
		list.AppendLine("}");
		output.AddSource("SongFiles.g.cs", list.ToString().Replace("\r\n", "\n"));
	}

	// A kit of patches: each piece a patch, played at note 60, with its gain, pan and choke
	private static string KitCode(ResolvedEntry kit, MusicContext context)
	{
		var pieces = new List<string>();
		foreach (var piece in kit.Json["pieces"]?.Items ?? Array.Empty<JNode>())
		{
			var id = piece["piece"]?.Str ?? throw new FormatError("a kit's piece has a \"piece\"", piece);
			var patch = piece["patch"]?.Str ?? throw new FormatError($"the piece '{id}' plays a \"patch\"", piece);
			pieces.Add($"new DrumKitPiece({context.Piece(id)}, [], {Code.F(piece["gainDb"]?.Num ?? "0")}, {Code.F(piece["pan"]?.Num ?? "0")}, "
				+ $"{piece["choke"]?.Num ?? "0"}, {context.Options.Patches}.{MusicContext.MemberPath(patch)})");
		}
		return $"new DrumKit({Code.Quote(kit.Json["name"]?.Str ?? kit.Key)}{string.Concat(pieces.Select(p => ", " + p))})";
	}

	// A cue's property, and a choice's enum (cues.md §6)
	private static List<string> CueCode(ProjectLibrary owner, CueDefinition cue, string member, MusicContext context)
	{
		var lines = new List<string>();
		var runtime = Code.Quote(owner.RuntimeCueName(cue.Id));
		if (!cue.IsChoice)
		{
			lines.AddRange(Doc(cue.About, 0));
			lines.Add($"public static Cue {member} {{ get; }} = new({runtime}, {Code.F((float)cue.InitialLevel)});");
			return lines;
		}
		string type;
		if (cue.Enum is { } bound)
		{
			var symbol = context.Enum(bound) ?? throw new FormatError($"the cue '{cue.Id}' chooses by the enum {bound}, which isn't found", cue.At["enum"]);
			var fields = symbol.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst).Select(f => f.Name).ToList();
			var options = cue.Options.Select(o => o.Name).ToList();
			if (!fields.OrderBy(f => f, StringComparer.Ordinal).SequenceEqual(options.OrderBy(o => o, StringComparer.Ordinal)))
				throw new FormatError($"the cue '{cue.Id}' chooses among {string.Join(", ", options)}, and {bound} has {string.Join(", ", fields)}: they must be the same", cue.At["enum"]);
			type = "global::" + bound;
		}
		else
		{
			type = member + "Option";
			lines.AddRange(Doc(cue.About, 0));
			lines.Add($"public enum {type}");
			lines.Add("{");
			foreach (var (name, about) in cue.Options)
			{
				lines.AddRange(Doc(about, 1));
				lines.Add($"    {name},");
			}
			lines.Add("}");
			lines.Add("");
		}
		lines.AddRange(Doc(cue.About, 0));
		lines.Add($"public static Cue<{type}> {member} {{ get; }} = new({runtime}, {type}.{cue.Options[cue.InitialOption].Name});");
		return lines;
	}

	private static IEnumerable<string> Doc(string? about, int depth) =>
		about is null ? Enumerable.Empty<string>() : Code.Comment(about, new string(' ', 4 * depth));

	private static void Write(SourceProductionContext output, string name, MusicOptions options, string kind, ClassTree tree, Reporter reporter)
	{
		var source = new StringBuilder();
		source.AppendLine("// <auto-generated/>");
		source.AppendLine($"// Made from the project's music by Lunelle's music generator: each {kind} from its file");
		source.AppendLine("#nullable enable");
		source.AppendLine("using BinaryVibrance.Lunelle.Chip;");
		source.AppendLine("using BinaryVibrance.Lunelle.Music;");
		source.AppendLine("using BinaryVibrance.Sirrow.Dsp;");
		source.AppendLine();
		source.AppendLine($"namespace {options.RootNamespace};");
		source.AppendLine();
		foreach (var clash in tree.Write(source))
			reporter.Report(new FormatProblem(EntryIds.Manifest, $"two of the project's {kind}s are both {clash} in C#: rename one"));
		output.AddSource(name, source.ToString().Replace("\r\n", "\n"));
	}

	// Says a problem at its place, in whichever library's file it is
	private sealed class Reporter(SourceProductionContext output, LoadedProject project)
	{
		public void Report(FormatProblem problem)
		{
			var library = project.All.Where(l => l.Key.Length == 0 ? !problem.File.Contains(":") : problem.File.StartsWith(l.Key + ":", StringComparison.Ordinal))
				.OrderByDescending(l => l.Key.Length).FirstOrDefault() ?? project.Root;
			var path = library.Key.Length == 0 ? problem.File : problem.File.Substring(library.Key.Length + 1);
			Report(library, path, problem.Message, problem.Start, problem.Length, problem.IsWarning);
		}

		public void Report(EntryFile file, FormatError error) => Report(file.Owner, file.Path, error.Message, error.Start, error.Length, false);

		public void Report(ProjectLibrary owner, string label, FormatError error)
		{
			var path = owner.Key.Length > 0 && label.StartsWith(owner.Key + ":", StringComparison.Ordinal) ? label.Substring(owner.Key.Length + 1) : label;
			Report(owner, path, error.Message, error.Start, error.Length, false);
		}

		private void Report(ProjectLibrary library, string path, string message, int start, int length, bool warning)
		{
			var descriptor = warning ? Warning : Problem;
			if (library.Source is AdditionalSource source && source.Text(path) is { } text)
			{
				var span = new TextSpan(Math.Min(start, text.Length), Math.Min(length, Math.Max(0, text.Length - start)));
				output.ReportDiagnostic(Diagnostic.Create(descriptor, Location.Create(source.FullPath(path), span, text.Lines.GetLinePositionSpan(span)), $"{Path.GetFileName(path)}: {message}"));
			}
			else
				output.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, $"{library.FileLabel(path)}: {message}"));
		}
	}
}

/// <summary>Static classes nested by an id's segments, each holding the members written into it.</summary>
internal sealed class ClassTree(string name)
{
	private readonly SortedDictionary<string, ClassTree> _children = new(StringComparer.Ordinal);
	private readonly List<(string Name, Func<int, IEnumerable<string>> Lines)> _members = new();

	/// <summary>Members written into the outermost class as they are.</summary>
	public List<string> Root { get; } = new();

	public bool Empty => _children.Count == 0 && _members.Count == 0 && Root.Count == 0;

	/// <summary>A member at <paramref name="path"/> (classes, then its own name), its lines given its depth.</summary>
	public void Add(string[] path, Func<int, IEnumerable<string>> lines)
	{
		var node = this;
		for (var i = 0; i < path.Length - 1; i++)
		{
			if (!node._children.TryGetValue(path[i], out var child))
				node._children[path[i]] = child = new ClassTree(path[i]);
			node = child;
		}
		node._members.Add((path[path.Length - 1], lines));
	}

	/// <summary>Writes the classes; returns each name that is both a class and a member in one class (which C# refuses).</summary>
	public List<string> Write(StringBuilder text) => Write(text, 0, name);

	private List<string> Write(StringBuilder text, int depth, string path)
	{
		var clashes = new List<string>();
		var indent = new string(' ', 4 * depth);
		text.AppendLine($"{indent}{(depth == 0 ? "" : "public ")}static partial class {name}");
		text.AppendLine($"{indent}{{");
		var first = true;
		foreach (var (member, lines) in _members)
		{
			if (_children.ContainsKey(member))
				clashes.Add($"{path}.{member}");
			if (!first)
				text.AppendLine();
			first = false;
			foreach (var line in lines(depth + 1))
				foreach (var part in line.Split('\n'))
					text.AppendLine(part.TrimEnd());
		}
		foreach (var child in _children.Values)
		{
			if (!first)
				text.AppendLine();
			first = false;
			clashes.AddRange(child.Write(text, depth + 1, $"{path}.{child.Name}"));
		}
		foreach (var raw in Root)
		{
			if (!first)
				text.AppendLine();
			first = false;
			text.AppendLine(raw);
		}
		text.AppendLine($"{indent}}}");
		return clashes;
	}

	private string Name => name;
}
