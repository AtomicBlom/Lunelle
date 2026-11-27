using BinaryVibrance.Lunelle.Music;

namespace BinaryVibrance.Lunelle.Tests;

/// <summary>
/// Song exports (docs/spec/exports.md): a scripted run of a song rendered the same every time, its events at exact
/// frames, its sections found by playing, its end, fade and tail, its loop points in the file, and an album of them.
/// </summary>
public class SongExportTests
{
	// A click on every beat while intensity is up, in a verse that plays twice and then a chorus, which goes back
	private static string Song(string exports, bool fill = true) => $$"""
		{
		  "format": "lunelle-song/3",
		  "cues": { "intensity": 0, "mood": null },
		  "tempo": 120,
		  "variations": [{ "name": "fill", "options": 4 }],
		  "tracks": [{ "name": "lead", "patch": "click", "macros": [{ "macro": "bright", "follow": "intensity" }] }],
		  "sections": [
		    {
		      "name": "verse", "bars": 1, "variations": ["fill"],
		      "parts": [
		        { "track": "lead", "line": "quarters", "notes": "1 1 1 1", "when": "intensity > 0.5" },
		        { "track": "lead", "line": "eighths", "notes": ". 1 . . . . . .", "when": "{{(fill ? "fill is 2" : "pass > 5")}}" }
		      ],
		      "exits": [{ "when": "pass < 2", "then": "verse" }, { "then": "chorus" }]
		    },
		    {
		      "name": "chorus", "bars": 1,
		      "parts": [{ "track": "lead", "line": "quarters", "notes": "1 1 1 1" }],
		      "exits": [{ "then": "verse" }]
		    }
		  ],
		  "exports": [{{exports}}]
		}
		""";

	private static MusicFolder Folder(string exports, bool fill = true)
	{
		var files = MusicFolderTests.Files();
		files["Songs/tune.song.json"] = Song(exports, fill);
		files["Kits/clicks.kit.json"] = MusicFolderTests.Kit;
		return MusicFolderTests.Folder(files);
	}

	private static (float[] Left, ExportResult Result) Render(MusicFolder folder, string export = "main")
	{
		var samples = SongExport.Render(folder, "tune", export, null, out var result);
		var left = new float[samples.Length / 2];
		for (var i = 0; i < left.Length; i++)
			left[i] = samples[i * 2];
		return (left, result);
	}

	[Test]
	public void An_export_renders_the_same_samples_every_time_and_another_seed_picks_its_own()
	{
		const string Exports = """
			{ "name": "main", "cues": { "intensity": 1 }, "end": { "at": "33:1" } },
			{ "name": "other", "seed": 7, "cues": { "intensity": 1 }, "end": { "at": "33:1" } }
			""";
		var folder = Folder(Exports);
		folder.Problems.ShouldBeEmpty();
		var first = Render(folder).Left;
		Render(folder).Left.ShouldBe(first);
		Render(Folder(Exports)).Left.ShouldBe(first);
		Render(folder, "other").Left.ShouldNotBe(first);

		using var a = new MemoryStream();
		using var b = new MemoryStream();
		SongExport.Render(folder, "tune", "main", a);
		SongExport.Render(folder, "tune", "main", b);
		a.ToArray().ShouldBe(b.ToArray());
	}

	[Test]
	public void A_cue_set_at_a_bar_is_in_force_from_that_bar_s_first_frame()
	{
		var folder = Folder("""{ "name": "main", "events": [{ "at": "3:1", "cue": "intensity", "to": 1 }], "end": { "at": "5:1" } }""", fill: false);
		folder.Problems.ShouldBeEmpty();
		var onsets = MusicRig.Onsets(Render(folder).Left);
		// Bars 1 and 2 are verses with intensity at 0; bar 3 is the chorus (four), bar 4 a verse with intensity up (four)
		onsets.ShouldAllBe(o => o >= 2 * MusicRig.Bar - 2000 || o < 2 * MusicRig.Bar);
		onsets.Count(o => o >= 3 * MusicRig.Bar && o < 4 * MusicRig.Bar).ShouldBe(4);
	}

