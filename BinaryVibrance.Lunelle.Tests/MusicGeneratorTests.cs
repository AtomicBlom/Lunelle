using System.Collections.Immutable;
using System.Reflection;

using BinaryVibrance.Lunelle.Music.Generator;
using BinaryVibrance.Lunelle.Chip;
using BinaryVibrance.Lunelle.Music;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace BinaryVibrance.Lunelle.Tests;

/// <summary>
/// Lunelle's music generator over a small project of its own in format 2: a manifest, cues, a patch and a song made into
/// C# that builds what the run-time loaders build from the same files, Lunelle's own sounds and a song's own made into
/// code too, and a file's mistake reported at its line.
/// </summary>
public class MusicGeneratorTests
{
	private const string Source = """
		using BinaryVibrance.Lunelle.Chip;
		using BinaryVibrance.Lunelle.Music;

		namespace Tunes
		{
			public enum Mood { Calm, Dark }

			// A kit of the game's own, in C#: the files name it, and the generator finds it here
			public static class Kits
			{
				public static DrumKit General => DrumKit.FromPatches("general", (Kit.Kick, Patches.Pluck, 0), (Kit.Snare, Patches.Pluck, 0));
			}
		}

		// The song's code, in the namespace of its file's folder (Songs)
		namespace Tunes.Songs
		{
			static partial class Tune
			{
				private static partial Part Riff() => Line.Eighths("1 3 5 3 1 3 5 8");

				static partial void OnBuilt(Song song, Section verse, Section chorus) => verse.Play(song.Tracks[1], Line.Quarters("8 . 5 ."));
			}
		}
		""";

	private const string Manifest = """
		{ "format": "lunelle/2", "name": "tunes", "macros": { "bright": { "number": 0, "about": "Opens the filter" } }, "libraries": { "lunelle": { "version": "1" } } }
		""";

	// The cues, the mood bound to the game's own enum
	private const string Cues = """
		{
		  "format": "lunelle-cues/1",
		  "cues": [
		    { "name": "intensity", "initial": 0.3, "about": "How much is going on" },
		    { "name": "mood", "options": ["Calm", "Dark"], "enum": "Tunes.Mood" }
		  ]
		}
		""";

	private static PatchSpec PluckSpec()
	{
		var spec = PatchSpec.From(Patch.Build("pluck", p =>
		{
			var pick = p.Sine(3f, Env.Decay(0.05f), 0.7f, velocity: 0.5f);
			p.Out(p.Sine(1f, Env.Adsr(0.002f, 0.4f, 0.2f, 0.1f)).ModulatedBy(pick, 0.4f).Detune(7f), p.Noise(Env.Decay(0.01f), 0.2f, pink: true));
			p.Filter(Filter.LowPass(1800f, q: 1.1f, envelopeOctaves: 2f, envelope: Env.Decay(0.3f)));
			p.Macro(new Macro(0), MacroTarget.FilterCutoff, 0.5f, 2f);
			p.Send(ChipSend.Space, 0.15f);
			p.Mono(0.03f);
		}));
		spec.About = "A plucked saw, for the test";
		return spec;
	}

	private const string Song = """
		{
		  "format": "lunelle-song/3",
		  "cues": { "intensity": 0.5, "mood": null },
		  "about": "A song for the test",
		  "tempo": 118,
		  "scale": "F# minor",
		  "patches": { "bright-pluck": { "base": "pluck", "set": { "filter.cutoff": 3600 } } },
		  "groups": [{ "name": "amp", "effects": [{ "fx": "distortion", "driveDb": 18, "toneHz": 3500, "cabinet": "fourByTwelve" }] }],
		  "variations": [{ "name": "fill", "options": 3, "pick": "random", "weights": [2, 1, 1] }, { "name": "counter", "options": 2, "pick": "cycle" }],
		  "tracks": [
		    { "name": "drums", "kit": "general", "level": -6, "humanise": { "ms": 4, "velocity": 0.1 } },
		    { "name": "lead", "patch": "pluck", "group": "amp", "octave": 5, "pan": -0.2, "macros": [{ "macro": "bright", "follow": "intensity", "from": 0.2, "to": 0.9 }] },
		    { "name": "keys", "patch": "lunelle:keys/electric-piano" },
		    { "name": "solo", "patch": "bright-pluck" }
		  ],
		  "sections": [
		    {
		      "name": "verse", "bars": 2, "chords": "i | VI",
		      "parts": [
		        { "track": "drums", "beat": "eighths", "rows": { "kick": "x... x...", "snare": "..x. ..x." }, "endWith": { "beat": "sixteenths", "rows": { "snare": "x.x. x.x. xxxx XXXX" } } },
		        { "track": "lead", "name": "riff", "part": { "code": "Riff" }, "when": "intensity > 0.4 and not mood is Dark", "onPhrase": true },
		        { "track": "keys", "sequence": [{ "line": "quarters", "notes": "1 3 5 3", "times": 1 }, { "line": "quarters", "notes": "5 . . .", "velocity": 0.5 }] }
		      ],
		      "sweeps": [{ "track": "lead", "macro": 0, "from": 0, "to": 1 }],
		      "exits": [{ "when": "intensity > 0.7 and mood is Calm", "then": "chorus" }, { "then": "verse" }]
		    },
		    {
		      "name": "chorus", "bars": 2, "chords": "III | VII",
		      "parts": [
		        { "track": "lead", "strum": "eighths", "notes": "D.DU .UDU", "voicing": "triad", "when": "(intensity < 0.2 or mood is Calm)" },
		        { "track": "solo", "name": "half the time", "line": "quarters", "notes": "1 . 5 .", "when": "fill is 2 or counter is 1", "velocity": 0.8 }
		      ],
		      "exits": [{ "when": "pass < 2", "then": "chorus" }, { "then": "verse" }]
		    }
		  ],
		  "hooks": ["OnBuilt"]
		}
		""";

