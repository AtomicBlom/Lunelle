using System.Globalization;

using Microsoft.CodeAnalysis;

using BinaryVibrance.Lunelle.Music.Generator.Format;

namespace BinaryVibrance.Lunelle.Music.Generator;

/// <summary>
/// What the generator knows beyond a file: the project as format 2 resolves it, the classes its music is written into
/// (<c>Patches</c>, <c>Kits</c>, <c>Cues</c>, <c>Macros</c> unless the project says otherwise), the song being written,
/// and from the compilation what the files alone can't say: a sound of the game's own in C#, an effect's argument
/// types, a bound enum, the drum pieces Lunelle names.
/// </summary>
internal sealed class MusicContext(Compilation compilation, MusicOptions options, Resolver resolver)
{
	public MusicOptions Options { get; } = options;

	public Resolver Resolver { get; } = resolver;

	/// <summary>The song being written, resolved, while it is.</summary>
	public ResolvedSong? Song { get; set; }

	/// <summary>The C# path below a kind's class of an entry by its canonical key: <c>lunelle:keys/electric-piano</c> is <c>Lunelle.Keys.ElectricPiano</c>.</summary>
	public static string MemberPath(string key) =>
		string.Join(".", key.Split(':').SelectMany(part => part.Split('/')).Select(s => Code.Identifier(Code.Pascal(s))));

	/// <summary>A song's own sound's member in its class: <c>~finale</c> is <c>OwnFinale</c>.</summary>
	public static string OwnMember(string key) => "Own" + Code.Identifier(Code.Pascal(key.TrimStart('~').Replace('/', '-')));

	public bool HasPatch(string key) =>
		Song?.Patches.ContainsKey(key) == true || HasMember(Options.Patches, Code.Pascal(key));

	public bool HasKit(string key) =>
		Song?.Kits.ContainsKey(key) == true || HasMember(Options.Kits, Code.Pascal(key));

	public string Patch(string key) =>
		key.StartsWith("~", StringComparison.Ordinal) ? OwnMember(key)
		: Song?.Patches.ContainsKey(key) == true ? $"{Options.Patches}.{MemberPath(key)}"
		: $"{Options.Patches}.{Code.Identifier(Code.Pascal(key))}";

	public string Kit(string key) =>
		key.StartsWith("~", StringComparison.Ordinal) ? OwnMember(key)
		: Song?.Kits.ContainsKey(key) == true ? $"{Options.Kits}.{MemberPath(key)}"
		: $"{Options.Kits}.{Code.Identifier(Code.Pascal(key))}";

	/// <summary>The C# path below the cues' class of a cue: its library's route, its namespace, its name.</summary>
	public static string CuePath(ProjectLibrary owner, string id) =>
		MemberPath(owner.Key.Length == 0 ? id : owner.Key + ":" + id);

	/// <summary>A cue the song names, as it imports it: <c>Cues.Lava.Heat</c>.</summary>
	/// <exception cref="FormatError">The song doesn't import it.</exception>
	public string Cue(string reference) =>
		Song?.Cue(reference) is { } import
			? $"{Options.Cues}.{CuePath(import.Owner, import.Definition.Id)}"
			: throw new FormatError($"the song reads the cue '{reference}', which it doesn't import: add it to its \"cues\"", Song?.File.Json["cues"]);

	/// <summary>Whether the song imports a cue by <paramref name="reference"/>.</summary>
	public bool HasCue(string reference) => Song?.Cue(reference) is not null;

	/// <summary>Whether <paramref name="reference"/> names a cue the project has, imported or not.</summary>
	public bool FindsCue(string reference) => Song?.Cue(reference) is not null
		|| (Song is { } song && Resolver.FindCue(song.File.Owner, reference, out _) is not null);

	/// <summary>The enum a choice cue chooses among, as C# names it in full, or null for a level.</summary>
	public string? CueEnum(string reference)
	{
		if (Song?.Cue(reference) is not { Definition.IsChoice: true } import)
			return null;
		return import.Definition.Enum is { } bound
			? "global::" + bound
			: $"global::{Options.RootNamespace}.{Options.Cues}.{CuePath(import.Owner, import.Definition.Id)}Option";
	}

