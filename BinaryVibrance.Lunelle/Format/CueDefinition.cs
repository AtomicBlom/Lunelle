using System.Globalization;

#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>
/// A cue as a cue file defines it (cues.md §2): a level from 0 to 1, or a choice among named options, with its id
/// (<c>namespace/name</c>), where it starts, and what it is for.
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class CueDefinition
{
	/// <summary>A level cue.</summary>
	public CueDefinition(string id, string file, JNode at, double initial, string? about)
	{
		Id = id;
		File = file;
		At = at;
		InitialLevel = initial;
		About = about;
		Options = System.Array.Empty<(string, string?)>();
	}

	/// <summary>A choice cue.</summary>
	public CueDefinition(string id, string file, JNode at, IReadOnlyList<(string Name, string? About)> options, int initial, string? about, string? enumType)
	{
		Id = id;
		File = file;
		At = at;
		Options = options;
		InitialOption = initial;
		About = about;
		Enum = enumType;
	}

	/// <summary>Its id in its project: <c>lava/heat</c>, or <c>intensity</c> with no namespace.</summary>
	public string Id { get; }

	/// <summary>The cue file that defines it.</summary>
	public string File { get; }

	/// <summary>Where in that file.</summary>
	public JNode At { get; }

	/// <summary>Whether it is a choice among <see cref="Options"/> rather than a level.</summary>
	public bool IsChoice => Options.Count > 0;

	/// <summary>A choice's options, in order: option <c>i</c> is the value <c>i</c>.</summary>
	public IReadOnlyList<(string Name, string? About)> Options { get; }

	/// <summary>A level's starting value, 0 to 1.</summary>
	public double InitialLevel { get; }

	/// <summary>A choice's starting option, by index.</summary>
	public int InitialOption { get; }

	/// <summary>What it is for, or null.</summary>
	public string? About { get; }

	/// <summary>A game's own enum a choice binds to (<c>"enum": "MyGame.Biome"</c>), or null; only code reads it.</summary>
	public string? Enum { get; }

	/// <summary>The index of option <paramref name="name"/>, or -1.</summary>
	public int OptionIndex(string name)
	{
		for (var i = 0; i < Options.Count; i++)
			if (Options[i].Name == name)
				return i;
		return -1;
	}

	/// <summary>
	/// The cues in a cue file's <paramref name="text"/> (<c>lunelle-cues/1</c>), each problem added to
	/// <paramref name="problems"/> against <paramref name="file"/>; a cue with a problem is left out.
	/// </summary>
	public static List<CueDefinition> Read(string text, string file, List<FormatProblem> problems)
	{
		var cues = new List<CueDefinition>();
		void Problem(string message, JNode? at) => problems.Add(new FormatProblem(file, message, at?.Start ?? 0, at?.Length ?? 0));
		JNode root;
		try
		{
			root = JReader.Parse(text);
		}
		catch (FormatError error)
		{
			problems.Add(new FormatProblem(file, error.Message, error.Start, error.Length));
			return cues;
		}
		if (root["format"]?.Str != "lunelle-cues/1")
		{
			Problem($"a cue file says \"format\": \"lunelle-cues/1\"{(root["format"]?.Str is { } other ? $", not \"{other}\"" : "")}", root["format"] ?? root);
			return cues;
		}
		var space = root["namespace"]?.Str;
		if (space is not null && EntryIds.IdProblem(space) is { } badSpace)
		{
			Problem($"its \"namespace\" isn't made of ids: {badSpace}", root["namespace"]);
			return cues;
		}
		foreach (var cue in root["cues"]?.Items ?? System.Array.Empty<JNode>())
		{
			var name = cue["name"]?.Str;
			if (name is null || EntryIds.SegmentProblem(name) is { } badName)
			{
				Problem(name is null ? "a cue has a \"name\"" : $"cue '{name}' isn't named as an id: {EntryIds.SegmentProblem(name)}", cue["name"] ?? cue);
				continue;
			}
			var id = space is null ? name : space + "/" + name;
			var about = cue["about"]?.Str;
			if (cue["options"] is { } optionsNode)
			{
				var options = new List<(string, string?)>();
				var ok = true;
				foreach (var option in optionsNode.Items)
				{
					var optionName = option.Str ?? option["name"]?.Str;
					if (optionName is null || !IsIdentifier(optionName))
					{
						Problem($"cue '{id}': an option is named as C# names things (\"GoodFuture\"), as it becomes an enum's member", option);
						ok = false;
					}
					else if (options.Exists(o => o.Item1 == optionName))
					{
						Problem($"cue '{id}' has the option '{optionName}' twice", option);
						ok = false;
					}
					else
						options.Add((optionName, option["about"]?.Str));
				}
				if (ok && options.Count < 2)
				{
					Problem($"cue '{id}' chooses among two or more \"options\"", optionsNode);
					ok = false;
				}
				var initial = 0;
				if (ok && cue["initial"] is { } initialNode)
				{
					initial = initialNode.Str is { } initialName ? options.FindIndex(o => o.Item1 == initialName) : -1;
					if (initial < 0)
					{
						Problem($"cue '{id}' starts at \"initial\", which names one of its options ({string.Join(", ", options.Select(o => o.Item1))})", initialNode);
						ok = false;
					}
				}
				if (ok)
					cues.Add(new CueDefinition(id, file, cue, options, initial, about, cue["enum"]?.Str));
			}
			else
			{
				var initial = 0.0;
				if (cue["initial"] is { } initialNode)
				{
					if (initialNode.Num is not { } number || !double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out initial) || initial < 0 || initial > 1)
					{
						Problem($"cue '{id}' is a level, starting from 0 to 1", initialNode);
						continue;
					}
				}
				cues.Add(new CueDefinition(id, file, cue, initial, about));
			}
		}
		return cues;
	}

	/// <summary>Whether <paramref name="name"/> is a C# identifier: a letter or '_', then letters, digits and '_'.</summary>
	public static bool IsIdentifier(string name)
	{
		if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_'))
			return false;
		foreach (var c in name)
			if (!(char.IsLetterOrDigit(c) || c == '_'))
				return false;
		return true;
	}
}
