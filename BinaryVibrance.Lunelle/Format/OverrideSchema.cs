#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>
/// What an override's path may name (overrides.md §3): each object's properties, whether the file writes them or not,
/// and each keyed array's key. An array not keyed here is a value, which a <c>set</c> replaces whole.
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class OverrideSchema
{
	private readonly Dictionary<string, OverrideSchema?> _properties = new(StringComparer.Ordinal);

	private OverrideSchema(Func<JNode, string?>? key = null) => Key = key;

	/// <summary>For a keyed array's items: an item's key, or null when it has none. Null for an object that isn't an array's item.</summary>
	public Func<JNode, string?>? Key { get; }

	/// <summary>Whether <paramref name="name"/> is one of its properties.</summary>
	public bool Has(string name) => _properties.ContainsKey(name);

	/// <summary>The schema under property <paramref name="name"/>: an object's, a keyed array's items', or null for a plain value.</summary>
	public OverrideSchema? Under(string name) => _properties.TryGetValue(name, out var under) ? under : null;

	/// <summary>Its properties.</summary>
	public IEnumerable<string> Properties => _properties.Keys;

	private OverrideSchema With(string name, OverrideSchema? under = null)
	{
		_properties[name] = under;
		return this;
	}

	private OverrideSchema Values(params string[] names)
	{
		foreach (var name in names)
			_properties[name] = null;
		return this;
	}

	// Whether a property is a keyed array (its schema has a Key) rather than an object
	internal static bool IsKeyed(OverrideSchema? schema) => schema?.Key is not null;

	/// <summary>A patch file's (<c>lunelle-patch/2</c>).</summary>
	public static OverrideSchema Patch { get; } = MakePatch();

	/// <summary>A kit file's (<c>lunelle-kit/1</c>).</summary>
	public static OverrideSchema Kit { get; } = MakeKit();

	/// <summary>The schema for <paramref name="kind"/>, or null for a kind that isn't overridden (songs, patterns).</summary>
	public static OverrideSchema? For(EntryKind kind) => kind switch
	{
		EntryKind.Patch => Patch,
		EntryKind.Kit => Kit,
		_ => null,
	};

	private static OverrideSchema MakePatch()
	{
		var modulation = new OverrideSchema(item => item["op"]?.Str).Values("op", "index");
		var driver = new OverrideSchema(item => item.Str);
		var op = new OverrideSchema(item => item["name"]?.Str)
			.Values("name", "kind", "ratio", "fixed", "detune", "phase", "envelope", "level", "velocity", "feedback", "pulseWidth",
				"pink", "oneShot", "decaySeconds", "damping", "pick", "modes", "keyLevel", "keyDecay")
			.With("modulatedBy", modulation)
			.With("drivenBy", driver);
		var output = new OverrideSchema(item => item["op"]?.Str).Values("op", "level", "pan");
		var macro = new OverrideSchema(MacroKey).Values("macro", "target", "from", "to", "op");
		return new OverrideSchema()
			.Values("format", "title", "about", "drive", "gain", "pan")
			.With("operators", op)
			.With("out", output)
			.With("filter", new OverrideSchema().Values("kind", "cutoff", "q", "keyTrack", "velocity", "envelopeOctaves", "envelope"))
			.With("sends", new OverrideSchema().Values("space", "echo", "key"))
			.With("vibrato", new OverrideSchema().Values("rate", "depth", "delay"))
			.With("tremolo", new OverrideSchema().Values("rate", "depth"))
			.With("pitchDrop", new OverrideSchema().Values("semitones", "seconds"))
			.With("mono", new OverrideSchema().Values("glide"))
			.With("macros", macro);
	}

	private static OverrideSchema MakeKit()
	{
		var layer = new OverrideSchema(item => item["upTo"]?.Num is { } upTo ? Number(upTo) : null).Values("upTo", "takes");
		var piece = new OverrideSchema(item => item["piece"]?.Str).Values("piece", "patch", "gainDb", "pan", "choke").With("layers", layer);
		return new OverrideSchema().Values("format", "title", "about", "source").With("pieces", piece);
	}

	/// <summary>A patch macro's key: <c>macro/target</c>, and <c>/op</c> for a target on one operator (<c>0/filterOctaves</c>, <c>2/opLevel/tine</c>).</summary>
	public static string? MacroKey(JNode item)
	{
		var macro = item["macro"]?.Num ?? item["macro"]?.Str;
		var target = item["target"]?.Str;
		if (macro is null || target is null)
			return null;
		return item["op"]?.Str is { } op ? $"{macro}/{target}/{op}" : $"{macro}/{target}";
	}

	/// <summary>A layer's <c>upTo</c> as a key: the number, written the shortest way (<c>0.35</c>), so <c>0.350</c> finds it too.</summary>
	public static string Number(string text) =>
		double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
			? value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
			: text;
}
