using BinaryVibrance.Lunelle.Chip;
using BinaryVibrance.Lunelle.Format;
using BinaryVibrance.Lunelle.Music;

namespace BinaryVibrance.Lunelle.Tests;

/// <summary>
/// Format 2's resolver: derived patches and kits in layers (docs/spec/overrides.md), a song's own sounds, and patterns
/// written into the parts that play them (patterns.md), each checked against the spec's test cases.
/// </summary>
public class ResolverTests
{
	private const string SawLead = """
		{
		  "format": "lunelle-patch/2",
		  "about": "Two saws over a sine an octave down",
		  "operators": [
		    { "name": "sub", "kind": "sine", "ratio": 0.5, "level": 0.3 },
		    { "name": "saw1", "kind": "saw" },
		    { "name": "saw2", "kind": "saw", "detune": 7 }
		  ],
		  "out": [{ "op": "sub" }, { "op": "saw1" }, { "op": "saw2" }],
		  "filter": { "kind": "lowPass", "cutoff": 2400 }
		}
		""";

	private static Dictionary<string, string> Files()
	{
		var files = MusicFolderTests.Files();
		files["Patches/saw-lead.patch.json"] = SawLead;
		return files;
	}

	private static MusicFolder Folder(Dictionary<string, string> files) => MusicFolderTests.Folder(files);

	private static JObject Patch(MusicFolder folder, string id) =>
		folder.Resolver.Resolve(folder.Project.Root.Entry(EntryKind.Patch, id)!)!.Json;

	[Test]
	public void One_layer_changes_only_what_it_sets()
	{
		var files = Files();
		files["Patches/lead.patch.json"] = """{ "format": "lunelle-patch/2", "base": "saw-lead", "set": { "filter.cutoff": 1800 } }""";
		var folder = Folder(files);
		folder.Problems.ShouldBeEmpty();

		var lead = Patch(folder, "lead");
		var below = Patch(folder, "saw-lead");
		lead["filter"]!["cutoff"]!.Num.ShouldBe("1800");
		lead["operators"]!.Same(below["operators"]).ShouldBeTrue();
		lead["name"]!.Str.ShouldBe("lead");
		lead["about"]!.Str.ShouldBe("Two saws over a sine an octave down");
		PatchFile.Read(lead.ToString()).Filter!.CutoffHz.ShouldBe(1800f);
		folder.Patches.Single(p => p.Name == "lead").Build().ShouldNotBeNull();
	}

	[Test]
	public void Three_layers_each_win_over_the_ones_below_and_say_which_set_what()
	{
		var files = Files();
		files[MusicFolder.ProjectFileName] = """{ "format": "lunelle/2", "name": "tunes", "macros": { "bright": 0 }, "libraries": { "base": { "path": "Libraries/base" } } }""";
		files["Libraries/base/lunelle.meta.json"] = """{ "format": "lunelle/2", "name": "base" }""";
		files["Libraries/base/Patches/saw-lead.patch.json"] = SawLead;
		files["Patches/lead.patch.json"] = """
			{ "format": "lunelle-patch/2", "base": "base:saw-lead", "set": { "filter.cutoff": 1800, "operators[saw2].detune": 9 } }
			""";
		files["Songs/tune.song.json"] = MusicFolderTests.Song("mood is Calm").Replace("\"tempo\": 120,", """
			"tempo": 120,
			"patches": { "click": { "base": "lead", "set": { "operators[saw2].detune": 15 } } },
			""", StringComparison.Ordinal);
		var folder = Folder(files);
		folder.Problems.ShouldContain(p => p.Contains("has a \"base\", so it is a new patch"));

		files["Songs/tune.song.json"] = files["Songs/tune.song.json"].Replace("\"patches\": { \"click\"", "\"patches\": { \"finale\"", StringComparison.Ordinal)
			.Replace("\"patch\": \"click\"", "\"patch\": \"finale\"", StringComparison.Ordinal);
		folder = Folder(files);
		folder.Problems.ShouldBeEmpty();
		var finale = folder.Resolved("tune")!.Patches["~finale"];
		finale.Json["filter"]!["cutoff"]!.Num.ShouldBe("1800");
		finale.Json["operators"]!.Items[2]["detune"]!.Num.ShouldBe("15");
		finale.Provenance["filter.cutoff"].ShouldBe("lead");
		finale.Provenance["operators[saw2].detune"].ShouldBe("~finale");
		folder.Songs[0].Build().Tracks[1].Patch!.Name.ShouldBe("finale");

		// The song's own is the song's alone
		files["Songs/other.song.json"] = MusicFolderTests.Song("mood is Calm").Replace("\"patch\": \"click\"", "\"patch\": \"finale\"", StringComparison.Ordinal);
		Folder(files).Problems.ShouldContain(p => p.StartsWith("Songs/other.song.json: no patch is 'finale'"));
	}

