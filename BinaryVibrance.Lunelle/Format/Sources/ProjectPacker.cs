using System.IO.Compression;

namespace BinaryVibrance.Lunelle.Format;

/// <summary>
/// Packs a project into a <c>.lunelle</c> file (project.md §6.1): its own files (never code, a build's output or the
/// workbench's), and every library it reaches bundled under <c>Libraries/&lt;name&gt;@&lt;version&gt;/</c> with only the
/// entries it uses, so the file stands alone. The zip is the same bytes every time for the same project: entries in
/// order, a fixed time, JSON deflated and samples stored, so <c>pack(unpack(f)) == f</c>.
/// </summary>
public static class ProjectPacker
{
	private static readonly DateTimeOffset Epoch = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>The packed file, and notes on what was left out (code) or plays a rest (a song's code).</summary>
	public sealed record Packed(byte[] Bytes, IReadOnlyList<string> Notes);

	/// <summary>Packs <paramref name="project"/>, resolving its references with <paramref name="resolver"/>.</summary>
	/// <exception cref="InvalidOperationException">Two libraries reached have one name and version but aren't one library.</exception>
	public static Packed Pack(LoadedProject project, Resolver resolver)
	{
		ArgumentNullException.ThrowIfNull(project);
		ArgumentNullException.ThrowIfNull(resolver);
		var notes = new List<string>();
		var root = project.Root;
		var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);

		// What each library is used for, from the project's own entries down
		var used = new Dictionary<ProjectLibrary, Use>();
		var walker = new Walker(resolver, used);
		foreach (var kind in new[] { EntryKind.Song, EntryKind.Patch, EntryKind.Kit, EntryKind.Pattern })
			foreach (var entry in root.Entries(kind).Values)
				walker.Visit(root, entry);

		// Each library bundled once by name and version, however many routes reach it
		var bundles = new Dictionary<string, (ProjectLibrary Library, Use Use)>(StringComparer.Ordinal);
		string Bundle(ProjectLibrary library) =>
			library.Manifest.Version is { } version ? $"{library.Name}@{version}" : library.Name;
		foreach (var (library, use) in used)
		{
			if (library == root)
				continue;
			var name = Bundle(library);
			if (bundles.TryGetValue(name, out var already))
			{
				if (already.Library.Source.Location != library.Source.Location && !Same(already.Library, library))
					throw new InvalidOperationException($"Two libraries are both {name} but aren't the same: {already.Library.Source.Location} and {library.Source.Location}.");
				already.Use.Merge(use);
			}
			else
				bundles[name] = (library, use.Copy());
		}

		// The project's own files
		foreach (var path in root.Source.Paths)
		{
			if (EntryIds.LeftOut(path) || path.StartsWith(EntryIds.Libraries, StringComparison.Ordinal))
				continue;
			if (EntryIds.IsCode(path))
			{
				notes.Add($"{path} is code, which a .lunelle doesn't hold: it is left out");
				continue;
			}
			files[path] = root.Source.ReadBytes(path);
		}
		foreach (var song in root.Entries(EntryKind.Song).Values)
			if (Code(song.Json).Count > 0)
				notes.Add($"{song.Path} names code ({string.Join(", ", Code(song.Json))}), which plays a rest from the .lunelle");
		if (root.Manifest.Libraries.Count > 0 && root.Manifest.Json.Members.Count > 0)
			files[EntryIds.Manifest] = Text(Manifest(root, used.GetValueOrDefault(root), other => $"{EntryIds.Libraries}{Bundle(other)}", asLibrary: false));

		// Each library's used entries, and its manifest pointing at its own libraries beside it
		foreach (var (name, (library, use)) in bundles)
		{
			var folder = $"{EntryIds.Libraries}{name}/";
			files[folder + EntryIds.Manifest] = Text(Manifest(library, use, other => $"../{Bundle(other)}", asLibrary: true));
			foreach (var path in use.Paths(library))
				files[folder + path] = library.Source.ReadBytes(path);
		}
		return new Packed(Zip(files), notes);
	}