	[Test]
	public void A_section_s_second_start_is_found_by_playing_and_one_never_reached_is_an_error()
	{
		var folder = Folder("""
			{ "name": "main", "events": [{ "at": "section verse#2", "cue": "intensity", "to": 1 }], "end": { "at": "section chorus end" } }
			""");
		folder.Problems.ShouldBeEmpty();
		var (left, result) = Render(folder);
		var onsets = MusicRig.Onsets(left);
		onsets.Count(o => o < MusicRig.Bar).ShouldBe(0);
		onsets.Count(o => o >= MusicRig.Bar && o < 2 * MusicRig.Bar).ShouldBe(4);
		// It ends where the chorus ends, three bars in, and rings out for no more than ten seconds
		result.Frames.ShouldBeGreaterThanOrEqualTo(3 * MusicRig.Bar);
		result.Frames.ShouldBeLessThanOrEqualTo(3 * MusicRig.Bar + 10 * MusicRig.Rate);

		var never = Folder("""
			{ "name": "main", "events": [{ "at": "section verse#9", "cue": "intensity", "to": 1 }], "end": { "at": "section chorus end" } }
			""");
		Should.Throw<FormatException>(() => Render(never)).Message.ShouldContain("never reaches \"section verse#9\"");
	}

	[Test]
	public void A_fade_brings_the_end_to_silence()
	{
		var folder = Folder("""{ "name": "main", "cues": { "intensity": 1 }, "end": { "at": "2:1", "fade": "1s" } }""");
		var (left, result) = Render(folder);
		result.Frames.ShouldBe(MusicRig.Bar + MusicRig.Rate);
		left[^1].ShouldBe(0f);
	}

	[Test]
	public void Loop_points_are_written_as_frames_a_reader_finds()
	{
		var folder = Folder("""
			{ "name": "main", "cues": { "intensity": 1 }, "loop": { "from": "section verse", "to": "section chorus" }, "end": { "at": "4:1", "tail": "0s" } }
			""");
		using var wav = new MemoryStream();
		var result = SongExport.Render(folder, "tune", "main", wav);
		result.LoopStart.ShouldBe(0);
		result.LoopEnd.ShouldBe(2 * MusicRig.Bar);
		ExportWav.ReadLoop(wav.ToArray()).ShouldBe((0L, 2L * MusicRig.Bar));
		result.Frames.ShouldBe(3 * MusicRig.Bar);
	}

	[Test]
	public void An_export_s_mistakes_are_said_against_the_song()
	{
		var folder = Folder("""
			{ "name": "main", "events": [{ "at": "17", "cue": "intensity", "to": 1 }, { "at": "1:1", "cue": "weather", "to": 1 },
			  { "at": "section bridge", "jump": "verse" }, { "at": "1:1", "track": "lead", "macro": "bright", "to": 2 }],
			  "end": { "at": "section outro end" } },
			{ "name": "main", "end": { "at": "1:1" } }
			""");
		folder.Problems.ShouldContain(p => p.Contains("\"17\" isn't a position"));
		folder.Problems.ShouldContain(p => p.Contains("sets 'weather', which the song doesn't import"));
		folder.Problems.ShouldContain(p => p.Contains("happens at 'bridge', which isn't a section"));
		folder.Problems.ShouldContain(p => p.Contains("a macro is held \"to\" a value from 0 to 1"));
		folder.Problems.ShouldContain(p => p.Contains("ends at 'outro', which isn't a section"));
		folder.Problems.ShouldContain(p => p.Contains("two exports are named 'main'"));
	}

	[Test]
	public void A_held_macro_wins_over_what_it_follows_until_it_is_let_go()
	{
		var folder = Folder("""
			{ "name": "main", "cues": { "intensity": 1 },
			  "events": [{ "at": "1:1", "track": "lead", "macro": "bright", "to": 0.25 }, { "at": "2:1", "track": "lead", "macro": "bright", "to": null }],
			  "end": { "at": "3:1" } }
			""");
		folder.Problems.ShouldBeEmpty();
		Render(folder).Result.Frames.ShouldBeGreaterThanOrEqualTo(2 * MusicRig.Bar);
	}

	[Test]
	public void An_album_renders_each_track_numbered_in_order()
	{
		var files = MusicFolderTests.Files();
		files["Songs/tune.song.json"] = Song("""
			{ "name": "main", "cues": { "intensity": 1 }, "end": { "at": "2:1", "tail": "0s" } },
			{ "name": "quiet", "end": { "at": "2:1", "tail": "0s" } }
			""");
		files[MusicFolder.ProjectFileName] = """
			{ "format": "lunelle/2", "name": "tunes", "macros": { "bright": 0 }, "album": { "title": "Tunes OST", "tracks": ["tune/quiet", "tune/main"] } }
			""";
		var folder = MusicFolderTests.Folder(files);
		folder.Problems.ShouldBeEmpty();
		var directory = Path.Combine(Path.GetTempPath(), "lunelle-album-" + Guid.NewGuid().ToString("N"));
		try
		{
			var written = SongExport.RenderAlbum(folder, directory);
			written.Select(Path.GetFileName).ShouldBe(["01 tune--quiet.wav", "02 tune--main.wav"]);
			System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(written[1])).ShouldContain("Tunes OST");
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}
}
