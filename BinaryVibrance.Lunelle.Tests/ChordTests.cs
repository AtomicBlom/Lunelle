using BinaryVibrance.Lunelle.Music;

namespace BinaryVibrance.Lunelle.Tests;

/// <summary>Chords by Roman numeral: the scale's own triads, sevenths, flats, and the kinds that override the scale.</summary>
public class ChordTests
{
	private static readonly Scale CMajor = Scale.Major(Note.C);

	[Test]
	public void The_scale_decides_each_triad()
	{
		Chord.Parse("I").Intervals(CMajor).ShouldBe([0, 4, 7]);
		Chord.Parse("ii").Intervals(CMajor).ShouldBe([0, 3, 7]);
		Chord.Parse("vii").Intervals(CMajor).ShouldBe([0, 3, 6]);
		Chord.Parse("V7").Intervals(CMajor).ShouldBe([0, 4, 7, 10]);
		Should.Throw<NotationException>(() => Chord.Parse("Imaj"));
	}

	[Test]
	public void A_flat_moves_the_root()
	{
		var chord = Chord.Parse("bVII");
		chord.RootClass(CMajor).ShouldBe(10);
		chord.Intervals(CMajor).ShouldBe([0, 4, 7]);
	}

	[Test]
	public void A_quality_an_extension_and_alterations_spell_any_chord()
	{
		var dMajor = Scale.Major(Note.D);
		Chord.Parse("V6").Intervals(dMajor).ShouldBe([0, 4, 7, 9]);
		Chord.Parse("ivm6").Intervals(dMajor).ShouldBe([0, 3, 7, 9]);
		Chord.Parse("ivm6").RootClass(dMajor).ShouldBe(7);
		Chord.Parse("V7sus4").Intervals(dMajor).ShouldBe([0, 5, 7, 10]);
		Chord.Parse("Iadd9").Intervals(dMajor).ShouldBe([0, 4, 7, 14]);
		Chord.Parse("bVIImaj7").Intervals(dMajor).ShouldBe([0, 4, 7, 11]);
		Chord.Parse("vim7").Intervals(dMajor).ShouldBe([0, 3, 7, 10]);

		var ebMajor = Scale.Major(Note.Ds);
		Chord.Parse("Imaj9").Intervals(ebMajor).ShouldBe([0, 4, 7, 11, 14]);
		Chord.Parse("vi9").Intervals(ebMajor).ShouldBe([0, 3, 7, 10, 14]);    // the scale's: Cm9
		Chord.Parse("V13").Intervals(ebMajor).ShouldBe([0, 4, 7, 10, 14, 21]);
		Chord.Parse("VIM7").Intervals(ebMajor).ShouldBe([0, 4, 7, 10]);       // C7, not the scale's Cm7
		Chord.Parse("VIM7(b9)").Intervals(ebMajor).ShouldBe([0, 4, 7, 10, 13]);
		Chord.Parse("I6/9").Intervals(ebMajor).ShouldBe([0, 4, 7, 9, 14]);
		Chord.Parse("I69").ShouldBe(Chord.Parse("I6/9"));
		Chord.Parse("viim7b5").Intervals(ebMajor).ShouldBe([0, 3, 6, 10]);
		Chord.Parse("viidim7").Intervals(ebMajor).ShouldBe([0, 3, 6, 9]);
		Chord.Parse("V7#9").Intervals(ebMajor).ShouldBe([0, 4, 7, 10, 15]);
		Chord.Parse("IVmaj7#11").Intervals(ebMajor).ShouldBe([0, 4, 7, 11, 18]);
		Chord.Parse("im11").Intervals(Scale.Minor(Note.A)).ShouldBe([0, 3, 7, 10, 14, 17]);

		Should.Throw<NotationException>(() => Chord.Parse("Iadd11"));
		Should.Throw<NotationException>(() => Chord.Parse("Imaj6"));
		Should.Throw<NotationException>(() => Chord.Parse("I9add9"));
		Should.Throw<NotationException>(() => Chord.Parse("imsus4"));
		Should.Throw<NotationException>(() => Chord.Parse("V57"));
	}

	[Test]
	public void Every_chord_reads_back_from_its_suffix()
	{
		foreach (var token in new[] { "I", "VM", "vim", "i5", "IVsus2", "Vsus4", "viidim", "V7", "Imaj7", "vim9", "V13",
			"VIM7b9", "I6/9", "Iadd9", "V7sus4", "ivm6", "IMmaj7","viim7b5", "V7#5#9", "IVmaj7#11", "V13b9" })
		{
			var chord = Chord.Parse(token);
			Chord.Parse("I" + chord.Suffix).ShouldBe(chord with { Degree = 1 }, token);
		}
	}

	[Test]
	public void In_E_minor()
	{
		var minor = Scale.Minor(Note.E);
		var power = Chord.Parse("i5");
		power.Intervals(minor).ShouldBe([0, 7]);
		power.RootClass(minor).ShouldBe(4);
		var six = Chord.Parse("VI");
		six.RootClass(minor).ShouldBe(0);
		six.Intervals(minor).ShouldBe([0, 4, 7]);
	}
}