	/// <summary>A macro as the file gives it: a name is a member of the macros' class, a number <c>new Macro(n)</c>.</summary>
	public string Macro(JNode macro)
	{
		if (macro.Str is { } name)
			return $"{Options.Macros}.{Code.Identifier(Code.Pascal(name))}";
		if (macro.Num is { } number && int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
			return Resolver.Project.Root.Manifest.Macros.FirstOrDefault(m => m.Number == id).Name is { } named
				? $"{Options.Macros}.{Code.Identifier(Code.Pascal(named))}"
				: $"new Macro({id})";
		throw new FormatError("a macro is a name or a number", macro);
	}

	/// <summary>A drum piece by its id: Lunelle's <c>Kit.HatClosed</c> where it names one, else <c>new DrumPiece("id")</c>.</summary>
	public string Piece(string id)
	{
		var member = Code.Pascal(id);
		var kit = compilation.GetTypeByMetadataName("BinaryVibrance.Lunelle.Chip.Kit");
		return kit is not null && kit.GetMembers(member).Any(m => m.IsStatic) ? $"Kit.{member}" : $"new DrumPiece({Code.Quote(id)})";
	}

	/// <summary>The enum type <paramref name="name"/> (a bound cue's), or null.</summary>
	public INamedTypeSymbol? Enum(string name) => compilation.GetTypeByMetadataName(name);

	/// <summary>An effect as the file names it: <c>Fx.Compressor(thresholdDb: -18f, ratio: 3f)</c>.</summary>
	public string Effect(JNode effect)
	{
		var fx = effect["fx"]?.Str ?? throw new FormatError("an effect names its factory (\"fx\": \"chorus\")", effect);
		var type = compilation.GetTypeByMetadataName("BinaryVibrance.Sirrow.Effects.Fx");
		var method = type?.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m => string.Equals(m.Name, fx, StringComparison.OrdinalIgnoreCase));
		if (type is not null && method is null)
			throw new FormatError($"no effect is called \"{fx}\"", effect["fx"]!);
		var arguments = new List<string>();
		foreach (var member in ((JObject)effect).Members)
		{
			if (member.Key == "fx")
				continue;
			var parameter = method?.Parameters.FirstOrDefault(p => string.Equals(p.Name, member.Key, StringComparison.OrdinalIgnoreCase));
			if (method is not null && parameter is null)
				throw new FormatError($"{fx} takes no \"{member.Key}\"", member.KeyStart, member.Key.Length + 2);
			var name = parameter?.Name ?? member.Key;
			arguments.Add($"{name}: {Argument(member.Value, parameter?.Type)}");
		}
		return $"Fx.{method?.Name ?? Code.Pascal(fx)}({string.Join(", ", arguments)})";
	}

	private static string Argument(JNode value, ITypeSymbol? type)
	{
		if (value.Num is { } number)
			return type?.SpecialType is SpecialType.System_Int32 ? number : Code.F(number);
		if (value is JBool b)
			return b.Value ? "true" : "false";
		if (value.Str is { } text && type is INamedTypeSymbol { TypeKind: TypeKind.Enum } choice)
		{
			var member = choice.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => string.Equals(f.Name, text, StringComparison.OrdinalIgnoreCase))
				?? throw new FormatError($"\"{text}\" isn't a {choice.Name} ({string.Join(", ", choice.GetMembers().OfType<IFieldSymbol>().Select(f => f.Name))})", value);
			return $"{choice.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{member.Name}";
		}
		throw new FormatError($"{value} isn't an argument this can write", value);
	}

	/// <summary>
	/// Whether the game declares <paramref name="member"/> in its own C# class <paramref name="type"/> (a sound of its own),
	/// a dotted path reaching into its nested classes as the generator nests them: <c>Lunelle.General</c> for a library's
	/// sampled kit is <c>Kits.Lunelle.General</c>.
	/// </summary>
	public bool HasMember(string type, string member)
	{
		var path = member.Split('.');
		IEnumerable<INamedTypeSymbol> types = compilation.GetSymbolsWithName(type, SymbolFilter.Type).OfType<INamedTypeSymbol>();
		for (var i = 0; i < path.Length - 1; i++)
		{
			var nested = path[i];
			types = types.SelectMany(t => t.GetTypeMembers(nested));
		}
		var name = path[path.Length - 1];
		return types.Any(t => t.GetMembers(name).Any(m => m.IsStatic && !m.IsImplicitlyDeclared && m.DeclaringSyntaxReferences.Length > 0));
	}
}
