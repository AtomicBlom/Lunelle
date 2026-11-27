using System.Text;

using BinaryVibrance.Lunelle.Chip;
using BinaryVibrance.Lunelle.Format;
using BinaryVibrance.Lunelle.Music;

namespace BinaryVibrance.Lunelle.Tests;

/// <summary>
/// A music project of files alone in format 2 (docs/spec/project.md, cues.md): entries named by their paths, a manifest,
/// cue files and songs that import them, kits of patches, libraries by path and Lunelle's own, songs whose code plays as
/// rests, and a broken file said rather than closing the rest.
/// </summary>
public class MusicFolderTests
{
	internal const string Manifest = """
		{
		  "format": "lunelle/2",
		  "name": "tunes",
		  "title": "Tunes",
		  "macros": { "bright": { "number": 0, "about": "Opens the filter" } }
		}
		""";

	internal const string Cues = """
		{
		  "format": "lunelle-cues/1",
		  "cues": [
		    { "name": "intensity", "initial": 0.3, "about": "How busy it is" },
		    { "name": "mood", "options": ["Calm", { "name": "Dark", "about": "After dusk" }], "initial": "Calm" }
		  ]
		}
		""";

	internal const string Kit = """
		{
		  "format": "lunelle-kit/1",
		  "pieces": [
		    { "piece": "kick", "patch": "click" },
		    { "piece": "snare", "patch": "click", "gainDb": -6, "choke": 1 }
		  ]
		}
		""";

	// A bar of four kicks, while the mood is as asked; a lead that is code, and a hook
	internal static string Song(string when, string imports = "\"intensity\": 0.6, \"mood\": null") => $$"""
		{
		  "format": "lunelle-song/3",
		  "cues": { {{imports}} },
		  "tempo": 120,
		  "tracks": [
		    { "name": "drums", "kit": "clicks" },
		    { "name": "lead", "patch": "click", "macros": [{ "macro": "bright", "follow": "intensity", "from": 0, "to": 1 }] }
		  ],
		  "sections": [
		    {
		      "name": "verse", "bars": 1,
		      "parts": [
		        { "track": "drums", "beat": "eighths", "rows": { "kick": "x.x.x.x." }, "when": "{{when}}" },
		        { "track": "lead", "part": { "code": "Riff" }, "endWith": { "part": { "code": "Fill" } } },
		        { "track": "lead", "code": "Counter" }
		      ],
		      "exits": [{ "then": "verse" }]
		    }
		  ],
		  "hooks": ["OnBuilt"]
		}
		""";

	internal static MusicFolder Folder(Dictionary<string, string> files, Func<string, IProjectSource?>? host = null) =>
		MusicFolder.Load(new MemorySource(files.Keys, path => files.TryGetValue(path, out var text) ? Encoding.UTF8.GetBytes(text) : throw new FileNotFoundException(path)), host);

	internal static Dictionary<string, string> Files(string when = "mood is Calm") => new()
	{
		[MusicFolder.ProjectFileName] = Manifest,
		["Cues/tunes.cues.json"] = Cues,
		["Patches/click.patch.json"] = PatchFile.Write(PatchSpec.From(MusicRig.Click)),
		["Kits/clicks.kit.json"] = Kit,
		["Songs/tune.song.json"] = Song(when),
		["README.md"] = "not music",
	};

	[Test]
	public void A_folder_s_files_are_its_songs_patches_kits_cues_and_macros()
	{
		var folder = Folder(Files());

		folder.Problems.ShouldBeEmpty();
		folder.Name.ShouldBe("tunes");
		folder.Title.ShouldBe("Tunes");
		folder.Songs.Select(s => s.Name).ShouldBe(["tune"]);
		folder.Patches.Select(p => p.Name).ShouldBe(["click"]);
		folder.Kits.Select(k => k.Name).ShouldBe(["clicks"]);
		folder.Cues.Select(c => c.Name).ShouldBe(["intensity", "mood"]);
		folder.Cues[1].OptionNames.ShouldBe(["Calm", "Dark"]);
		folder.Macros["bright"].ShouldBe(new Macro(0));
		folder.MacroName(0).ShouldBe("bright");
		folder.SongPath("tune").ShouldBe("Songs/tune.song.json");
		folder.PatchPath("click").ShouldBe("Patches/click.patch.json");
		folder.KitPath("clicks").ShouldBe("Kits/clicks.kit.json");
		folder.Project.Root.Cues["mood"].Options[1].About.ShouldBe("After dusk");

		var kit = folder.Kits[0].Build();
		kit.Name.ShouldBe("clicks");
		kit.Pieces.Select(p => p.Piece.Id).ShouldBe(["kick", "snare"]);
		kit.Pieces.ShouldAllBe(p => p.Patch != null && p.Layers.Length == 0);
		kit.Pieces[1].GainDb.ShouldBe(-6f);
		kit.Pieces[1].ChokeGroup.ShouldBe(1);
		folder.Kits[0].Build().ShouldBeSameAs(kit);
		folder.Songs[0].Build().Name.ShouldBe("tune");
	}