	private sealed class Text(string path, string text) : AdditionalText
	{
		public override string Path { get; } = path;

		public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
	}

	private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
	{
		public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
	}

	private sealed class Provider(AnalyzerConfigOptions global) : AnalyzerConfigOptionsProvider
	{
		public override AnalyzerConfigOptions GlobalOptions => global;

		public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => global;

		public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => global;
	}

	// Lunelle's own sounds, as its package hands them to the generator
	private static string LibraryFolder([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
		Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "BinaryVibrance.Lunelle", "Library"));

	// The project compiled with the generator over its files (the manifest and cues as above unless given): what it made, and every diagnostic
	private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult Run) Generate(params (string Path, string Text)[] files)
	{
		var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
			.Select(path => MetadataReference.CreateFromFile(path))
			.Append(MetadataReference.CreateFromFile(typeof(Patch).Assembly.Location))
			.Append(MetadataReference.CreateFromFile(typeof(AudioEffect).Assembly.Location));
		var parse = new CSharpParseOptions(LanguageVersion.Preview);
		var compilation = CSharpCompilation.Create("Tunes", [CSharpSyntaxTree.ParseText(Source, parse)], references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
		var project = Path.Combine(Path.GetTempPath(), "tunes");
		var all = files.ToList();
		if (!all.Exists(f => f.Path == "lunelle.meta.json"))
			all.Add(("lunelle.meta.json", Manifest));
		if (!all.Exists(f => f.Path.StartsWith("Cues/", StringComparison.Ordinal)))
			all.Add(("Cues/tunes.cues.json", Cues));
		var library = LibraryFolder();
		var texts = all.Select(f => (AdditionalText)new Text(Path.Combine(project, f.Path), f.Text))
			.Concat(Directory.EnumerateFiles(library, "*.json", SearchOption.AllDirectories).Select(f => (AdditionalText)new Text(f, File.ReadAllText(f))));
		GeneratorDriver driver = CSharpGeneratorDriver.Create([new MusicGenerator().AsSourceGenerator()], [.. texts], parse,
			new Provider(new Options(new()
			{
				["build_property.RootNamespace"] = "Tunes",
				["build_property.ProjectDir"] = project + Path.DirectorySeparatorChar,
				["build_property.LunelleLibraryDir"] = library,
			})));
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
		return (output, diagnostics, driver.GetRunResult());
	}

	private static System.Reflection.Assembly Emit(Compilation compilation)
	{
		using var image = new MemoryStream();
		var result = compilation.Emit(image);
		result.Success.ShouldBeTrue(string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
		return System.Reflection.Assembly.Load(image.ToArray());
	}

	private static string Generated(GeneratorDriverRunResult run, string hint) =>
		run.Results.Single().GeneratedSources.Single(s => s.HintName == hint).SourceText.ToString();

	[Test]
	public void A_project_s_music_is_made_into_the_code_that_builds_what_its_loaders_do()
	{
		var patchFile = PatchFile.Write(PluckSpec(), ["pick", "saw", "breath"]);
		var (output, diagnostics, run) = Generate(("Patches/pluck.patch.json", patchFile), ("Songs/tune.song.json", Song));
		diagnostics.ShouldBeEmpty();
		run.Results.Single().GeneratedSources.Select(s => s.HintName)
			.ShouldBe(["Patches.g.cs", "Cues.g.cs", "Macros.g.cs", "Tunes.Songs.Tune.g.cs", "SongFiles.g.cs"], ignoreOrder: true);
		// A file's patch and song are listed as they are added, for a catalog to take without code of its own
		var patchesCode = Generated(run, "Patches.g.cs");
		patchesCode.ShouldContain("(\"pluck\", () => Pluck),");
		Generated(run, "SongFiles.g.cs").ShouldContain("(\"tune\", global::Tunes.Songs.Tune.Build),");
		patchesCode.ShouldContain("public static Patch Pluck => Patch.Build(\"pluck\", p =>");
		patchesCode.ShouldContain("var pick = p.Sine(3f, Env.Decay(0.05f), 0.7f, velocity: 0.5f);");
		patchesCode.ShouldContain("// A plucked saw, for the test");
		patchesCode.ShouldContain("p.Macro(Macros.Bright, MacroTarget.FilterCutoff, 0.5f, 2f);");
		// A library's sound is nested by its library and id
		patchesCode.ShouldContain("public static partial class Lunelle");
		patchesCode.ShouldContain("public static Patch ElectricPiano => Patch.Build(\"keys/electric-piano\", p =>");
		var cuesCode = Generated(run, "Cues.g.cs");
		cuesCode.ShouldContain("public static Cue Intensity { get; } = new(\"intensity\", 0.3f);");
		cuesCode.ShouldContain("public static Cue<global::Tunes.Mood> Mood { get; } = new(\"mood\", global::Tunes.Mood.Calm);");
		cuesCode.ShouldContain("// How much is going on");
		Generated(run, "Macros.g.cs").ShouldContain("public static Macro Bright { get; } = new(0);");

		var songCode = Generated(run, "Tunes.Songs.Tune.g.cs");
		songCode.ShouldContain("namespace Tunes.Songs;");
		songCode.ShouldContain("verse.Exit(chorus, Cues.Intensity.Above(0.7f) & Cues.Mood.Is(global::Tunes.Mood.Calm));");
		songCode.ShouldContain("verse.Then(verse);");
		songCode.ShouldContain("var fill = song.Variation(\"fill\", 3, global::BinaryVibrance.Lunelle.Music.VariationPick.Random, [2f, 1f, 1f]);");
		songCode.ShouldContain(".When(fill.Is(2) | counter.Is(1))");
		songCode.ShouldContain(".When(Cues.Intensity.Above(0.4f) & !Cues.Mood.Is(global::Tunes.Mood.Dark))");
		songCode.ShouldContain("Fx.Distortion(driveDb: 18f, toneHz: 3500f, cabinet: global::BinaryVibrance.Sirrow.Effects.Cabinet.FourByTwelve)");
		songCode.ShouldContain("Part.Sequence((Line.Quarters(\"1 3 5 3\"), 1), (Line.Quarters(\"5 . . .\").Velocity(0.5f), 1))");
		songCode.ShouldContain("Patches.Lunelle.Keys.ElectricPiano");
		// The song's own patch, its alone
		songCode.ShouldContain("private static Patch OwnBrightPluck => Patch.Build(\"bright-pluck\", p =>");

		var assembly = Emit(output);
		var patch = (Patch)assembly.GetType("Tunes.Patches")!.GetProperty("Pluck", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
		PatchFile.Write(PatchSpec.From(patch), ["pick", "saw", "breath"]).ShouldBe(PatchFile.Write(PatchFile.Read(patchFile), ["pick", "saw", "breath"]).Replace("  \"about\": \"A plucked saw, for the test\",\n", ""));

		// The generated song and the same files resolved and loaded at run time, written out again: the same song
		var tune = assembly.GetType("Tunes.Songs.Tune")!;
		var generated = (Song)tune.GetMethod("Build")!.Invoke(null, null)!;
		var cues = (Type)assembly.GetType("Tunes.Cues")!;
		CueBase CueOf(string name) => (CueBase)cues.GetProperty(PatchCode.Pascal(name))!.GetValue(null)!;
		var folder = MusicFolder.Load(
			["lunelle.meta.json", "Cues/tunes.cues.json", "Patches/pluck.patch.json", "Songs/tune.song.json"],
			path => System.Text.Encoding.UTF8.GetBytes(path switch
			{
				"lunelle.meta.json" => Manifest,
				"Cues/tunes.cues.json" => Cues,
				"Patches/pluck.patch.json" => patchFile,
				_ => Song,
			}));
		var resolved = folder.Resolved("tune")!;
		var loaded = SongFile.Load(resolved.Json.ToString(), new SongBindings
		{
			Patch = key => key == "pluck" ? patch : resolved.Patches.TryGetValue(key, out var p) ? PatchFile.Load(p.Json.ToString()) : null,
			Kit = name => (DrumKit?)assembly.GetType("Tunes.Kits")!.GetProperty(PatchCode.Pascal(name))?.GetValue(null),
			Cue = name => name is "intensity" or "mood" ? CueOf(name) : null,
			Macro = name => name == "bright" ? new Macro(0) : null,
			Part = name => (Part?)tune.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null),
			Hook = (name, song, sections) => tune.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [song, .. sections]),
		});
		var names = new SongNames { Patch = p => p.Name, Macro = m => m.Id == 0 ? "bright" : null };
		SongFile.Write(generated, names).ShouldBe(SongFile.Write(loaded, names));
		generated.Sections[0].Placements.Count.ShouldBe(4);
		generated.Variations.Select(v => (v.Name, v.Options, v.Pick)).ShouldBe([("fill", 3, VariationPick.Random), ("counter", 2, VariationPick.Cycle)]);
		generated.Variations[0].Weights.ShouldBe([2f, 1f, 1f]);
		// A variation is the song's own, so not among the cues the game must tell it
		generated.Cues.Select(c => c.Name).ShouldNotContain("fill");

		// And it sounds the same
		using var a = new MusicRig();
		using var b = new MusicRig();
		a.Player.Play(generated, fadeSeconds: 0f);
		b.Player.Play(loaded, fadeSeconds: 0f);
		a.Step(MusicRig.Bar * 2);
		b.Step(MusicRig.Bar * 2);
		a.Left().ShouldBe(b.Left());
	}

	[Test]
	public void A_mistake_in_a_music_file_is_an_error_at_its_line()
	{
		var broken = Song.Replace("\"patch\": \"pluck\"", "\"patch\": \"nope\"", StringComparison.Ordinal);
		var (_, diagnostics, _) = Generate(("Patches/pluck.patch.json", PatchFile.Write(PluckSpec())), ("Songs/tune.song.json", broken));
		var error = diagnostics.ShouldHaveSingleItem();
		error.Id.ShouldBe("LUNELLEMUSIC001");
		error.GetMessage().ShouldContain("no patch is 'nope'");
		error.Location.GetLineSpan().StartLinePosition.Line.ShouldBe(broken.Split('\n').ToList().FindIndex(l => l.Contains("\"nope\"", StringComparison.Ordinal)));

		var unclosed = Generate(("Songs/tune.song.json", "{ \"format\": \"lunelle-song/3\"")).Diagnostics.ShouldHaveSingleItem();
		unclosed.GetMessage().ShouldContain("tune.song.json");

		// A song reading a cue it doesn't import
		var unimported = Song.Replace("\"cues\": { \"intensity\": 0.5, \"mood\": null },", "\"cues\": { \"intensity\": 0.5 },", StringComparison.Ordinal);
		Generate(("Patches/pluck.patch.json", PatchFile.Write(PluckSpec())), ("Songs/tune.song.json", unimported)).Diagnostics
			.ShouldContain(d => d.GetMessage().Contains("reads the cue 'mood', which it doesn't import"));

		// Format 1's files, with no manifest, are said to want one
		Generate(("lunelle.meta.json", ""), ("Songs/tune.song.json", Song)).Diagnostics.ShouldNotBeEmpty();
	}

	// The sample's folder, from this file's
	private static string HelloSynthFolder([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
		Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "samples", "HelloSynth"));

	[Test]
	public void HelloSynths_generated_music_is_what_its_files_say()
	{
		var folder = HelloSynthFolder();
		foreach (var (name, build) in HelloSynth.Catalog.Patches)
		{
			var file = File.ReadAllText(Path.Combine(folder, "Patches", name + ".patch.json")).ReplaceLineEndings("\n");
			var spec = PatchFile.Read(file, out var names);
			var made = PatchSpec.From(build());
			made.About = spec.About;
			PatchFile.Write(made, names).ShouldBe(file);
		}
		var song = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "Songs", "demo.song.json")))!.AsObject();
		song.Remove("about");
		song.Remove("cues");
		var generated = SongFile.ToJson(HelloSynth.Catalog.Songs.Single().Build(), new SongNames
		{
			Patch = p => HelloSynth.Catalog.Patches.First(x => x.Build().Name == p.Name).Name,
			Kit = _ => "demo-kit",
			Macro = m => m.Id == 0 ? "bright" : null,
		});
		SongFile.Write(generated).ShouldBe(SongFile.Write(song));
	}
}
