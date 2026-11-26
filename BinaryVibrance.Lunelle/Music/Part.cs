namespace BinaryVibrance.Lunelle.Music;

/// <summary>
/// Something a track plays in a section: a <see cref="Line"/> of notes, a <see cref="Beat"/> of drums, or a
/// <see cref="Strum"/> of chords. Each is written as a string and checked when it is placed (<see cref="Section.Play"/>).
/// </summary>
public abstract class Part
{
	private protected Part() { }

	// The part read against a meter: its length and its notes before the scale, chords and kit resolve them
	internal abstract ParsedPart Parse(Meter meter);

	// Whether the track can play it: a beat needs a kit, the rest a patch
	internal abstract void Check(SongTrack track);

	/// <summary>This part played harder or softer: every velocity times <paramref name="scale"/> (0 to 2), held to 1 at most.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is below 0 or above 2.</exception>
	public Part Velocity(float scale)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(scale);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, 2f);
		return new PartVelocity(this, scale);
	}

	/// <summary>
	/// <paramref name="items"/> played one after another, each <c>Times</c> over, as one part: three bars of a groove
	/// then a fill. Every item must suit the same track: all beats, or all lines and strums.
	/// </summary>
	/// <exception cref="ArgumentException">There are none, or an item plays fewer than once.</exception>
	public static Part Sequence(params (Part Part, int Times)[] items)
	{
		ArgumentNullException.ThrowIfNull(items);
		if (items.Length == 0)
			throw new ArgumentException("A sequence plays one part or more.", nameof(items));
		foreach (var (part, times) in items)
		{
			ArgumentNullException.ThrowIfNull(part, nameof(items));
			if (times < 1)
				throw new ArgumentException($"A sequence's part plays once or more, not {times} times.", nameof(items));
		}
		return new PartSequence([.. items]);
	}
}