	[Test]
	public void A_song_s_override_without_a_base_changes_that_sound_for_that_song_alone()
	{
		var files = Files();
		files["Songs/tune.song.json"] = MusicFolderTests.Song("mood is Calm")
			.Replace("\"patch\": \"click\"", "\"patch\": \"saw-lead\"", StringComparison.Ordinal)
			.Replace("\"tempo\": 120,", "\"tempo\": 120, \"patches\": { \"saw-lead\": { \"set\": { \"filter.cutoff\": 4200 } } },", StringComparison.Ordinal);
		files["Songs/other.song.json"] = MusicFolderTests.Song("mood is Calm").Replace("\"patch\": \"click\"", "\"patch\": \"saw-lead\"", StringComparison.Ordinal);
		var folder = Folder(files);
		folder.Problems.ShouldBeEmpty();
		folder.Resolved("tune")!.Patches.Keys.ShouldContain("~saw-lead");
		folder.Resolved("tune")!.Patches.Keys.ShouldNotContain("saw-lead");
		folder.Resolved("tune")!.Patches["~saw-lead"].Json["filter"]!["cutoff"]!.Num.ShouldBe("4200");
		folder.Resolved("other")!.Patches["saw-lead"].Json["filter"]!["cutoff"]!.Num.ShouldBe("2400");
	}

	[Test]
	public void A_change_below_shows_through_except_where_a_layer_set_it()
	{
		var files = Files();
		files["Patches/lead.patch.json"] = """{ "format": "lunelle-patch/2", "base": "saw-lead", "set": { "filter.cutoff": 2400 } }""";
		files["Patches/saw-lead.patch.json"] = SawLead.Replace("\"cutoff\": 2400", "\"cutoff\": 3000, \"q\": 2", StringComparison.Ordinal);
		var lead = Patch(Folder(files), "lead");
		lead["filter"]!["q"]!.Num.ShouldBe("2");
		lead["filter"]!["cutoff"]!.Num.ShouldBe("2400");
	}

	[Test]
	public void A_default_can_be_set_or_removed_whether_the_base_writes_it_or_not()
	{
		var files = Files();
		files["Patches/lead.patch.json"] = """{ "format": "lunelle-patch/2", "base": "saw-lead", "set": { "filter.q": 3, "sends.space": 0.3 } }""";
		var lead = Patch(Folder(files), "lead");
		lead["filter"]!["q"]!.Num.ShouldBe("3");
		lead["sends"]!["space"]!.Num.ShouldBe("0.3");

		files["Patches/quiet.patch.json"] = """{ "format": "lunelle-patch/2", "base": "lead", "remove": ["filter.q"] }""";
		Patch(Folder(files), "quiet")["filter"]!["q"].ShouldBeNull();

		files["Patches/typo.patch.json"] = """{ "format": "lunelle-patch/2", "base": "saw-lead", "set": { "filter.cutof": 1 } }""";
		var folder = Folder(files);
		folder.Problems.ShouldContain(p => p.StartsWith("Patches/typo.patch.json: \"filter.cutof\" names 'cutof'"));
	}

	[Test]
	public void A_path_through_a_renamed_item_is_dangling_kept_and_applied_again_when_it_is_back()
	{
		var files = Files();
		files["Patches/lead.patch.json"] = """{ "format": "lunelle-patch/2", "base": "saw-lead", "set": { "operators[saw2].detune": 9 }, "remove": ["operators[gone]"] }""";
		files["Patches/saw-lead.patch.json"] = SawLead.Replace("saw2", "saw-wide", StringComparison.Ordinal);
		var folder = Folder(files);
		folder.Diagnostics.Where(p => p.File == "Patches/lead.patch.json").ShouldAllBe(p => p.IsWarning);
		folder.Diagnostics.Count(p => p.File == "Patches/lead.patch.json").ShouldBe(2);
		Patch(folder, "lead")["operators"]!.Items[2]["detune"]!.Num.ShouldBe("7");
		folder.Patches.Single(p => p.Name == "lead").Build().ShouldNotBeNull();

		files["Patches/saw-lead.patch.json"] = SawLead;
		Patch(Folder(files), "lead")["operators"]!.Items[2]["detune"]!.Num.ShouldBe("9");
	}

