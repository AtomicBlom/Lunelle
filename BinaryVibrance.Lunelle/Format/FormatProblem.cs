#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>
/// An error or a warning about a project's files: the file it is in (its path in its project, <c>/</c> between folders,
/// under <c>&lt;library&gt;:</c> for one in a library), where in the file's text, and what. An error leaves out only
/// what it touches: the rest of the project still loads.
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class FormatProblem
{
	/// <summary>A problem.</summary>
	public FormatProblem(string file, string message, int start = 0, int length = 0, bool warning = false, string? missing = null)
	{
		File = file;
		Message = message;
		Start = start;
		Length = length;
		IsWarning = warning;
		Missing = missing;
	}

	/// <summary>
	/// For a reference to a sound that isn't there, what it names: <c>patch:lead</c>, <c>kit:drums</c>. A project with code
	/// may have that sound in C#, which the generator finds where the resolver can't.
	/// </summary>
	public string? Missing { get; }

	/// <summary>The file, as its project names it (<c>Songs/aurora.song.json</c>), or <c>lunelle:Patches/x.patch.json</c> in a library.</summary>
	public string File { get; }

	/// <summary>What is wrong.</summary>
	public string Message { get; }

	/// <summary>Where it starts in the file's text (0 for the file as a whole).</summary>
	public int Start { get; }

	/// <summary>How much of the text it covers.</summary>
	public int Length { get; }

	/// <summary>Whether it is only a warning: what it concerns still loads.</summary>
	public bool IsWarning { get; }

	/// <summary>"file: message", and "warning: " before a warning's message.</summary>
	public override string ToString() => $"{File}: {(IsWarning ? "warning: " : "")}{Message}";
}
