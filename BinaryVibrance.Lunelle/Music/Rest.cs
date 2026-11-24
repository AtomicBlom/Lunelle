namespace BinaryVibrance.Lunelle.Music;

/// <summary>
/// A part that plays nothing, a bar at a time, on any track: what a song loaded from its file alone plays where the file
/// names code that isn't there (<see cref="MusicFolder"/>), so the rest of the song still plays.
/// </summary>
public sealed class Rest : Part
{
	private Rest() { }

	/// <summary>The one rest.</summary>
	public static Rest Bar { get; } = new();

	internal override void Check(SongTrack track)
	{
	}

	internal override ParsedPart Parse(Meter meter) => new Parsed(meter.TicksPerBar);

	private sealed class Parsed(int length) : ParsedPart(length)
	{
		public override void Emit(PartContext context, int offset, int limit, List<SongEvent> into)
		{
		}
	}
}