	[Test]
	public void An_insert_before_its_target_resolves_and_one_after_it_names_the_layers()
	{
		var files = Files();
		const string Wobble = """{ "name": "wobble", "kind": "sine", "ratio": 0.5, "level": 0 }""";
		files["Patches/lead.patch.json"] = $$"""
			{ "format": "lunelle-patch/2", "base": "saw-lead",
			  "insert": [{ "into": "operators", "before": "sub", "item": {{Wobble}} }],
			  "set": { "operators[sub].modulatedBy": [{ "op": "wobble", "index": 0.4 }] } }
			""";
		var folder = Folder(files);
		folder.Problems.ShouldBeEmpty();
		Patch(folder, "lead")["operators"]!.Items.Select(o => o["name"]!.Str).ShouldBe(["wobble", "sub", "saw1", "saw2"]);
		folder.Patches.Single(p => p.Name == "lead").Build().ShouldNotBeNull();

		files["Patches/lead.patch.json"] = files["Patches/lead.patch.json"].Replace("\"before\": \"sub\", ", "", StringComparison.Ordinal);
		var after = Folder(files);
		Should.Throw<FormatException>(() => after.Patches.Single(p => p.Name == "lead").Build()).Message.ShouldContain("saw-lead → lead");
	}

	[Test]
	public void A_cycle_of_bases_or_a_chain_deeper_than_sixteen_is_an_error()
	{
		var files = Files();
		files["Patches/a.patch.json"] = """{ "format": "lunelle-patch/2", "base": "b" }""";
		files["Patches/b.patch.json"] = """{ "format": "lunelle-patch/2", "base": "a" }""";
		Folder(files).Problems.ShouldContain(p => p.Contains("leads round in a circle") && p.Contains("a → b → a"));

		files.Remove("Patches/a.patch.json");
		files.Remove("Patches/b.patch.json");
		for (var i = 1; i <= 17; i++)
			files[$"Patches/p{i}.patch.json"] = $$"""{ "format": "lunelle-patch/2", "base": "{{(i == 1 ? "saw-lead" : $"p{i - 1}")}}" }""";
		Folder(files).Problems.ShouldContain(p => p.Contains("more than 16 deep"));
	}

	[Test]
	public void A_derived_file_says_only_what_it_changes()
	{
		var files = Files();
		files["Patches/lead.patch.json"] = """{ "format": "lunelle-patch/2", "base": "saw-lead", "operators": [] }""";
		Folder(files).Problems.ShouldContain(p => p.Contains("\"operators\" belongs in a whole patch"));
	}

	[Test]
	public void A_derived_kit_sets_a_piece_and_inserts_one_with_samples_beside_itself()
	{
		var files = Files();
		files[MusicFolder.ProjectFileName] = """{ "format": "lunelle/2", "name": "tunes", "macros": { "bright": 0 }, "libraries": { "lunelle": { "version": "1" } } }""";
		files["Kits/mine/kit.json"] = """
			{ "format": "lunelle-kit/1", "base": "lunelle:rock", "set": { "pieces[kick].gainDb": -3 },
			  "insert": [{ "into": "pieces", "item": { "piece": "clap", "layers": [{ "upTo": 1, "takes": ["clap.ogg"] }] } }] }
			""";
		var folder = Folder(files);
		folder.Problems.ShouldBeEmpty();
		var kit = folder.Resolver.Resolve(folder.Project.Root.Entry(EntryKind.Kit, "mine")!)!;
		kit.Json["pieces"]!.Items.First(p => p["piece"]!.Str == "kick")["gainDb"]!.Num.ShouldBe("-3");
		kit.Json["pieces"]!.Items.Last()["layers"]!.Items[0]["takes"]!.Items[0].Str.ShouldBe("//Kits/mine/clap.ogg");
		kit.Json["pieces"]!.Items[0]["layers"]!.Items[0]["takes"]!.Items[0].Str.ShouldStartWith("lunelle//Kits/rock/");
	}