	[Test]
	public void An_entry_s_id_is_its_path_below_its_kind_s_folder()
	{
		EntryIds.Entry("Patches/electric-piano.patch.json").ShouldBe((EntryKind.Patch, "electric-piano"));
		EntryIds.Entry("Patches/bass/sub.patch.json").ShouldBe((EntryKind.Patch, "bass/sub"));
		EntryIds.Entry("Kits/breaks.kit.json").ShouldBe((EntryKind.Kit, "breaks"));
		EntryIds.Entry("Kits/drums/rock/kit.json").ShouldBe((EntryKind.Kit, "drums/rock"));
		EntryIds.Entry("Songs/world-1/overworld.song.json").ShouldBe((EntryKind.Song, "world-1/overworld"));
		EntryIds.Entry("Patterns/drums/four.pattern.json").ShouldBe((EntryKind.Pattern, "drums/four"));
		EntryIds.Entry("Kits/rock/kick_1a.ogg").ShouldBeNull();
		EntryIds.IdProblem("Bass").ShouldNotBeNull();
		EntryIds.IdProblem("bass/sub-2").ShouldBeNull();
		EntryIds.Reference("lunelle:keys/piano").ShouldBe(("lunelle", "keys/piano"));
		EntryIds.Reference("lead").ShouldBe((null, "lead"));

		var files = Files();
		files["Kits/clicks/kit.json"] = Kit;
		files["Patches/Bass.patch.json"] = files["Patches/click.patch.json"];
		files["Patches/click/soft.patch.json"] = files["Patches/click.patch.json"];
		var folder = Folder(files);
		folder.Problems.ShouldContain(p => p.StartsWith("Kits/clicks.kit.json: ") && p.Contains("are both the kit 'clicks'"));
		folder.Problems.ShouldContain(p => p.StartsWith("Kits/clicks/kit.json: ") && p.Contains("are both the kit 'clicks'"));
		folder.Problems.ShouldContain(p => p.StartsWith("Patches/Bass.patch.json: "));
		folder.Problems.ShouldContain(p => p.StartsWith("Patches/click.patch.json: ") && p.Contains("also a folder"));
		folder.Kits.ShouldBeEmpty();
	}

	[Test]
	public void What_a_song_names_as_code_is_said_and_plays_nothing_while_the_rest_plays()
	{
		var folder = Folder(Files());
		folder.CodeIn("tune").ShouldBe(["part Riff", "part Fill", "placement Counter", "hook OnBuilt"]);

		using var rig = new MusicRig();
		rig.Player.Play(folder.Songs[0].Build(), fadeSeconds: 0f);
		rig.Step(MusicRig.Bar);
		MusicRig.Onsets(rig.Left()).Count.ShouldBe(4);
	}

	[Test]
	public void A_choice_cue_from_a_cue_file_chooses_as_an_enum_s_would()
	{
		var folder = Folder(Files(when: "mood is Dark"));
		var mood = folder.Cues.Single(c => c.Name == "mood");

		using var rig = new MusicRig();
		rig.Player.Play(folder.Songs[0].Build(), fadeSeconds: 0f);
		rig.Step(MusicRig.Bar);
		MusicRig.Onsets(rig.Left()).ShouldBeEmpty();

		rig.Player.Set(mood, mood.OptionValue(1));
		rig.Step(MusicRig.Bar);
		MusicRig.Onsets(rig.Left()).Count.ShouldBe(4);
	}

