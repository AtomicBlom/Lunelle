using BinaryVibrance.Lunelle.Music;

namespace BinaryVibrance.Lunelle.Tests;

/// <summary>A song's rolls: drawn afresh at each section start, compared exactly, the same picks from the same seed.</summary>
public class RollTests
{
	// A section of a bar or more played over and over, with three fills each taking a third of the roll
	private static (Song Song, Placement[] Fills, Cue Roll) Fills(int bars = 1)
	{
		var song = new Song("fills");
		var fill = song.Roll("fill");
		var tone = song.Track("tone", ChipRig.Tone);
		var a = song.Section("a", bars);
		Placement[] fills =
		[
			a.Play(tone, Line.Quarters("1 - - -")).When(fill.Below(0.33f)).Named("A"),
			a.Play(tone, Line.Quarters("3 - - -")).When(fill.Above(0.33f) & fill.Below(0.66f)).Named("B"),
			a.Play(tone, Line.Quarters("5 - - -")).When(fill.Above(0.66f)).Named("C"),
		];
		a.Then(a);
		return (song, fills, fill);
	}

	// The fill playing in each of the first bars, and the roll drawn for it
	private static List<(string Fill, float Roll)> Picks(uint seed, int bars = 24)
	{
		var (song, fills, roll) = Fills();
		using var rig = new MusicRig();
		rig.Player.Play(rig.Player.Prepare(song, seed: seed));
		var picks = new List<(string, float)>();
		for (var bar = 0; bar < bars; bar++)
		{
			rig.StepTo(bar * MusicRig.Bar + MusicRig.Bar / 2);
			var on = fills.Where(rig.Player.IsActive).Select(f => f.Name).ToArray();
			on.Length.ShouldBe(1, $"bar {bar + 1}: one fill, not {string.Join(", ", on)}");
			picks.Add((on[0], rig.Player.Get(roll)));
		}
		return picks;
	}

	[Test]
	public void Each_section_start_picks_exactly_one_of_the_slices_afresh()
	{
		var picks = Picks(seed: 7);

		picks.Select(p => p.Fill).Distinct().Count().ShouldBe(3);
		picks.Select(p => p.Roll).Distinct().Count().ShouldBe(picks.Count);
		foreach (var (fill, value) in picks)
			fill.ShouldBe(value < 0.33f ? "A" : value < 0.66f ? "B" : "C");
	}

	[Test]
	public void The_same_seed_picks_the_same_and_another_seed_picks_otherwise()
	{
		Picks(seed: 7).ShouldBe(Picks(seed: 7));
		Picks(seed: 8).Select(p => p.Roll).ShouldNotBe(Picks(seed: 7).Select(p => p.Roll));
	}

	[Test]
	public void A_roll_is_drawn_strictly_inside_nought_to_one()
	{
		foreach (var (_, value) in Picks(seed: 1, bars: 64))
			value.ShouldBeInRange(float.Epsilon, 1f - float.Epsilon);
	}

	[Test]
	public void A_swap_keeps_the_sections_picks()
	{
		// Two bars, so the swap is taken at a bar line within the section, not at its end
		var (song, _, roll) = Fills(bars: 2);
		using var rig = new MusicRig();
		rig.Player.Play(rig.Player.Prepare(song, seed: 3));
		rig.StepTo(MusicRig.Bar / 4);
		var drawn = rig.Player.Get(roll);
		var (rebuilt, _, rebuiltRoll) = Fills(bars: 2);

		rig.Player.Swap(rebuilt);
		rig.StepTo(MusicRig.Bar * 3 / 2);
		rig.Player.Position.Section.ShouldBe("a");

		rig.Player.Get(rebuiltRoll).ShouldBe(drawn);
	}

	[Test]
	public void Two_rolls_of_one_name_are_refused()
	{
		var song = new Song("twice");
		song.Roll("fill");

		Should.Throw<ArgumentException>(() => song.Roll("fill"));
	}
}
