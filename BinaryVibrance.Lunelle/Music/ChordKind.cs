namespace BinaryVibrance.Lunelle.Music;

/// <summary>The triad a <see cref="Chord"/> stacks its extension on.</summary>
public enum ChordKind
{
	/// <summary>The scale's own triad on that degree: major, minor or diminished as the scale makes it.</summary>
	Triad,
	/// <summary>A major triad whatever the scale: 0 4 7.</summary>
	Major,
	/// <summary>A minor triad whatever the scale: 0 3 7.</summary>
	Minor,
	/// <summary>A power chord, root and fifth: 0 7.</summary>
	Power,
	/// <summary>The second instead of the third: 0 2 7.</summary>
	Sus2,
	/// <summary>The fourth instead of the third: 0 5 7.</summary>
	Sus4,
	/// <summary>A diminished triad: 0 3 6; its seventh is the diminished one.</summary>
	Diminished,
}
