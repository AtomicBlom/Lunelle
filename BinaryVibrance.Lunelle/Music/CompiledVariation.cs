namespace BinaryVibrance.Lunelle.Music;

// A song's variation as the sequencer picks it (Song.Variation): its options, how it picks, the seed from its name, and for
// picking at random its weights summed as it goes, from 0 to 1 at the last
internal sealed class CompiledVariation
{
	public string Name { get; private init; } = "";
	public int Options { get; private init; }
	public VariationPick Pick { get; private init; }
	public uint Seed { get; private init; }
	public float[] Cumulative { get; private init; } = [];

	public static CompiledVariation Of(Variation variation, uint seed)
	{
		var cumulative = new float[variation.Options];
		var total = variation.Weights.Count > 0 ? variation.Weights.Sum() : variation.Options;
		var sum = 0f;
		for (var o = 0; o < cumulative.Length; o++)
		{
			sum += variation.Weights.Count > 0 ? variation.Weights[o] : 1f;
			cumulative[o] = sum / total;
		}
		cumulative[^1] = 1f;
		return new CompiledVariation
		{
			Name = variation.Name,
			Options = variation.Options,
			Pick = variation.Pick,
			Seed = seed,
			Cumulative = cumulative,
		};
	}
}
