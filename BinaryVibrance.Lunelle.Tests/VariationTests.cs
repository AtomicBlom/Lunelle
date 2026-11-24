using BinaryVibrance.Lunelle.Music;

namespace BinaryVibrance.Lunelle.Tests;

/// <summary>
/// A song's variations: one option picked as each section that names it starts, at random by weight, in turn, or
/// shuffled; the same picks from the same seed; one count shared by every section that names it, and moved by no other;
/// kept by a swap, moved by a jump; and refused where they can't be.
/// </summary>
public class VariationTests
{
	// Sections of a bar played over and over, "a" with one fill for each option of `fill`
	private static (Song Song, Placement[] Fills, Variation Fill) Fills(VariationPick pick, int options = 3, float[]? weights = null, int bars = 1)
	{
		var song = new Song("fills");
		var fill = song.Variation("fill", options, pick, weights);
		var tone = song.Track("tone", ChipRig.Tone);
		var a = song.Section("a", bars);
		var fills = Enumerable.Range(1, options)
			.Select(o => a.Play(tone, Line.Quarters($"{o} - - -")).When(fill.Is(o)).Named($"fill {o}"))
			.ToArray();
		a.Then(a);
		return (song, fills, fill);
	}

	// The option playing in each of the first bars, checking one fill plays and that it is the option the player reads
	private static List<int> Picks(VariationPick pick, uint seed = 7, int bars = 24, int options = 3, float[]? weights = null)
	{
		var (song, fills, fill) = Fills(pick, options, weights);
		using var rig = new MusicRig();
		rig.Player.Play(rig.Player.Prepare(song, seed: seed));
		var picks = new List<int>();
		for (var bar = 0; bar < bars; bar++)
		{
			rig.StepTo(bar * MusicRig.Bar + MusicRig.Bar / 2);
			var on = fills.Where(rig.Player.IsActive).Select(f => f.Name).ToArray();
			on.Length.ShouldBe(1, $"bar {bar + 1}: one fill, not {string.Join(", ", on)}");
			var option = rig.Player.Get(fill);
			on[0].ShouldBe($"fill {option}");
			picks.Add(option);
		}
		return picks;
	}

	[Test]
	public void Random_picks_afresh_each_time_round_and_the_same_seed_picks_the_same()
	{
		var picks = Picks(VariationPick.Random);

		picks.Distinct().Count().ShouldBe(3);
		Picks(VariationPick.Random).ShouldBe(picks);
		Picks(VariationPick.Random, seed: 8).ShouldNotBe(picks);
	}

	[Test]
	public void Random_picks_each_option_about_as_often_as_its_weight()
	{
		var picks = Picks(VariationPick.Random, seed: 11, bars: 400, weights: [6f, 1f, 1f]);

		var first = picks.Count(p => p == 1) / (float)picks.Count;
		first.ShouldBeInRange(0.65f, 0.85f);   // 6 of 8, 0.75
		picks.ShouldContain(2);
		picks.ShouldContain(3);
		// A weight of 0 is never picked
		Picks(VariationPick.Random, seed: 11, bars: 60, weights: [1f, 0f, 1f]).ShouldNotContain(2);
	}

	[Test]
	public void Cycle_picks_in_turn_whatever_the_seed()
	{
		Picks(VariationPick.Cycle, bars: 7).ShouldBe([1, 2, 3, 1, 2, 3, 1]);
		Picks(VariationPick.Cycle, seed: 99, bars: 7).ShouldBe([1, 2, 3, 1, 2, 3, 1]);
	}

	[Test]
	public void Shuffle_plays_every_option_once_a_round_and_never_one_twice_running()
	{
		foreach (var seed in new uint[] { 1, 2, 3, 7, 42 })
		{
			var picks = Picks(VariationPick.Shuffle, seed, bars: 30);
			for (var round = 0; round < picks.Count / 3; round++)
				picks.Skip(round * 3).Take(3).Order().ToArray().ShouldBe([1, 2, 3], $"seed {seed}, round {round + 1}");
			for (var i = 1; i < picks.Count; i++)
				picks[i].ShouldNotBe(picks[i - 1], $"seed {seed}, bars {i} and {i + 1}");
		}
		// Rounds in more than one order
		Picks(VariationPick.Shuffle, seed: 5, bars: 30).Chunk(3).Select(r => string.Join("", r)).Distinct().Count().ShouldBeGreaterThan(1);
		// Two options alternate, even across rounds
		Picks(VariationPick.Shuffle, seed: 5, bars: 10, options: 2).Zip(Picks(VariationPick.Shuffle, seed: 5, bars: 10, options: 2).Skip(1))
			.ShouldAllBe(pair => pair.First != pair.Second);
	}

