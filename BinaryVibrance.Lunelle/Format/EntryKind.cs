#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>The kinds of entry a project holds, each in a folder of its own (project.md §2).</summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
enum EntryKind
{
	/// <summary><c>Songs/**/*.song.json</c>.</summary>
	Song,

	/// <summary><c>Patches/**/*.patch.json</c>.</summary>
	Patch,

	/// <summary><c>Kits/**/*.kit.json</c>, or <c>Kits/**/kit.json</c> for a kit with a folder of its own.</summary>
	Kit,

	/// <summary><c>Patterns/**/*.pattern.json</c>.</summary>
	Pattern,
}