	[Test]
	public void An_edited_derived_patch_is_saved_as_what_it_changes_and_its_sets_stay_pinned()
	{
		var below = (JObject)JReader.Parse(SawLead);
		var wanted = (JObject)below.Clone();
		((JObject)wanted["filter"]!).Set("cutoff", new JNumber(1200));
		((JArray)wanted["operators"]!).Values.RemoveAt(0);
		((JArray)wanted["out"]!).Values.RemoveAt(0);
		((JArray)wanted["operators"]!).Values.Insert(1, JReader.Parse("""{ "name": "saw3", "kind": "saw", "detune": -7 }"""));
		((JArray)wanted["out"]!).Values.Add(JReader.Parse("""{ "op": "saw3" }"""));
		var file = (JObject)JReader.Parse("""{ "format": "lunelle-patch/2", "base": "saw-lead", "set": { "sends.space": 0.2 } }""");
		((JObject)wanted).Set("sends", JReader.Parse("""{ "space": 0.2 }"""));

		var layer = OverrideDiff.Layer(below, wanted, OverrideSchema.Patch, file);
		layer["base"]!.Str.ShouldBe("saw-lead");
		layer["set"]!["filter.cutoff"]!.Num.ShouldBe("1200");
		layer["remove"]!.Items.Select(r => r.Str).ShouldBe(["operators[sub]", "out[sub]"]);
		layer["insert"]!.Items.Select(i => i["into"]!.Str).ShouldBe(["operators", "out"]);

		var applied = (JObject)below.Clone();
		var problems = new List<FormatProblem>();
		Overrides.Apply(applied, layer, OverrideSchema.Patch, "lead", "lead", problems, []);
		problems.ShouldBeEmpty();
		applied.Same(wanted).ShouldBeTrue(applied.ToString());

		// A set the file had stays, even once it equals the base
		((JObject)below).Set("sends", JReader.Parse("""{ "space": 0.2 }"""));
		OverrideDiff.Layer(below, wanted, OverrideSchema.Patch, file)["set"]!["sends.space"]!.Num.ShouldBe("0.2");
	}

	private const string Groove = """
		{ "format": "lunelle-pattern/1", "kind": "beat", "about": "Kick on the beats", "step": "eighths", "rows": { "kick": "x.x.x.x.", "snare": "..x...x." } }
		""";

	private static string BeatSong(string part) => $$"""
		{
		  "format": "lunelle-song/3",
		  "tempo": 120,
		  "tracks": [{ "name": "drums", "kit": "clicks" }, { "name": "lead", "patch": "click" }],
		  "sections": [{ "name": "verse", "bars": 2, "parts": [{{part}}], "exits": [{ "then": "verse" }] }]
		}
		""";

	private static float[] Render(MusicFolder folder, string song = "tune", int bars = 2)
	{
		using var rig = new MusicRig();
		rig.Player.Play(folder.Songs.Single(s => s.Name == song).Build(), fadeSeconds: 0f);
		rig.Step(MusicRig.Bar * bars);
		return rig.Left();
	}

	[Test]
	public void A_part_with_a_pattern_plays_as_the_same_part_written_in()
	{
		var files = Files();
		files["Patterns/drums/groove.pattern.json"] = Groove;
		files["Songs/tune.song.json"] = BeatSong("""{ "track": "drums", "pattern": "drums/groove" }""");
		files["Songs/inline.song.json"] = BeatSong("""{ "track": "drums", "beat": "eighths", "rows": { "kick": "x.x.x.x.", "snare": "..x...x." } }""");
		var folder = Folder(files);
		folder.Problems.ShouldBeEmpty();
		Render(folder).ShouldBe(Render(folder, "inline"));

		// Its transforms are the part's options, and a piece mapped away is silent
		files["Songs/tune.song.json"] = BeatSong("""{ "track": "drums", "pattern": "drums/groove", "velocity": 0.5, "pieces": { "snare": null } }""");
		files["Songs/inline.song.json"] = BeatSong("""{ "track": "drums", "beat": "eighths", "rows": { "kick": "x.x.x.x." }, "velocity": 0.5 }""");
		folder = Folder(files);
		folder.Problems.ShouldBeEmpty();
		Render(folder).ShouldBe(Render(folder, "inline"));
	}

