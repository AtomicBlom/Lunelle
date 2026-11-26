using System.IO.Compression;

namespace BinaryVibrance.Lunelle.Format;

// Lunelle's own, not the generator's: the generator reads a project from its compilation's additional files

/// <summary>
/// A project's files given as a list of paths and a function to read one (project.md §1): embedded resources, a
/// browser's storage, or a test's dictionary. A library inside it (<c>Libraries/…</c>) opens as a view of the same files.
/// </summary>
public class MemorySource : IProjectSource
{
	private readonly Func<string, byte[]> _read;
	private readonly string _prefix;
	private readonly string _rootLocation;

	/// <summary>The files <paramref name="paths"/>, read by <paramref name="read"/> by their paths as given.</summary>
	public MemorySource(IEnumerable<string> paths, Func<string, byte[]> read, string location = "the project")
		: this(Normalize(paths), read, location, "", location)
	{
	}

	private protected MemorySource(IReadOnlyList<string> all, Func<string, byte[]> read, string location, string prefix, string rootLocation)
	{
		All = all;
		_read = read;
		_prefix = prefix;
		_rootLocation = rootLocation;
		Location = location;
		Paths = prefix.Length == 0
			? all
			: [.. all.Where(p => p.StartsWith(prefix, StringComparison.Ordinal)).Select(p => p[prefix.Length..])];
	}

	/// <summary>Every path in the whole source, whatever view this is.</summary>
	private protected IReadOnlyList<string> All { get; }

	/// <inheritdoc/>
	public string Location { get; }

	/// <inheritdoc/>
	public IReadOnlyList<string> Paths { get; }

	/// <inheritdoc/>
	public string ReadText(string path) => System.Text.Encoding.UTF8.GetString(ReadBytes(path));

	/// <inheritdoc/>
	public byte[] ReadBytes(string path)
	{
		try
		{
			return _read(_prefix + path);
		}
		catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException or UnauthorizedAccessException or InvalidDataException)
		{
			throw new IOException($"{path} can't be read: {ex.Message}", ex);
		}
	}

	/// <inheritdoc/>
	public virtual IProjectSource? Open(string path)
	{
		if (Resolver.Normalize(_prefix + path) is not { Length: > 0 } inside)
			return null;
		var folder = inside + "/";
		return All.Contains(folder + EntryIds.Manifest) ? new MemorySource(All, _read, $"{_rootLocation}/{inside}", folder, _rootLocation) : null;
	}

	private static string[] Normalize(IEnumerable<string> paths)
	{
		ArgumentNullException.ThrowIfNull(paths);
		return [.. paths.Select(p => p.Replace('\\', '/')).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
	}
}

/// <summary>A project in a folder on disk: its files, and libraries beside it by their relative paths (a folder or a <c>.lunelle</c>).</summary>
public sealed class DirectorySource : IProjectSource
{
	private readonly string _root;

	/// <summary>The project in <paramref name="directory"/>.</summary>
	/// <exception cref="DirectoryNotFoundException">There is no such folder.</exception>
	public DirectorySource(string directory)
	{
		ArgumentNullException.ThrowIfNull(directory);
		_root = Path.GetFullPath(directory);
		if (!Directory.Exists(_root))
			throw new DirectoryNotFoundException($"There is no folder {_root}.");
		Location = _root;
		Paths = [.. Walk(_root, "").Order(StringComparer.Ordinal)];
	}

	/// <inheritdoc/>
	public string Location { get; }

	/// <inheritdoc/>
	public IReadOnlyList<string> Paths { get; }

	/// <inheritdoc/>
	public string ReadText(string path) => File.ReadAllText(Full(path));

	/// <inheritdoc/>
	public byte[] ReadBytes(string path) => File.ReadAllBytes(Full(path));

	/// <inheritdoc/>
	public IProjectSource? Open(string path)
	{
		var full = Path.GetFullPath(Path.Combine(_root, path));
		if (Directory.Exists(full) && File.Exists(Path.Combine(full, EntryIds.Manifest)))
			return new DirectorySource(full);
		if (File.Exists(full) && full.EndsWith(".lunelle", StringComparison.OrdinalIgnoreCase))
		{
			try
			{
				return ZipSource.Read(File.ReadAllBytes(full), full);
			}
			catch (InvalidDataException)
			{
				return null;
			}
		}
		return null;
	}

	private string Full(string path) => Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));

	// Every file, skipping the folders a project never holds (a build's output, git's, the workbench's)
	private static IEnumerable<string> Walk(string folder, string relative)
	{
		foreach (var file in Directory.EnumerateFiles(folder))
			yield return relative + Path.GetFileName(file);
		foreach (var sub in Directory.EnumerateDirectories(folder))
		{
			var name = Path.GetFileName(sub);
			if (relative.Length == 0 && name is "bin" or "obj" or ".git" or ".lunelle-studio" or ".vs")
				continue;
			foreach (var file in Walk(sub, relative + name + "/"))
				yield return file;
		}
	}
}

/// <summary>
/// A packed project, a <c>.lunelle</c> file, read without unpacking it (project.md §6.3). Read defensively: an entry
/// whose path is absolute, holds <c>..</c> or a <c>\</c> is refused. A zip with no manifest at its root but one in its
/// single top-level folder is a zipped project folder, read from that folder.
/// </summary>
public sealed class ZipSource : MemorySource
{
	private ZipSource(IReadOnlyList<string> all, Dictionary<string, byte[]> files, string location, string prefix)
		: base(all, path => files.TryGetValue(path, out var bytes) ? bytes : throw new KeyNotFoundException($"{path} isn't in the file."), location, prefix, location)
	{
	}

	/// <summary>The project in the <c>.lunelle</c> file <paramref name="bytes"/>; <paramref name="location"/> names it in messages.</summary>
	/// <exception cref="InvalidDataException">It isn't a zip, holds a path that would leave it, or holds no project.</exception>
	public static ZipSource Read(byte[] bytes, string location = "the .lunelle file")
	{
		ArgumentNullException.ThrowIfNull(bytes);
		var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
		try
		{
			using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
			foreach (var entry in zip.Entries)
			{
				if (entry.FullName.EndsWith('/'))
					continue;
				var name = entry.FullName;
				if (name.Contains('\\') || name.StartsWith('/') || name.Contains(':') || Resolver.Normalize(name) != name)
					throw new InvalidDataException($"{location} holds \"{name}\", a path that would leave the project.");
				using var stream = entry.Open();
				using var copy = new MemoryStream();
				stream.CopyTo(copy);
				files[name] = copy.ToArray();
			}
		}
		catch (Exception ex) when (ex is InvalidDataException or IOException && ex is not FileNotFoundException)
		{
			throw ex as InvalidDataException ?? new InvalidDataException($"{location} isn't a zip: {ex.Message}", ex);
		}
		var all = files.Keys.Order(StringComparer.Ordinal).ToArray();
		if (files.ContainsKey(EntryIds.Manifest))
			return new ZipSource(all, files, location, "");
		var roots = all.Where(p => !p.StartsWith("__MACOSX/", StringComparison.Ordinal)).Select(p => p.Split('/')[0]).Distinct().ToList();
		if (roots.Count == 1 && files.ContainsKey($"{roots[0]}/{EntryIds.Manifest}"))
			return new ZipSource(all, files, location, roots[0] + "/");
		throw new InvalidDataException($"{location} holds no {EntryIds.Manifest} at its root: it isn't Lunelle music.");
	}
}