	/// <summary>Writes <paramref name="files"/> as a zip, deterministically: in order, at 1980-01-01, JSON and text deflated, samples stored.</summary>
	public static byte[] Zip(IEnumerable<KeyValuePair<string, byte[]>> files)
	{
		using var stream = new MemoryStream();
		using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
			foreach (var (path, bytes) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
			{
				var sample = path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)
					|| path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
				var entry = zip.CreateEntry(path, sample ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
				entry.LastWriteTime = Epoch;
				using var into = entry.Open();
				into.Write(bytes);
			}
		return stream.ToArray();
	}

	// A manifest as bundled: its libraries those still used, each at its bundle; a library's without its songs or album
	private static JObject Manifest(ProjectLibrary library, Use? use, Func<ProjectLibrary, string> path, bool asLibrary)
	{
		var json = (JObject)library.Manifest.Json.Clone();
		if (asLibrary)
		{
			json.Remove("songs");
			json.Remove("album");
		}
		var libraries = new JObject();
		foreach (var reference in library.Manifest.Libraries)
		{
			if (!library.Libraries.TryGetValue(reference.LocalName, out var other) || other is null)
				continue;
			if (use is not null && !use.Libraries.Contains(reference.LocalName))
				continue;
			var entry = new JObject().Set("path", new JString(path(other)));
			if (other.Manifest.Version is { } version)
				entry.Set("version", new JString(version));
			libraries.Set(reference.LocalName, entry);
		}
		if (libraries.Members.Count > 0)
			json.Set("libraries", libraries);
		else
			json.Remove("libraries");
		return json;
	}

	private static byte[] Text(JObject json) => System.Text.Encoding.UTF8.GetBytes(JWriter.Write(json) + "\n");

	// Whether two libraries at one name and version hold the same files (the same library reached two ways)
	private static bool Same(ProjectLibrary a, ProjectLibrary b) =>
		a.Source.Paths.SequenceEqual(b.Source.Paths)
		&& a.Source.Paths.Where(p => p.EndsWith(".json", StringComparison.Ordinal)).All(p => a.Source.ReadText(p) == b.Source.ReadText(p));

	private static List<string> Code(JObject song)
	{
		var code = new List<string>();
		foreach (var section in song["sections"]?.Items ?? [])
			foreach (var part in section["parts"]?.Items ?? [])
			{
				if (part["code"]?.Str is { } placement)
					code.Add($"placement {placement}");
				if (part["part"]?["code"]?.Str is { } name)
					code.Add($"part {name}");
			}
		foreach (var hook in song["hooks"]?.Items ?? [])
			if (hook.Str is { } name)
				code.Add($"hook {name}");
		return code;
	}

	// What a library is used for: its entries, its cues, and which of its own libraries those reach
	private sealed class Use
	{
		public HashSet<(EntryKind, string)> Entries { get; } = [];
		public HashSet<string> Cues { get; } = new(StringComparer.Ordinal);
		public HashSet<string> Libraries { get; } = new(StringComparer.Ordinal);

		public Use Copy()
		{
			var copy = new Use();
			copy.Merge(this);
			return copy;
		}

		public void Merge(Use other)
		{
			Entries.UnionWith(other.Entries);
			Cues.UnionWith(other.Cues);
			Libraries.UnionWith(other.Libraries);
		}

		// Every file these come from: each entry's, a kit's whole folder (its samples, licence and notes), each cue's file
		public IEnumerable<string> Paths(ProjectLibrary library)
		{
			var paths = new SortedSet<string>(StringComparer.Ordinal);
			foreach (var (kind, id) in Entries)
			{
				if (library.Entry(kind, id) is not { } entry)
					continue;
				paths.Add(entry.Path);
				if (kind == EntryKind.Kit && entry.Path.EndsWith("/kit.json", StringComparison.Ordinal))
					foreach (var path in library.Source.Paths)
						if (path.StartsWith(entry.Folder, StringComparison.Ordinal))
							paths.Add(path);
			}
			foreach (var cue in Cues)
				if (library.Cues.TryGetValue(cue, out var definition))
					paths.Add(definition.File.Contains(':') ? definition.File[(definition.File.LastIndexOf(':') + 1)..] : definition.File);
			return paths;
		}
	}

	// Follows every reference from the project's entries, into the libraries and on through theirs
	private sealed class Walker(Resolver resolver, Dictionary<ProjectLibrary, Use> used)
	{
		private Use UseOf(ProjectLibrary library)
		{
			if (!used.TryGetValue(library, out var use))
				used[library] = use = new Use();
			return use;
		}

		public void Visit(ProjectLibrary library, EntryFile entry)
		{
			if (!UseOf(library).Entries.Add((entry.Kind, entry.Id)))
				return;
			var json = entry.Json;
			if (json["base"]?.Str is { } baseRef)
				Reference(library, entry.Kind, baseRef);
			switch (entry.Kind)
			{
				case EntryKind.Kit:
					Patches(library, json);
					break;
				case EntryKind.Pattern:
					foreach (var item in json["sequence"]?.Items ?? [])
						if (item["pattern"]?.Str is { } pattern)
							Reference(library, EntryKind.Pattern, pattern);
					break;
				case EntryKind.Song:
					foreach (var kind in new[] { EntryKind.Patch, EntryKind.Kit })
						if (json[kind == EntryKind.Patch ? "patches" : "kits"] is JObject local)
							foreach (var member in local.Members)
							{
								if (member.Value["base"]?.Str is { } localBase)
									Reference(library, kind, localBase, json);
								else
									Reference(library, kind, member.Key, json);
								Patches(library, member.Value);
							}
					foreach (var track in json["tracks"]?.Items ?? [])
					{
						if (track["patch"]?.Str is { } patch)
							Reference(library, EntryKind.Patch, patch, json);
						if (track["kit"]?.Str is { } kit)
							Reference(library, EntryKind.Kit, kit, json);
					}
					if (json["cues"] is JObject cues)
						foreach (var member in cues.Members)
							if (resolver.FindCue(library, member.Key, out _) is { } cue)
							{
								UseOf(cue.Owner).Cues.Add(cue.Cue.Id);
								if (member.Key.IndexOf(':') is var colon and > 0)
									UseOf(library).Libraries.Add(member.Key[..colon]);
							}
					foreach (var section in json["sections"]?.Items ?? [])
						foreach (var part in section["parts"]?.Items ?? [])
						{
							if (part["pattern"]?.Str is { } pattern)
								Reference(library, EntryKind.Pattern, pattern);
							if (part["endWith"]?["pattern"]?.Str is { } fill)
								Reference(library, EntryKind.Pattern, fill);
						}
					break;
			}
		}

		// Every "patch" a kit (or a change to one) names
		private void Patches(ProjectLibrary library, JNode node)
		{
			if (node is JObject obj)
				foreach (var member in obj.Members)
				{
					if (member.Key == "patch" && member.Value.Str is { } patch)
						Reference(library, EntryKind.Patch, patch);
					else
						Patches(library, member.Value);
				}
			else
				foreach (var item in node.Items)
					Patches(library, item);
		}

		private void Reference(ProjectLibrary from, EntryKind kind, string reference, JObject? song = null)
		{
			// A song's own sound is found before the project's
			if (song is not null && song[kind == EntryKind.Patch ? "patches" : "kits"]?[reference]?["base"] is not null)
				return;
			if (resolver.Find(from, kind, reference, out _) is not { } entry)
				return;
			if (reference.IndexOf(':') is var colon and > 0)
				UseOf(from).Libraries.Add(reference[..colon]);
			Visit(entry.Owner, entry);
		}
	}
}
