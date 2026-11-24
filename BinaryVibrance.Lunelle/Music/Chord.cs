using System.Text;

namespace BinaryVibrance.Lunelle.Music;

/// <summary>
/// A chord on a degree of the scale, written as a Roman numeral and a chord symbol's suffix: <c>i</c>, <c>VI5</c>,
/// <c>bVII</c>, <c>V7</c>, <c>ivsus4</c>, <c>Imaj9</c>, <c>vim9</c>, <c>VIM7b9</c>, <c>V13</c>, <c>I6/9</c>. The numeral's
/// case means nothing: the scale decides whether a triad is major or minor, and which seventh it takes, unless the suffix
/// says.
/// </summary>
/// <param name="Degree">The degree of its root, 1 to 7.</param>
/// <param name="Kind">The triad it stacks on its root.</param>
/// <param name="Accidental">Semitones its root is moved off the scale: -1 for a flat.</param>
/// <param name="Extension">What it adds over the triad.</param>
/// <param name="MajorSeventh">Whether its seventh is the major one whatever the scale (<c>maj</c>).</param>
/// <param name="Alterations">Its fifth, ninth, eleventh or thirteenth moved a semitone.</param>
public readonly record struct Chord(
	int Degree,
	ChordKind Kind = ChordKind.Triad,
	int Accidental = 0,
	ChordExtension Extension = ChordExtension.None,
	bool MajorSeventh = false,
	ChordAlterations Alterations = ChordAlterations.None)
{
	private static readonly string[] Numerals = ["vii", "iii", "vi", "iv", "ii", "v", "i"];
	private static readonly int[] NumeralDegrees = [7, 3, 6, 4, 2, 5, 1];

	// Longest first, so 6/9 isn't read as 6 and 13 isn't read as 1
	private static readonly (string Text, ChordExtension Extension)[] Extensions =
	[
		("6/9", ChordExtension.SixNine), ("69", ChordExtension.SixNine), ("13", ChordExtension.Thirteenth),
		("11", ChordExtension.Eleventh), ("9", ChordExtension.Ninth), ("7", ChordExtension.Seventh), ("6", ChordExtension.Sixth),
	];

	private static readonly (string Text, ChordAlterations Alteration)[] AlterationNames =
	[
		("b5", ChordAlterations.FlatFive), ("#5", ChordAlterations.SharpFive), ("b9", ChordAlterations.FlatNine),
		("#9", ChordAlterations.SharpNine), ("#11", ChordAlterations.SharpEleven), ("b13", ChordAlterations.FlatThirteen),
	];

	/// <summary>
	/// A chord from its token: <c>b</c> or <c>#</c> as often as wanted, a numeral <c>I</c> to <c>VII</c> in either case,
	/// then its suffix, each part optional and in this order: a quality (<c>M</c>, <c>m</c> or <c>dim</c>; or <c>5</c>
	/// alone, a power chord); an extension (<c>6</c>, <c>6/9</c>, <c>7</c>, <c>9</c>, <c>11</c> or <c>13</c>, <c>maj</c>
	/// before the last four for the major seventh); <c>sus2</c> or <c>sus4</c>; <c>add9</c>; and alterations (<c>b5</c>,
	/// <c>#5</c>, <c>b9</c>, <c>#9</c>, <c>#11</c>, <c>b13</c>), in brackets or not. A quality makes the seventh a
	/// minor one (diminished on <c>dim</c>) whatever the scale; without one the scale's own seventh is taken.
	/// </summary>
	/// <exception cref="NotationException">The token isn't a chord.</exception>
	public static Chord Parse(string token)
	{
		ArgumentNullException.ThrowIfNull(token);
		var at = 0;
		var accidental = 0;
		while (at < token.Length && token[at] is 'b' or '#')
			accidental += token[at++] == 'b' ? -1 : 1;
		var degree = 0;
		for (var n = 0; n < Numerals.Length; n++)
			if (token.AsSpan(at).StartsWith(Numerals[n], StringComparison.OrdinalIgnoreCase))
			{
				degree = NumeralDegrees[n];
				at += Numerals[n].Length;
				break;
			}
		if (degree == 0)
			throw new NotationException($"Chord \"{token}\": it has no numeral I to VII (column {at + 1})", token, 0, at + 1);

		if (Take(token, ref at, "5"))
			return at == token.Length
				? new Chord(degree, ChordKind.Power, accidental)
				: throw Wrong(token, at, "a power chord (5) takes nothing after it");

		var kind = ChordKind.Triad;
		if (!token.AsSpan(at).StartsWith("maj", StringComparison.Ordinal))
		{
			if (Take(token, ref at, "dim"))
				kind = ChordKind.Diminished;
			else if (Take(token, ref at, "M"))
				kind = ChordKind.Major;
			else if (Take(token, ref at, "m"))
				kind = ChordKind.Minor;
		}

		var majorAt = at;
		var major = Take(token, ref at, "maj");
		var extension = ChordExtension.None;
		foreach (var (text, made) in Extensions)
			if (Take(token, ref at, text))
			{
				extension = made;
				break;
			}
		if (major && extension is not (ChordExtension.Seventh or ChordExtension.Ninth or ChordExtension.Eleventh or ChordExtension.Thirteenth))
			throw Wrong(token, majorAt, "maj is followed by 7, 9, 11 or 13");

		var susAt = at;
		var sus = Take(token, ref at, "sus4") ? ChordKind.Sus4
			: Take(token, ref at, "sus2") ? ChordKind.Sus2
			: Take(token, ref at, "sus") ? ChordKind.Sus4
			: kind;
		if (sus != kind)
		{
			if (kind is not (ChordKind.Triad or ChordKind.Major))
				throw Wrong(token, susAt, "a sus chord has no third to be minor or diminished");
			kind = sus;
		}

		var addAt = at;
		if (Take(token, ref at, "add9"))
		{
			if (extension != ChordExtension.None)
				throw Wrong(token, addAt, "add9 is for a chord with no other extension; 9 or 6/9 have it already");
			extension = ChordExtension.Add9;
		}

		var alterations = ChordAlterations.None;
		while (at < token.Length)
		{
			if (token[at] is '(' or ')' or ',')
			{
				at++;
				continue;
			}
			var found = false;
			foreach (var (text, alteration) in AlterationNames)
				if (Take(token, ref at, text))
				{
					alterations |= alteration;
					found = true;
					break;
				}
			if (!found)
				throw Wrong(token, at, "it isn't a quality (M, m, dim, 5), an extension (6, 6/9, 7, 9, 11, 13, maj7, maj9, "
					+ "maj11, maj13), sus2, sus4, add9 or an alteration (b5, #5, b9, #9, #11, b13), in that order");
		}
		return new Chord(degree, kind, accidental, extension, major, alterations);
	}

	private static bool Take(string token, ref int at, string text)
	{
		if (!token.AsSpan(at).StartsWith(text, StringComparison.Ordinal))
			return false;
		at += text.Length;
		return true;
	}

	private static NotationException Wrong(string token, int at, string why) =>
		new($"Chord \"{token}\": \"{token[at..]}\": {why} (column {at + 1})", token, 0, at + 1);

	/// <summary>Its suffix as <see cref="Parse"/> reads it: "", <c>m7</c>, <c>maj9</c>, <c>M7b9</c>, <c>7sus4</c>.</summary>
	public string Suffix
	{
		get
		{
			if (Kind == ChordKind.Power)
				return "5";
			var text = new StringBuilder(Kind switch
			{
				ChordKind.Major => "M",
				ChordKind.Minor => "m",
				ChordKind.Diminished => "dim",
				_ => "",
			});
			if (MajorSeventh)
				text.Append("maj");
			text.Append(Extension switch
			{
				ChordExtension.Sixth => "6",
				ChordExtension.SixNine => "6/9",
				ChordExtension.Seventh => "7",
				ChordExtension.Ninth => "9",
				ChordExtension.Eleventh => "11",
				ChordExtension.Thirteenth => "13",
				_ => "",
			});
			if (Kind == ChordKind.Sus2)
				text.Append("sus2");
			else if (Kind == ChordKind.Sus4)
				text.Append("sus4");
			if (Extension == ChordExtension.Add9)
				text.Append("add9");
			foreach (var (name, alteration) in AlterationNames)
				if (Alterations.HasFlag(alteration))
					text.Append(name);
			return text.ToString();
		}
	}

	/// <summary>The pitch class of its root in <paramref name="scale"/>, 0 to 11.</summary>
	public int RootClass(Scale scale) => (((int)scale.Degree(Degree, 0, Accidental).Midi % 12) + 12) % 12;

	/// <summary>
	/// The semitones of its notes above its root in <paramref name="scale"/>, rising, the first 0: past 12 for a ninth,
	/// an eleventh or a thirteenth.
	/// </summary>
	public int[] Intervals(Scale scale)
	{
		if (Kind == ChordKind.Power)
			return [0, 7];
		var r = RootClass(scale);
		var notes = new List<int> { 0 };
		var (third, fifth) = Kind switch
		{
			ChordKind.Major => (4, 7),
			ChordKind.Minor => (3, 7),
			ChordKind.Diminished => (3, 6),
			ChordKind.Sus2 => (2, 7),
			ChordKind.Sus4 => (5, 7),
			_ => (scale.Contains(r + 4) ? 4 : scale.Contains(r + 3) ? 3 : -1,
				scale.Contains(r + 7) ? 7 : scale.Contains(r + 6) ? 6 : 7),
		};
		if (Alterations.HasFlag(ChordAlterations.FlatFive))
			fifth = 6;
		else if (Alterations.HasFlag(ChordAlterations.SharpFive))
			fifth = 8;
		if (third >= 0)
			notes.Add(third);
		notes.Add(fifth);

		if (Extension is ChordExtension.Sixth or ChordExtension.SixNine)
			notes.Add(9);
		if (Extension >= ChordExtension.Seventh)
			notes.Add(MajorSeventh ? 11 : Kind switch
			{
				ChordKind.Diminished => 9,
				ChordKind.Triad => scale.Contains(r + 10) ? 10 : scale.Contains(r + 11) ? 11 : 10,
				_ => 10,
			});

		var alteredNinth = false;
		if (Alterations.HasFlag(ChordAlterations.FlatNine))
		{
			notes.Add(13);
			alteredNinth = true;
		}
		if (Alterations.HasFlag(ChordAlterations.SharpNine))
		{
			notes.Add(15);
			alteredNinth = true;
		}
		if (!alteredNinth && Extension is ChordExtension.SixNine or ChordExtension.Add9 or ChordExtension.Ninth
			or ChordExtension.Eleventh or ChordExtension.Thirteenth)
			notes.Add(14);

		if (Alterations.HasFlag(ChordAlterations.SharpEleven))
			notes.Add(18);
		else if (Extension == ChordExtension.Eleventh)
			notes.Add(17);

		if (Alterations.HasFlag(ChordAlterations.FlatThirteen))
			notes.Add(20);
		else if (Extension == ChordExtension.Thirteenth)
			notes.Add(21);

		return [.. notes.Distinct().Order()];
	}
}