	[Test]
	public void A_sequence_plays_its_patterns_in_turn_and_a_cycle_or_a_mixed_kind_is_an_error()
	{
		var files = Files();
		files["Patterns/groove.pattern.json"] = Groove;
		files["Patterns/fill.pattern.json"] = """{ "format": "lunelle-pattern/1", "kind": "beat", "step": "eighths", "rows": { "snare": "xxxxxxxx" } }""";
		files["Patterns/both.pattern.json"] = """{ "format": "lunelle-pattern/1", "kind": "beat", "sequence": [{ "pattern": "groove", "times": 3 }, { "pattern": "fill" }] }""";
		files["Songs/tune.song.json"] = BeatSong("""{ "track": "drums", "pattern": "both" }""").Replace("\"bars\": 2", "\"bars\": 4", StringComparison.Ordinal);
		files["Songs/inline.song.json"] = BeatSong("""
			{ "track": "drums", "beat": "eighths", "rows": { "kick": "x.x.x.x. x.x.x.x. x.x.x.x. ........", "snare": "..x...x. ..x...x. ..x...x. xxxxxxxx" } }
			""").Replace("\"bars\": 2", "\"bars\": 4", StringComparison.Ordinal);
		var folder = Folder(files);
		folder.Problems.ShouldBeEmpty();
		Render(folder, bars: 4).ShouldBe(Render(folder, "inline", bars: 4));

		files["Patterns/a.pattern.json"] = """{ "format": "lunelle-pattern/1", "kind": "beat", "sequence": [{ "pattern": "b" }] }""";
		files["Patterns/b.pattern.json"] = """{ "format": "lunelle-pattern/1", "kind": "beat", "sequence": [{ "pattern": "a" }] }""";
		files["Patterns/tune.pattern.json"] = """{ "format": "lunelle-pattern/1", "kind": "line", "step": "eighths", "notes": "1 2 3 4 5 6 7 8" }""";
		files["Patterns/mixed.pattern.json"] = """{ "format": "lunelle-pattern/1", "kind": "beat", "sequence": [{ "pattern": "groove" }, { "pattern": "tune" }] }""";
		var broken = Folder(files);
		broken.Problems.ShouldContain(p => p.Contains("lead round in a circle"));
		broken.Problems.ShouldContain(p => p.StartsWith("Patterns/mixed.pattern.json: the pattern 'tune' is a line"));
	}

	[Test]
	public void A_pattern_on_a_kit_without_a_piece_warns_and_a_mismatch_is_an_error()
	{
		var files = Files();
		files["Patterns/groove.pattern.json"] = Groove.Replace("\"snare\"", "\"ride\"", StringComparison.Ordinal);
		files["Songs/tune.song.json"] = BeatSong("""{ "track": "drums", "pattern": "groove" }""");
		var folder = Folder(files);
		folder.Diagnostics.ShouldHaveSingleItem().IsWarning.ShouldBeTrue();
		folder.Problems[0].ShouldContain("plays 'ride', which the kit 'clicks' hasn't");

		files["Songs/tune.song.json"] = BeatSong("""{ "track": "drums", "pattern": "groove", "pieces": { "ride": "snare" } }""");
		Folder(files).Problems.ShouldBeEmpty();

		files["Songs/tune.song.json"] = BeatSong("""{ "track": "lead", "pattern": "groove" }""");
		Folder(files).Problems.ShouldContain(p => p.Contains("a patch track can't play"));

		files["Patterns/groove.pattern.json"] = Groove.Replace("\"kind\": \"beat\",", "\"kind\": \"beat\", \"meter\": \"3/4\",", StringComparison.Ordinal);
		files["Songs/tune.song.json"] = BeatSong("""{ "track": "drums", "pattern": "groove" }""");
		Folder(files).Problems.ShouldContain(p => p.Contains("is in 3/4, and the song is in 4/4"));

		files["Songs/tune.song.json"] = BeatSong("""{ "track": "drums", "pattern": "nowhere" }""");
		var missing = Folder(files);
		missing.Problems.ShouldContain(p => p.Contains("no pattern is 'nowhere': the part plays nothing"));
		missing.Songs[0].Build().ShouldNotBeNull();
	}
}
