using BinaryVibrance.Lunelle.Music.Generator.Format;

using Microsoft.CodeAnalysis.Text;

namespace BinaryVibrance.Lunelle.Music.Generator;

/// <summary>
/// A project's files as the generator has them: the compilation's additional files under one folder, by their paths
/// relative to it. A library is another folder's (its <c>"path"</c> from this one's root), when its files are additional
/// files too: Lunelle's MSBuild adds a project's music and its libraries' (build/BinaryVibrance.Lunelle.props).
/// </summary>
internal sealed class AdditionalSource : IProjectSource
{
	private readonly IReadOnlyDictionary<string, SourceText> _all;

	/// <summary>The files under <paramref name="root"/> among <paramref name="all"/>, by full path.</summary>
	public AdditionalSource(string root, IReadOnlyDictionary<string, SourceText> all)
	{
		_all = all;
		Root = Full(root);
		var prefix = Root + Path.DirectorySeparatorChar;
		Paths = all.Keys.Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			.Select(p => p.Substring(prefix.Length).Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/'))
			.OrderBy(p => p, StringComparer.Ordinal).ToList();
	}

	/// <summary>The folder, in full, without a separator at its end.</summary>
	public string Root { get; }

	/// <inheritdoc/>
	public string Location => Root;

	/// <inheritdoc/>
	public IReadOnlyList<string> Paths { get; }

	/// <summary>The full path of the file at <paramref name="path"/>.</summary>
	public string FullPath(string path) => Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));

	/// <summary>The file's text, as the compilation has it, for a diagnostic's place.</summary>
	public SourceText? Text(string path) => _all.TryGetValue(FullPath(path), out var text) ? text : null;

	/// <inheritdoc/>
	public string ReadText(string path) =>
		Text(path)?.ToString() ?? throw new IOException($"{path} isn't among the project's additional files.");

	/// <inheritdoc/>
	public byte[] ReadBytes(string path) => throw new IOException("The generator reads no samples.");

	/// <inheritdoc/>
	public IProjectSource? Open(string path)
	{
		var root = Full(Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar)));
		return _all.ContainsKey(Path.Combine(root, EntryIds.Manifest)) ? new AdditionalSource(root, _all) : null;
	}

	/// <summary><paramref name="path"/> in full, without a separator at its end.</summary>
	public static string Full(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
