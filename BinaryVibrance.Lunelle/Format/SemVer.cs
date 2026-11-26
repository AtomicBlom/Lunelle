using System.Globalization;

#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>
/// A version as a manifest writes it: <c>major[.minor[.patch]]</c>, with an optional <c>-prerelease</c> that is kept but
/// not compared. A library pinned at <c>"1.2"</c> takes any 1.x from 1.2 (project.md §4.2).
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
readonly struct SemVer
{
	/// <summary>A version.</summary>
	public SemVer(int major, int minor, int patch, int parts, string text)
	{
		Major = major;
		Minor = minor;
		Patch = patch;
		Parts = parts;
		Text = text;
	}

	/// <summary>The major version.</summary>
	public int Major { get; }

	/// <summary>The minor version (0 where not written).</summary>
	public int Minor { get; }

	/// <summary>The patch version (0 where not written).</summary>
	public int Patch { get; }

	/// <summary>How many of the three were written.</summary>
	public int Parts { get; }

	/// <summary>The text as written.</summary>
	public string Text { get; }

	/// <summary>Reads <paramref name="text"/>; false when it isn't a version.</summary>
	public static bool TryParse(string text, out SemVer version)
	{
		version = default;
		var core = text;
		var dash = text.IndexOf('-');
		if (dash >= 0)
			core = text.Substring(0, dash);
		var plus = core.IndexOf('+');
		if (plus >= 0)
			core = core.Substring(0, plus);
		var parts = core.Split('.');
		if (parts.Length is < 1 or > 3)
			return false;
		var numbers = new int[3];
		for (var i = 0; i < parts.Length; i++)
			if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
				return false;
		version = new SemVer(numbers[0], numbers[1], numbers[2], parts.Length, text);
		return true;
	}

	/// <summary>
	/// Whether <paramref name="provided"/> meets this as a least version within its major: the same major, and no older
	/// in what this writes.
	/// </summary>
	public bool AcceptedBy(SemVer provided)
	{
		if (provided.Major != Major)
			return false;
		if (Parts >= 2 && provided.Minor < Minor)
			return false;
		return Parts < 3 || provided.Minor > Minor || provided.Patch >= Patch;
	}

	/// <summary>The text as written.</summary>
	public override string ToString() => Text;
}
