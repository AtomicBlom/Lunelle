namespace BinaryVibrance.Lunelle.Music;

/// <summary>
/// A part played harder or softer: every note's or hit's velocity scaled, then held to 1 at most
/// (<see cref="Part.Velocity"/>). <c>0.7</c> is a softer take of the same groove (patterns.md §4).
/// </summary>
public sealed class PartVelocity : Part
{
	internal PartVelocity(Part part, float scale)
	{
		Inner = part;
		Scale = scale;
	}

	/// <summary>The part scaled.</summary>
	public Part Inner { get; }

	/// <summary>What each velocity is multiplied by.</summary>
	public float Scale { get; }

	internal override void Check(SongTrack track) => Inner.Check(track);

	internal override ParsedPart Parse(Meter meter) => new Parsed(Inner.Parse(meter), Scale);

	private sealed class Parsed(ParsedPart inner, float scale) : ParsedPart(inner.LengthTicks)
	{
		public override void Emit(PartContext context, int offset, int limit, List<SongEvent> into)
		{
			var from = into.Count;
			inner.Emit(context, offset, limit, into);
			var events = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(into);
			for (var i = from; i < events.Length; i++)
				events[i].Velocity = Math.Min(1f, events[i].Velocity * scale);
		}
	}
}