	[Test]
	public void A_song_imports_the_cues_it_reads_each_with_a_preview_value()
	{
		var folder = Folder(Files());
		var cues = folder.CuesOf("tune");
		cues.Select(c => c.Import.Reference).ShouldBe(["intensity", "mood"]);
		cues[0].Import.PreviewValue.ShouldBe(0.6, 1e-9);
		cues[1].Import.PreviewValue.ShouldBe(0);

		// Reading a cue it doesn't import is an error at the song; importing one it doesn't read is not
		var files = Files();
		files["Songs/tune.song.json"] = Song("mood is Calm", "\"intensity\": null");
		Should.Throw<FormatException>(() => Folder(files).Songs[0].Build()).Message.ShouldContain("doesn't import");

		files["Songs/tune.song.json"] = Song("mood is Calm", "\"intensity\": \"Calm\", \"mood\": 0.5, \"weather\": null");
		var wrong = Folder(files);
		wrong.Problems.ShouldContain(p => p.Contains("'intensity' is a level"));
		wrong.Problems.ShouldContain(p => p.Contains("'mood' is previewed at one of its options"));
		wrong.Problems.ShouldContain(p => p.Contains("no cue is 'weather'"));
	}

	[Test]
	public void Cue_ids_come_from_their_namespace_and_name_and_are_one_per_project()
	{
		var files = Files();
		files["Cues/lava.cues.json"] = """
			{ "format": "lunelle-cues/1", "namespace": "lava", "cues": [{ "name": "heat" }, { "name": "phase", "options": ["Countdown", "Race"] }] }
			""";
		files["Cues/lava-more.cues.json"] = """
			{ "format": "lunelle-cues/1", "namespace": "lava", "cues": [{ "name": "rise", "initial": 1 }] }
			""";
		var folder = Folder(files);
		folder.Problems.ShouldBeEmpty();
		folder.Cues.Select(c => c.Name).ShouldBe(["intensity", "lava/heat", "lava/phase", "lava/rise", "mood"]);

		files["Cues/again.cues.json"] = """{ "format": "lunelle-cues/1", "cues": [{ "name": "mood", "options": ["A", "B"] }] }""";
		files["Cues/bad.cues.json"] = """{ "format": "lunelle-cues/1", "cues": [{ "name": "x", "options": ["only"] }, { "name": "y", "initial": 2 }, { "name": "z", "options": ["a b", "C"] }] }""";
		var twice = Folder(files);
		twice.Problems.ShouldContain(p => p.StartsWith("Cues/again.cues.json: the cue 'mood' is defined in"));
		twice.Problems.ShouldContain(p => p.StartsWith("Cues/tunes.cues.json: the cue 'mood' is defined in"));
		twice.Problems.ShouldContain(p => p.Contains("cue 'x' chooses among two or more"));
		twice.Problems.ShouldContain(p => p.Contains("cue 'y' is a level"));
		twice.Problems.ShouldContain(p => p.Contains("cue 'z': an option is named as C# names things"));
	}

	[Test]
	public void A_broken_file_is_said_and_the_rest_still_load()
	{
		var files = Files();
		files["Patches/broken.patch.json"] = "{ \"format\": \"lunelle-patch/2\" ";
		files["Patches/old.patch.json"] = "{ \"format\": \"lunelle-patch/1\" }";
		var folder = Folder(files);

		folder.Problems.Count.ShouldBe(2);
		folder.Problems.ShouldContain(p => p.StartsWith("Patches/broken.patch.json: "));
		folder.Problems.ShouldContain(p => p.StartsWith("Patches/old.patch.json: ") && p.Contains("lunelle-patch/2"));
		folder.Patches.Select(p => p.Name).ShouldBe(["click"]);
		folder.Songs[0].Build().ShouldNotBeNull();
	}

	[Test]
	public void The_manifest_s_mistakes_are_said_one_by_one()
	{
		var files = Files();
		files[MusicFolder.ProjectFileName] = """
			{
			  "format": "lunelle/2",
			  "name": "Tunes",
			  "macros": { "bright": 0, "dark": 0, "wide": 9 },
			  "songs": ["tune", "missing"],
			  "colour": "blue"
			}
			""";
		var folder = Folder(files);

		folder.Macros.Keys.ShouldBe(["bright"]);
		folder.Problems.Count.ShouldBe(5);
		folder.Problems.ShouldContain(p => p.Contains("\"colour\" isn't something a manifest says"));
		folder.Problems.ShouldContain(p => p.Contains("its \"name\" isn't an id"));
		folder.Problems.ShouldContain(p => p.Contains("macro 'dark' is 0, which 'bright' is already"));
		folder.Problems.ShouldContain(p => p.Contains("macro 'wide' turns a number from 0 to 7"));
		folder.Problems.ShouldContain(p => p.Contains("\"songs\" lists 'missing'"));

		files[MusicFolder.ProjectFileName] = """{ "format": "lunelle-project/1", "name": "tunes" }""";
		Folder(files).Problems.ShouldContain(p => p.Contains("this reads \"lunelle/2\""));
		files.Remove(MusicFolder.ProjectFileName);
		Folder(files).Problems.ShouldContain(p => p.Contains("no lunelle.meta.json"));
	}

