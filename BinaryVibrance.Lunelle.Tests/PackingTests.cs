using System.IO.Compression;
using System.Text;

using BinaryVibrance.Lunelle.Format;
using BinaryVibrance.Lunelle.Music;

namespace BinaryVibrance.Lunelle.Tests;

/// <summary>
/// A project packed into a <c>.lunelle</c> file and read back (docs/spec/project.md §6): the same bytes every time, the
/// round trip exact, only the library entries used bundled, code and the workbench's files left out, and a zip that
/// would write outside the project refused.
/// </summary>
public class PackingTests
{
	private static Dictionary<string, string> Files()
	{
		var files = MusicFolderTests.Files();
		files[MusicFolder.ProjectFileName] = """
			{ "format": "lunelle/2", "name": "tunes", "macros": { "bright": 0 }, "libraries": { "lunelle": { "version": "1" } } }
			""";
		// Two library sounds: a patch, and a sampled kit (with its folder's samples, licence and notes)
		files["Songs/tune.song.json"] = MusicFolderTests.Song("mood is Calm")
			.Replace("\"patch\": \"click\"", "\"patch\": \"lunelle:keys/electric-piano\"", StringComparison.Ordinal)
			.Replace("\"kit\": \"clicks\"", "\"kit\": \"lunelle:rock\"", StringComparison.Ordinal);
		files["Tunes.csproj"] = "<Project />";
		files["Catalog.cs"] = "class Catalog { }";
		files[".lunelle-studio/workbench.json"] = "{}";
		files["bin/Debug/x.dll"] = "x";
		return files;
	}

	private static ProjectPacker.Packed Pack(Dictionary<string, string> files)
	{
		var folder = MusicFolderTests.Folder(files);
		folder.Problems.ShouldBeEmpty();
		return ProjectPacker.Pack(folder.Project, folder.Resolver);
	}

	private static List<string> Entries(byte[] zip)
	{
		using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
		return [.. archive.Entries.Select(e => e.FullName)];
	}

	[Test]
	public void Packing_is_the_same_bytes_every_time_and_leaves_out_code_and_the_workbench_s_files()
	{
		var first = Pack(Files());
		Pack(Files()).Bytes.ShouldBe(first.Bytes);

		var entries = Entries(first.Bytes);
		entries.ShouldBe([.. entries.Order(StringComparer.Ordinal)]);
		entries.ShouldNotContain(e => e.EndsWith(".cs") || e.EndsWith(".csproj") || e.StartsWith(".lunelle-studio/") || e.StartsWith("bin/"));
		first.Notes.ShouldContain(n => n.StartsWith("Catalog.cs is code"));
		first.Notes.ShouldContain(n => n.StartsWith("Songs/tune.song.json names code"));

		using var archive = new ZipArchive(new MemoryStream(first.Bytes), ZipArchiveMode.Read);
		archive.Entries.ShouldAllBe(e => e.LastWriteTime.Year == 1980);
	}

	[Test]
	public void Only_the_library_entries_used_are_bundled_with_what_they_need()
	{
		var entries = Entries(Pack(Files()).Bytes);
		var bundled = entries.Where(e => e.StartsWith("Libraries/")).ToList();
		bundled.ShouldContain("Libraries/lunelle@1.0.0/lunelle.meta.json");
		bundled.ShouldContain("Libraries/lunelle@1.0.0/Patches/keys/electric-piano.patch.json");
		bundled.ShouldContain("Libraries/lunelle@1.0.0/Kits/rock/kit.json");
		bundled.ShouldContain("Libraries/lunelle@1.0.0/Kits/rock/LICENSE.txt");
		bundled.ShouldContain(e => e.StartsWith("Libraries/lunelle@1.0.0/Kits/rock/") && e.EndsWith(".ogg"));
		bundled.ShouldNotContain(e => e.Contains("/Patches/leads/") || e.Contains("/Kits/general/") || e.Contains("/Kits/synth"));
	}

	[Test]
	public void Unpacked_it_plays_as_the_folder_did_and_packs_again_to_the_same_bytes()
	{
		var packed = Pack(Files()).Bytes;
		var source = ZipSource.Read(packed);
		source.Paths.ShouldContain(EntryIds.Manifest);
		var unpacked = MusicFolder.Load(source, host: _ => null);
		unpacked.Problems.ShouldBeEmpty();
		var manifest = JReader.Parse(source.ReadText(EntryIds.Manifest));
		manifest["libraries"]!["lunelle"]!["path"]!.Str.ShouldBe("Libraries/lunelle@1.0.0");

		var original = MusicFolderTests.Folder(Files());
		Render(unpacked).ShouldBe(Render(original));

		ProjectPacker.Pack(unpacked.Project, unpacked.Resolver).Bytes.ShouldBe(packed);
	}

	private static float[] Render(MusicFolder folder)
	{
		using var rig = new MusicRig();
		rig.Player.Play(folder.Songs[0].Build(), fadeSeconds: 0f);
		rig.Step(MusicRig.Bar);
		return rig.Left();
	}

	[Test]
	[Arguments("../evil.song.json")]
	[Arguments("/abs.song.json")]
	[Arguments("Songs\\back.song.json")]
	public void A_zip_with_a_path_leaving_the_project_is_refused(string path)
	{
		var zip = ProjectPacker.Zip([new(EntryIds.Manifest, Encoding.UTF8.GetBytes("{}")), new(path, Encoding.UTF8.GetBytes("{}"))]);
		Should.Throw<InvalidDataException>(() => ZipSource.Read(zip));
	}

	[Test]
	public void A_zipped_project_folder_opens_from_its_one_folder()
	{
		var files = MusicFolderTests.Files();
		var zip = ProjectPacker.Zip(files.Select(f => new KeyValuePair<string, byte[]>("Tunes/" + f.Key, Encoding.UTF8.GetBytes(f.Value))));
		var folder = MusicFolder.Load(ZipSource.Read(zip));
		folder.Problems.ShouldBeEmpty();
		folder.Songs.Select(s => s.Name).ShouldBe(["tune"]);

		Should.Throw<InvalidDataException>(() => ZipSource.Read(ProjectPacker.Zip([new("Songs/x.song.json", [])])));
	}
}
