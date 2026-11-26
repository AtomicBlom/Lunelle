#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>Something wrong at a place in a music file's text: thrown while reading one value, then reported as a <see cref="FormatProblem"/>.</summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class FormatError : Exception
{
	/// <summary>What is wrong, at <paramref name="start"/> for <paramref name="length"/> characters of the file.</summary>
	public FormatError(string message, int start, int length)
		: base(message)
	{
		Start = start;
		Length = length;
	}

	/// <summary>What is wrong, at <paramref name="at"/>'s place.</summary>
	public FormatError(string message, JNode? at)
		: this(message, at?.Start ?? 0, at?.Length ?? 0)
	{
	}

	/// <summary>Where it starts in the file's text.</summary>
	public int Start { get; }

	/// <summary>How much of the text it covers.</summary>
	public int Length { get; }
}