	[Test]
	public void A_kit_piece_is_a_patch_or_layers_never_both()
	{
		var files = Files();
		files["Kits/clicks.kit.json"] = """
			{ "format": "lunelle-kit/1", "pieces": [{ "piece": "kick", "patch": "click", "layers": [{ "upTo": 1, "takes": ["kick.wav"] }] }] }
			""";
		Should.Throw<InvalidDataException>(() => Folder(files).Kits[0].Build()).Message.ShouldContain("a piece is one or the other");

		files["Kits/clicks.kit.json"] = """{ "format": "lunelle-kit/1", "pieces": [{ "piece": "kick", "patch": "thud" }] }""";
		var folder = Folder(files);
		folder.Problems.ShouldContain(p => p.StartsWith("Kits/clicks.kit.json: no patch is 'thud'"));
	}

	[Test]
	public void Songs_are_in_the_manifest_s_order_then_by_id()
	{
		var files = Files();
		files["Songs/a.song.json"] = files["Songs/tune.song.json"];
		files["Songs/b.song.json"] = files["Songs/tune.song.json"];
		files[MusicFolder.ProjectFileName] = Manifest.Replace("\"title\": \"Tunes\",", "\"title\": \"Tunes\", \"songs\": [\"tune\"],", StringComparison.Ordinal);
		Folder(files).Songs.Select(s => s.Name).ShouldBe(["tune", "a", "b"]);
	}

	[Test]
	public void Lunelle_s_own_library_is_there_to_reference()
	{
		var files = Files();
		files[MusicFolder.ProjectFileName] = Manifest.Replace("\"title\": \"Tunes\",", "\"title\": \"Tunes\", \"libraries\": { \"lunelle\": { \"version\": \"1\" } },", StringComparison.Ordinal);
		files["Songs/tune.song.json"] = Song("mood is Calm").Replace("\"kit\": \"clicks\"", "\"kit\": \"lunelle:synth\"", StringComparison.Ordinal)
			.Replace("\"patch\": \"click\"", "\"patch\": \"lunelle:keys/electric-piano\"", StringComparison.Ordinal);
		var folder = Folder(files);
		folder.Problems.ShouldBeEmpty();
		var song = folder.Songs[0].Build();
		song.Tracks[1].Patch!.Name.ShouldBe("keys/electric-piano");
		song.Tracks[0].Kit!.Pieces.ShouldContain(p => p.Piece.Id == "kick");

		// A sampled kit's takes are read from the library's own files
		var rock = folder.Resolver.Resolve(folder.Project.Root.Libraries["lunelle"]!.Entry(EntryKind.Kit, "rock")!)!;
		rock.Json["pieces"]!.Items[0]["layers"]!.Items[0]["takes"]!.Items[0].Str.ShouldStartWith("lunelle//Kits/rock/");

		// A version the host doesn't provide is refused
		files[MusicFolder.ProjectFileName] = Manifest.Replace("\"title\": \"Tunes\",", "\"title\": \"Tunes\", \"libraries\": { \"lunelle\": { \"version\": \"2\" } },", StringComparison.Ordinal);
		Folder(files).Problems.ShouldContain(p => p.Contains("wanted at 2 or later"));
	}

	[Test]
	[Arguments("1", true)]
	[Arguments("1.2", true)]
	[Arguments("1.4", true)]
	[Arguments("1.4.1", false)]
	[Arguments("1.5", false)]
	[Arguments("2", false)]
	public void A_host_library_at_1_4_meets_what_it_should(string wanted, bool accepted)
	{
		SemVer.TryParse(wanted, out var want).ShouldBeTrue();
		SemVer.TryParse("1.4.0", out var provided).ShouldBeTrue();
		want.AcceptedBy(provided).ShouldBe(accepted);
	}

