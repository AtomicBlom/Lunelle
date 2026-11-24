namespace BinaryVibrance.Lunelle.Music;

/// <summary>Notes of a <see cref="Chord"/> moved a semitone off their natural place, or added so moved.</summary>
[Flags]
public enum ChordAlterations
{
	/// <summary>None.</summary>
	None = 0,
	/// <summary>The fifth lowered: <c>b5</c>.</summary>
	FlatFive = 1,
	/// <summary>The fifth raised: <c>#5</c>.</summary>
	SharpFive = 2,
	/// <summary>The ninth lowered, in place of the natural one: <c>b9</c>.</summary>
	FlatNine = 4,
	/// <summary>The ninth raised, in place of the natural one: <c>#9</c>.</summary>
	SharpNine = 8,
	/// <summary>The eleventh raised: <c>#11</c>.</summary>
	SharpEleven = 16,
	/// <summary>The thirteenth lowered, in place of the natural one: <c>b13</c>.</summary>
	FlatThirteen = 32,
}
