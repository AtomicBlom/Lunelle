namespace BinaryVibrance.Lunelle.Music;

/// <summary>
/// Parts played one after another, each as many times as it says, as one part: three bars of a groove and then a fill
/// (<see cref="Part.Sequence"/>). Every item is the same kind of part, a beat, a line or a strum, so one track plays
/// them all. A pattern made of patterns is this (patterns.md §2.1).
/// </summary>
public sealed class PartSequence : Part
{
	private readonly (Part Part, int Times)[] _items;

	internal PartSequence((Part Part, int Times)[] items) => _items = items;

	/// <summary>Its items, in order, each with how many times it plays.</summary>
	public IReadOnlyList<(Part Part, int Times)> Items => _items;

	internal override void Check(SongTrack track)
	{
		foreach (var (part, _) in _items)
			part.Check(track);
	}

	internal override ParsedPart Parse(Meter meter)
	{
		var parsed = new (ParsedPart Part, int Times)[_items.Length];
		var length = 0;
		for (var i = 0; i < _items.Length; i++)
		{
			parsed[i] = (_items[i].Part.Parse(meter), _items[i].Times);
			length += parsed[i].Part.LengthTicks * _items[i].Times;
		}
		return new Parsed(length, parsed);
	}

	private sealed class Parsed(int lengthTicks, (ParsedPart Part, int Times)[] items) : ParsedPart(lengthTicks)
	{
		public override void Emit(PartContext context, int offset, int limit, List<SongEvent> into)
		{
			var at = offset;
			foreach (var (part, times) in items)
				for (var pass = 0; pass < times; pass++)
				{
					if (at >= limit)
						return;
					// Each pass ends where the next begins: nothing it plays rings on into the next
					part.Emit(context, at, Math.Min(limit, at + part.LengthTicks), into);
					at += part.LengthTicks;
				}
		}
	}
}
