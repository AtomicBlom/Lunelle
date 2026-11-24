namespace BinaryVibrance.Lunelle.Music;

/// <summary>How a <see cref="Variation"/> picks its next option.</summary>
public enum VariationPick
{
	/// <summary>At random, each option as likely as its weight (all alike unless weighted), drawn from the song's seed.</summary>
	Random,

	/// <summary>In turn: 1, 2, 3, then 1 again.</summary>
	Cycle,

	/// <summary>Every option once, in an order drawn from the song's seed, then a new order; never the same twice running,
	/// even from one order to the next.</summary>
	Shuffle,
}

/// <summary>
/// One of several options a song picks for itself, afresh each time a section that names it starts, so a part can differ
/// each time round: one drum fill this time, another the next (<see cref="Song.Variation"/>). Its options are numbered
/// from 1, and a part chooses one with <see cref="Is"/>: <c>fill.Is(2)</c>, <c>"fill is 2"</c> in a song file. It is the
/// song's own, so not among the cues a game tells it (<see cref="Song.Cues"/>), and it only chooses parts and exits: no
/// level or macro follows it.
/// </summary>
public sealed class Variation : CueBase
{
	internal Variation(string name, int options, VariationPick pick, IReadOnlyList<float> weights) : base(name)
	{
		Options = options;
		Pick = pick;
		Weights = weights;
	}

	/// <summary>How many options it has, numbered 1 to this.</summary>
	public int Options { get; }

	/// <summary>How it picks the next.</summary>
	public VariationPick Pick { get; }

	/// <summary>Each option's weight, in order, for <see cref="VariationPick.Random"/>; empty for all alike.</summary>
	public IReadOnlyList<float> Weights { get; }

	/// <summary>True while option <paramref name="option"/> (1 to <see cref="Options"/>) is the one picked.</summary>
	/// <exception cref="ArgumentOutOfRangeException">It has no such option.</exception>
	public Condition Is(int option)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(option, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(option, Options);
		return new IsCondition(this, option);
	}

	// Option 1 until the first pick, which is made before a section's parts are first asked
	internal override float InitialValue => 1f;

	/// <inheritdoc/>
	public override IReadOnlyList<string> OptionNames => [.. Enumerable.Range(1, Options).Select(o => o.ToString(System.Globalization.CultureInfo.InvariantCulture))];

	/// <inheritdoc/>
	public override float OptionValue(int index) => index + 1;
}
