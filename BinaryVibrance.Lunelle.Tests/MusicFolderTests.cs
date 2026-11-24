using System.Text;

using BinaryVibrance.Lunelle.Chip;
using BinaryVibrance.Lunelle.Music;

namespace BinaryVibrance.Lunelle.Tests;

/// <summary>
/// A music project of files alone: its cues and macros from lunelle-project.json, kits of patches, songs whose code plays
/// as rests, and a broken file said rather than closing the rest.
/// </summary>
public class MusicFolderTests
{
	private const string Project = """
		{
		  "format": "lunelle-project/1",
		  "name": "Tunes",
		  "cues": [
		    { "name": "intensity", "initial": 0.3 },
		    { "name": "mood", "options": ["Calm", "Dark"], "initial": "Calm" }
		  ],
		  "macros": { "bright": 0 }
		}
		""";

	private const string Kit = """
		{
		  "name": "clicks",
		  "pieces": [
		    { "piece": "kick", "patch": "click" },
		    { "piece": "snare", "patch": "click", "gainDb": -6, "choke": 1 }
		  ]
		}
		""";

	// A bar of four kicks, while the mood is as asked; a lead that is code, and a hook
	private static string Song(string when) => $$"""
		{
		  "format": "lunelle-song/2",
		  "name": "tune",
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

	private static MusicFolder Folder(Dictionary<string, string> files) =>
		MusicFolder.Load(files.Keys, path => files.TryGetValue(path, out var text) ? Encoding.UTF8.GetBytes(text) : throw new FileNotFoundException(path));

	private static Dictionary<string, string> Files(string when = "mood is Calm") => new()
	{
		[MusicFolder.ProjectFileName] = Project,
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
		folder.Name.ShouldBe("Tunes");
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

		var kit = folder.Kits[0].Build();
		kit.Pieces.Select(p => p.Piece.Id).ShouldBe(["kick", "snare"]);
		kit.Pieces.ShouldAllBe(p => p.Patch != null && p.Layers.Length == 0);
		kit.Pieces[1].GainDb.ShouldBe(-6f);
		kit.Pieces[1].ChokeGroup.ShouldBe(1);
		folder.Kits[0].Build().ShouldBeSameAs(kit);
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
	public void A_choice_cue_from_the_project_file_chooses_as_an_enum_s_would()
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
	public void A_broken_file_is_said_and_the_rest_still_load()
	{
		var files = Files();
		files["Patches/broken.patch.json"] = "{ \"format\": \"lunelle-patch/1\" ";
		files["Patches/code.patch.json"] = "{ \"format\": \"lunelle-patch/1\", \"name\": \"code\", \"code\": \"Organ\" }";
		files["Songs/nameless.song.json"] = "{ \"format\": \"lunelle-song/2\" }";
		var folder = Folder(files);

		folder.Problems.Count.ShouldBe(3);
		folder.Problems.ShouldContain(p => p.StartsWith("Patches/broken.patch.json: "));
		folder.Problems.ShouldContain(p => p.StartsWith("Patches/code.patch.json: ") && p.Contains("is code"));
		folder.Problems.ShouldContain(p => p == "Songs/nameless.song.json: a song file has a \"name\".");
		folder.Patches.Select(p => p.Name).ShouldBe(["click"]);
		folder.Songs.Select(s => s.Name).ShouldBe(["tune"]);
		folder.Songs[0].Build().ShouldNotBeNull();
	}

	[Test]
	public void The_project_file_s_mistakes_are_said_one_by_one()
	{
		var files = Files();
		files[MusicFolder.ProjectFileName] = """
			{
			  "format": "lunelle-project/1",
			  "cues": [
			    { "name": "intensity" },
			    { "name": "intensity" },
			    { "name": "mood", "options": ["Calm", "Dark"], "initial": "Stormy" },
			    { "options": ["A"] }
			  ],
			  "macros": { "bright": 0, "dark": 0, "wide": 9 }
			}
			""";
		var folder = Folder(files);

		folder.Cues.Select(c => c.Name).ShouldBe(["intensity"]);
		folder.Macros.Keys.ShouldBe(["bright"]);
		folder.Problems.ShouldBe(
		[
			"lunelle-project.json: two cues are named 'intensity'.",
			"lunelle-project.json: Cue 'mood' starts at 'Stormy', which isn't one of its options (Calm, Dark). (Parameter 'initial')",
			"lunelle-project.json: a cue has a \"name\".",
			"lunelle-project.json: macro 'dark' is 0, which 'bright' is already.",
			"lunelle-project.json: macro 'wide' is 9, and a macro is a number from 0 to 7.",
		]);
	}

	[Test]
	public void A_kit_piece_is_a_patch_or_layers_never_both()
	{
		var files = Files();
		files["Kits/clicks.kit.json"] = """
			{ "name": "clicks", "pieces": [{ "piece": "kick", "patch": "click", "layers": [{ "upTo": 1, "takes": ["kick.wav"] }] }] }
			""";
		Should.Throw<InvalidDataException>(() => Folder(files).Kits[0].Build()).Message.ShouldContain("a piece is one or the other");

		files["Kits/clicks.kit.json"] = """{ "name": "clicks", "pieces": [{ "piece": "kick", "patch": "thud" }] }""";
		Should.Throw<InvalidDataException>(() => Folder(files).Kits[0].Build()).Message.ShouldContain("no patch is named 'thud'");
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
			File.WriteAllText(Path.Combine(root, "bin", "old.song.json"), Song("mood is Calm").Replace("\"tune\"", "\"old\""));

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