	// Sections "a" and "b" taking turns, each a bar, with fills on `fill` in "a" and, if `shared`, in "b" too
	private static (Song Song, Variation Fill, Section A, Section B) TakingTurns(bool shared)
	{
		var song = new Song("turns");
		var fill = song.Variation("fill", 3, VariationPick.Cycle);
		var tone = song.Track("tone", ChipRig.Tone);
		var a = song.Section("a", 1);
		var b = song.Section("b", 1);
		for (var o = 1; o <= 3; o++)
		{
			a.Play(tone, Line.Quarters($"{o} - - -")).When(fill.Is(o));
			if (shared)
				b.Play(tone, Line.Quarters($"{o + 2} - - -")).When(fill.Is(o));
		}
		if (!shared)
			b.Play(tone, Line.Quarters("8 - - -"));
		a.Then(b);
		b.Then(a);
		return (song, fill, a, b);
	}

	private static List<(string Section, int Option)> Turns(bool shared, int bars = 6)
	{
		var (song, fill, _, _) = TakingTurns(shared);
		using var rig = new MusicRig();
		rig.Player.Play(rig.Player.Prepare(song, seed: 1));
		var seen = new List<(string, int)>();
		for (var bar = 0; bar < bars; bar++)
		{
			rig.StepTo(bar * MusicRig.Bar + MusicRig.Bar / 2);
			seen.Add((rig.Player.Position.Section, rig.Player.Get(fill)));
		}
		return seen;
	}

	[Test]
	public void Every_section_that_names_a_variation_moves_one_count_on()
	{
		Turns(shared: true).ShouldBe([("a", 1), ("b", 2), ("a", 3), ("b", 1), ("a", 2), ("b", 3)]);
	}

	[Test]
	public void A_section_that_doesnt_name_it_moves_it_on_not_at_all()
	{
		Turns(shared: false).Where(t => t.Section == "a").Select(t => t.Option).ShouldBe([1, 2, 3]);
	}

	[Test]
	public void A_swap_keeps_the_pick_and_the_count_and_a_jump_moves_it_on()
	{
		// Two bars, so the swap is taken at a bar line within the section, not at its end
		var (song, _, fill) = Fills(VariationPick.Cycle, bars: 2);
		using var rig = new MusicRig();
		rig.Player.Play(rig.Player.Prepare(song, seed: 3));
		rig.StepTo(MusicRig.Bar / 4);
		rig.Player.Get(fill).ShouldBe(1);
		var (rebuilt, _, rebuiltFill) = Fills(VariationPick.Cycle, bars: 2);

		rig.Player.Swap(rebuilt);
		rig.StepTo(MusicRig.Bar * 3 / 2);
		rig.Player.Position.Section.ShouldBe("a");
		rig.Player.Get(rebuiltFill).ShouldBe(1);
		// The section's next start goes on from the count the old song had made
		rig.StepTo(MusicRig.Bar * 5 / 2);
		rig.Player.Get(rebuiltFill).ShouldBe(2);

		rig.Player.Jump(rebuilt.Sections[0]);
		rig.StepTo(MusicRig.Bar * 3 + MusicRig.Bar / 4);
		rig.Player.Get(rebuiltFill).ShouldBe(3);
	}

	[Test]
	public void A_variation_is_the_songs_not_a_cue_the_game_tells()
	{
		var (song, _, fill) = Fills(VariationPick.Shuffle);

		song.Variations.ShouldHaveSingleItem().ShouldBeSameAs(fill);
		song.Cues.ShouldNotContain(fill);
		fill.OptionNames.ShouldBe(["1", "2", "3"]);
		ConditionText.Write(fill.Is(2)).ShouldBe("fill is 2");
		ConditionText.Parse("fill is 3", n => n == "fill" ? fill : null).ShouldBe(fill.Is(3));
		Should.Throw<FormatException>(() => ConditionText.Parse("fill > 0.5", n => n == "fill" ? fill : null));
		using var rig = new MusicRig();
		Should.Throw<ArgumentException>(() => rig.Player.Set(fill, 2f));
	}

	[Test]
	public void What_cant_be_a_variation_is_refused()
	{
		var song = new Song("refused");
		var fill = song.Variation("fill", 3);

		Should.Throw<ArgumentException>(() => song.Variation("fill", 2));
		Should.Throw<ArgumentException>(() => song.Variation("one", 1));
		Should.Throw<ArgumentException>(() => song.Variation("short", 3, VariationPick.Random, [1f, 2f]));
		Should.Throw<ArgumentException>(() => song.Variation("cycled", 3, VariationPick.Cycle, [1f, 1f, 1f]));
		Should.Throw<ArgumentException>(() => song.Variation("none", 2, VariationPick.Random, [0f, 0f]));
		Should.Throw<ArgumentException>(() => song.Variation("below", 2, VariationPick.Random, [-1f, 2f]));
		Should.Throw<ArgumentOutOfRangeException>(() => fill.Is(0));
		Should.Throw<ArgumentOutOfRangeException>(() => fill.Is(4));
	}
}
