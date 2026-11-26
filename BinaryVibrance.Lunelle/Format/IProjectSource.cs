#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>
/// Where a project's files are, as format 2 reads them (project.md §1): a list of paths relative to its root (<c>/</c>
/// between folders) and a way to read each, so a folder, a <c>.lunelle</c> zip, embedded resources, a browser's
/// storage or a compilation's additional files all load alike.
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
interface IProjectSource
{
	/// <summary>Where it is, for messages, and to tell one project from another in a cycle of libraries.</summary>
	string Location { get; }

	/// <summary>Every file's path, relative to the root.</summary>
	IReadOnlyList<string> Paths { get; }

	/// <summary>The text of the file at <paramref name="path"/>.</summary>
	/// <exception cref="IOException">It can't be read.</exception>
	string ReadText(string path);

	/// <summary>The bytes of the file at <paramref name="path"/>: a kit's samples.</summary>
	/// <exception cref="IOException">It can't be read.</exception>
	byte[] ReadBytes(string path);

	/// <summary>
	/// The project at <paramref name="path"/>, relative to this one's root (a library's <c>"path"</c>): a folder, or a
	/// <c>.lunelle</c> file. Null when there is none there.
	/// </summary>
	IProjectSource? Open(string path);
}
