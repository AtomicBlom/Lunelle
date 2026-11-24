namespace BinaryVibrance.Lunelle.Music;

/// <summary>How a <see cref="Strum"/> lays a chord across a guitar's six strings.</summary>
public enum Voicing
{
	/// <summary>An open chord, as a guitar plays it in the first frets: the bass on the lowest string that has the root.</summary>
	Open,
	/// <summary>A power chord: root, fifth and octave.</summary>
	Power,
	/// <summary>The power chord with the third above its octave.</summary>
	Triad,
	/// <summary>
	/// The chord's notes stacked up from its root within an octave, the root in the track's octave: as a hand plays it on
	/// keys, not across strings. With no spread, a stroke sounds as a block chord.
	/// </summary>
	Close,
	/// <summary>
	/// As keys play over a bass that has the root: the chord without its root, and without its fifth where four notes
	/// are left, moved by octaves until its lowest note is from D to C# of the track's octave, so one chord leads to the
	/// next.
	/// </summary>
	Rootless,
}