	[Test]
	public void A_library_by_path_resolves_its_references_in_its_own_libraries()
	{
		var files = Files();
		files[MusicFolder.ProjectFileName] = """
			{ "format": "lunelle/2", "name": "tunes", "macros": { "bright": 0 }, "libraries": { "game": { "path": "Libraries/game", "version": "2.0.0" }, "lunelle": { "version": "1" } } }
			""";
		files["Libraries/game/lunelle.meta.json"] = """{ "format": "lunelle/2", "name": "game", "version": "2.0.0", "libraries": { "lunelle": { "version": "1" } } }""";
		files["Libraries/game/Patches/lead.patch.json"] = """{ "format": "lunelle-patch/2", "base": "lunelle:leads/saw-lead", "set": { "gain": -3 } }""";
		files["Libraries/game/Cues/game.cues.json"] = """{ "format": "lunelle-cues/1", "cues": [{ "name": "danger" }] }""";
		files["Songs/tune.song.json"] = Song("mood is Calm", "\"intensity\": null, \"mood\": null, \"game:danger\": 0.5")
			.Replace("\"patch\": \"click\"", "\"patch\": \"game:lead\"", StringComparison.Ordinal);
		var folder = Folder(files);
		folder.Problems.ShouldBeEmpty();
		var resolved = folder.Resolved("tune")!;
		resolved.Patches.Keys.ShouldContain("game:lead");
		resolved.Patches["game:lead"].Layers.Select(l => l.Key).ShouldBe(["game:lunelle:leads/saw-lead", "game:lead"]);
		folder.Songs[0].Build().Tracks[1].Patch!.Name.ShouldBe("lead");
		folder.Cues.Select(c => c.Name).ShouldContain("game:danger");

		// A library not listed by the project itself can't be reached through another's
		files["Songs/tune.song.json"] = files["Songs/tune.song.json"].Replace("\"patch\": \"game:lead\"", "\"patch\": \"lunelle:leads/saw-lead\"", StringComparison.Ordinal);
		files[MusicFolder.ProjectFileName] = """{ "format": "lunelle/2", "name": "tunes", "macros": { "bright": 0 }, "libraries": { "game": { "path": "Libraries/game" } } }""";
		Folder(files).Problems.ShouldContain(p => p.Contains("names the library 'lunelle', which the project doesn't list"));

		// A missing path is an error on the manifest, and on what uses it; the rest loads
		files[MusicFolder.ProjectFileName] = """{ "format": "lunelle/2", "name": "tunes", "macros": { "bright": 0 }, "libraries": { "game": { "path": "Libraries/nowhere" } } }""";
		files["Songs/tune.song.json"] = Song("mood is Calm").Replace("\"patch\": \"click\"", "\"patch\": \"game:lead\"", StringComparison.Ordinal);
		var missing = Folder(files);
		missing.Problems.ShouldContain(p => p.StartsWith("lunelle.meta.json: the library 'game' isn't found"));
		missing.Problems.ShouldContain(p => p.StartsWith("Songs/tune.song.json: ") && p.Contains("couldn't be loaded"));
		missing.Patches.Select(p => p.Name).ShouldBe(["click"]);
	}

	[Test]
	public void A_cycle_of_libraries_is_an_error_naming_it()
	{
		var files = Files();
		files[MusicFolder.ProjectFileName] = """{ "format": "lunelle/2", "name": "tunes", "libraries": { "a": { "path": "Libraries/a" } } }""";
		files["Libraries/a/lunelle.meta.json"] = """{ "format": "lunelle/2", "name": "a", "libraries": { "b": { "path": "../b" } } }""";
		files["Libraries/b/lunelle.meta.json"] = """{ "format": "lunelle/2", "name": "b", "libraries": { "a": { "path": "../a" } } }""";
		Folder(files).Problems.ShouldContain(p => p.Contains("leads back to a project that leads to it"));
	}

	[Test]
	public void A_folder_on_disk_loads_as_one_given_by_paths()
	{
		var root = Path.Combine(Path.GetTempPath(), "lunelle-folder-" + Guid.NewGuid().ToString("N"));
		try
		{
			foreach (var (path, text) in Files())
			{
				Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path))!);
				File.WriteAllText(Path.Combine(root, path), text);
			}
			// A build's output is not the project's music
			Directory.CreateDirectory(Path.Combine(root, "bin"));
			File.WriteAllText(Path.Combine(root, "bin", "old.song.json"), Song("mood is Calm"));

			var folder = MusicFolder.FromDirectory(root);
			folder.Problems.ShouldBeEmpty();
			folder.Songs.Select(s => s.Name).ShouldBe(["tune"]);
			folder.SongPath("tune").ShouldBe("Songs/tune.song.json");
			folder.Songs[0].Build().ShouldNotBeNull();
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}
}
