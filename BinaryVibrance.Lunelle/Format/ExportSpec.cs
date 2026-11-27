using System.Globalization;

#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>
/// A song's export as its file writes it (exports.md §2): a scripted run of the song, from where it starts to how it
/// ends, with the cues and macros it changes on the way, rendered the same every time (Lunelle's <c>SongExport</c>).
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class ExportSpec
{
	private ExportSpec(string name, JObject json)
	{
		Name = name;
		Json = json;
	}

	/// <summary>Its name in the song, an id segment: the export's full id is <c>song/name</c>.</summary>
	public string Name { get; }

	/// <summary>Its JSON as written.</summary>
	public JObject Json { get; }

	/// <summary>Its title, or null.</summary>
	public string? Title => Json["title"]?.Str;

	/// <summary>What it is, or null.</summary>
	public string? About => Json["about"]?.Str;

	/// <summary>The seed its variations draw from.</summary>
	public uint Seed { get; private set; }

	/// <summary>The scale it plays in, as written ("G minor"), or null for the song's own.</summary>
	public string? Scale => Json["scale"]?.Str;

	/// <summary>The section it starts at, or null for the song's first.</summary>
	public string? Start => Json["start"]?.Str;

	/// <summary>The starting value of each cue it gives, by the cue as the song imports it.</summary>
	public IReadOnlyList<(string Cue, JNode Value)> Cues { get; private set; } = System.Array.Empty<(string, JNode)>();

	/// <summary>Its events, in the order written.</summary>
	public IReadOnlyList<ExportEvent> Events { get; private set; } = System.Array.Empty<ExportEvent>();

	/// <summary>Where the end begins.</summary>
	public ExportPosition End { get; private set; } = null!;

	/// <summary>How long the music fades after <see cref="End"/>, in seconds: 0 stops it there.</summary>
	public double FadeSeconds { get; private set; }

	/// <summary>How long the render goes on after the music stops, in seconds; null to go on until it is silent (10 s at most).</summary>
	public double? TailSeconds { get; private set; }

	/// <summary>Where the loop starts and ends in the rendered file, or null for no loop.</summary>
	public (ExportPosition From, ExportPosition To)? Loop { get; private set; }

	/// <summary>
	/// The exports a song's JSON lists (its <c>"exports"</c>), checked against what it plays: its imported cues, tracks,
	/// sections and the project's macros. Each problem is said through <paramref name="problem"/>; an export with one is left out.
	/// </summary>
	public static List<ExportSpec> Read(JObject song, ResolvedSong resolved, IReadOnlyCollection<string> macros, Action<string, JNode?> problem)
	{
		var exports = new List<ExportSpec>();
		if (song["exports"] is not { } node)
			return exports;
		var sections = new HashSet<string>(song["sections"]?.Items.Select(s => s["name"]?.Str ?? "") ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
		var tracks = new HashSet<string>(song["tracks"]?.Items.Select(t => t["name"]?.Str ?? "") ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
		var names = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in node.Items)
		{
			if (item is not JObject json)
			{
				problem("an export is an object: { \"name\", \"end\", … }", item);
				continue;
			}
			var name = json["name"]?.Str;
			if (name is null || EntryIds.SegmentProblem(name) is not null)
			{
				problem(name is null ? "an export has a \"name\"" : $"the export '{name}' isn't named as an id: {EntryIds.SegmentProblem(name)}", json["name"] ?? json);
				continue;
			}
			if (!names.Add(name))
			{
				problem($"two exports are named '{name}'", json["name"]);
				continue;
			}
			var ok = true;
			void Bad(string message, JNode? at)
			{
				problem($"the export '{name}': {message}", at);
				ok = false;
			}
			var export = new ExportSpec(name, json);
			if (json["seed"] is { } seed)
			{
				if (seed.Num is { } n && uint.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
					export.Seed = value;
				else
					Bad("\"seed\" is a whole number, 0 or more", seed);
			}
			if (export.Start is { } start && !sections.Contains(start))
				Bad($"it starts at '{start}', which isn't a section", json["start"]);

			var cues = new List<(string, JNode)>();
			if (json["cues"] is JObject startCues)
				foreach (var member in startCues.Members)
				{
					var at = new JString(member.Key) { Start = member.KeyStart, Length = member.Key.Length + 2 };
					if (resolved.Cue(member.Key) is not { } cue)
						Bad($"it starts '{member.Key}', which the song doesn't import", at);
					else if (CueValueProblem(cue.Definition, member.Value, allowNull: false) is { } wrong)
						Bad($"'{member.Key}' {wrong}", member.Value);
					else
						cues.Add((member.Key, member.Value));
				}
			export.Cues = cues;

			var events = new List<ExportEvent>();
			foreach (var e in json["events"]?.Items ?? System.Array.Empty<JNode>())
			{
				string? why = null;
				var position = e["at"]?.Str is { } text ? ExportPosition.Parse(text, e["at"]!, out why) : null;
				if (position is null)
				{
					Bad(why ?? "an event says when it happens, \"at\"", e["at"] ?? e);
					continue;
				}
				if (position.Section is { } section && !sections.Contains(section))
				{
					Bad($"an event happens at '{section}', which isn't a section", e["at"]);
					continue;
				}
				ExportDuration? over = null;
				if (e["over"] is { } overNode)
				{
					over = overNode.Str is { } overText ? ExportDuration.Parse(overText) : null;
					if (over is null)
					{
						Bad("\"over\" is \"<n> bars\", \"<n> beats\" or \"<n>s\"", overNode);
						continue;
					}
				}
				var made = new ExportEvent(e, position, over);
				if (e["jump"]?.Str is { } jump)
				{
					if (!sections.Contains(jump))
						Bad($"an event jumps to '{jump}', which isn't a section", e["jump"]);
					else
						events.Add(made);
				}
				else if (e["cue"]?.Str is { } cueRef)
				{
					var cue = resolved.Cue(cueRef);
					if (cue is null)
						Bad($"an event sets '{cueRef}', which the song doesn't import", e["cue"]);
					else if (e["to"] is not { } to || CueValueProblem(cue.Definition, to, allowNull: false) is { } wrong)
						Bad($"an event sets '{cueRef}' \"to\" {(e["to"] is null ? "something" : CueValueProblem(cue.Definition, e["to"]!, false))}", e["to"] ?? e);
					else if (over is not null && cue.Definition.IsChoice)
						Bad($"'{cueRef}' is a choice, which is set, not ramped \"over\" a time", e["over"]);
					else
						events.Add(made);
				}
				else if (e["track"]?.Str is { } track)
				{
					var macro = e["macro"];
					if (!tracks.Contains(track))
						Bad($"an event steers the track '{track}', which isn't one", e["track"]);
					else if (macro?.Str is { } macroName ? !macros.Contains(macroName) : macro?.Num is null)
						Bad($"an event steers the macro {macro?.ToString() ?? "(none)"}, which the project doesn't name", macro ?? e);
					else if (e["to"] is not { } to || !(to is JNull || to is JNumber { Value: >= 0 and <= 1 }))
						Bad("a macro is held \"to\" a value from 0 to 1, or let go with null", e["to"] ?? e);
					else if (over is not null && e["to"] is JNull)
						Bad("a macro let go isn't ramped \"over\" a time", e["over"]);
					else
						events.Add(made);
				}
				else
					Bad("an event sets a \"cue\", steers a \"track\"'s \"macro\", or makes a \"jump\"", e);
			}
			export.Events = events;

			if (json["end"] is not JObject end || end["at"]?.Str is not { } endAt)
				Bad("it says how it ends: \"end\": { \"at\": \"section outro end\" }", json["end"] ?? json);
			else if (ExportPosition.Parse(endAt, end["at"]!, out var endWhy) is not { } endPosition)
				Bad(endWhy!, end["at"]);
			else if (endPosition.Section is { } endSection && !sections.Contains(endSection))
				Bad($"it ends at '{endSection}', which isn't a section", end["at"]);
			else
			{
				export.End = endPosition;
				if (end["fade"] is { } fade)
				{
					if (fade.Str is { } fadeText && ExportDuration.Parse(fadeText) is { Unit: DurationUnit.Seconds } seconds)
						export.FadeSeconds = seconds.Amount;
					else
						Bad("\"fade\" is a time in seconds (\"8s\")", fade);
				}
				if (end["tail"] is { } tail && tail.Str != "auto")
				{
					if (tail.Str is { } tailText && ExportDuration.Parse(tailText) is { Unit: DurationUnit.Seconds } seconds)
						export.TailSeconds = seconds.Amount;
					else
						Bad("\"tail\" is \"auto\" or a time in seconds (\"3s\")", tail);
				}
			}

			if (json["loop"] is JObject loop)
			{
				var from = loop["from"]?.Str is { } f ? ExportPosition.Parse(f, loop["from"]!, out _) : null;
				var to = loop["to"]?.Str is { } t ? ExportPosition.Parse(t, loop["to"]!, out _) : null;
				if (from is null || to is null)
					Bad("\"loop\" is { \"from\": <position>, \"to\": <position> }", loop);
				else
					export.Loop = (from, to);
			}
			if (ok)
				exports.Add(export);
		}
		return exports;
	}

	// Why a value can't be a cue's (a level from 0 to 1, or a choice's option by name), or null when it can
	private static string? CueValueProblem(CueDefinition cue, JNode value, bool allowNull)
	{
		if (allowNull && value is JNull)
			return null;
		if (cue.IsChoice)
			return value.Str is { } option && cue.OptionIndex(option) >= 0
				? null
				: $"is a choice: one of {string.Join(", ", cue.Options.Select(o => o.Name))}";
		return value is JNumber { Value: >= 0 and <= 1 } ? null : "is a level, from 0 to 1";
	}
}

/// <summary>An export's event (exports.md §3): when, and what it does.</summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class ExportEvent
{
	/// <summary>An event.</summary>
	public ExportEvent(JNode json, ExportPosition at, ExportDuration? over)
	{
		Json = json;
		At = at;
		Over = over;
	}

	/// <summary>Its JSON as written.</summary>
	public JNode Json { get; }

	/// <summary>When it happens.</summary>
	public ExportPosition At { get; }

	/// <summary>How long it ramps, or null to set at once.</summary>
	public ExportDuration? Over { get; }

	/// <summary>The cue it sets, as the song imports it, or null.</summary>
	public string? Cue => Json["cue"]?.Str;

	/// <summary>The track whose macro it holds, or null.</summary>
	public string? Track => Json["track"]?.Str;

	/// <summary>The macro it holds, by name or number, or null.</summary>
	public JNode? Macro => Json["macro"];

	/// <summary>The value it goes to: a level, an option's name, or null to let a macro go.</summary>
	public JNode? To => Json["to"];

	/// <summary>The section it jumps to, or null.</summary>
	public string? Jump => Json["jump"]?.Str;
}

/// <summary>
/// Where in an export something happens (exports.md §3.1): bar and beat from its start (<c>"17:1"</c>, <c>"17:2.5"</c>),
/// or a section's start or end, the first time or the nth (<c>"section lift#2"</c>, <c>"section outro end"</c>).
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class ExportPosition
{
	private ExportPosition(string text, JNode at)
	{
		Text = text;
		At = at;
	}

	/// <summary>As written.</summary>
	public string Text { get; }

	/// <summary>Where it is written.</summary>
	public JNode At { get; }

	/// <summary>The bar, 1 the first, for a bar position.</summary>
	public int Bar { get; private set; }

	/// <summary>The beat in the bar, 1 the first, for a bar position.</summary>
	public double Beat { get; private set; } = 1;

	/// <summary>The section, for a section position, or null.</summary>
	public string? Section { get; private set; }

	/// <summary>Which time the section comes round, 1 the first.</summary>
	public int Time { get; private set; } = 1;

	/// <summary>Whether it is where that pass of the section ends, rather than starts.</summary>
	public bool IsEnd { get; private set; }

	/// <summary>Reads <paramref name="text"/>, or says why it isn't a position.</summary>
	public static ExportPosition? Parse(string text, JNode at, out string? why)
	{
		why = null;
		var position = new ExportPosition(text, at);
		var trimmed = text.Trim();
		if (trimmed.StartsWith("section ", StringComparison.Ordinal))
		{
			var rest = trimmed.Substring("section ".Length).Trim();
			if (rest.EndsWith(" end", StringComparison.Ordinal))
			{
				position.IsEnd = true;
				rest = rest.Substring(0, rest.Length - " end".Length).Trim();
			}
			var hash = rest.IndexOf('#');
			if (hash >= 0)
			{
				if (!int.TryParse(rest.Substring(hash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var time) || time < 1)
				{
					why = $"\"{text}\" isn't a position: after '#' comes which time, 1 or more";
					return null;
				}
				position.Time = time;
				rest = rest.Substring(0, hash);
			}
			if (rest.Length == 0)
			{
				why = $"\"{text}\" isn't a position: it names no section";
				return null;
			}
			position.Section = rest;
			return position;
		}
		var colon = trimmed.IndexOf(':');
		if (colon > 0
			&& int.TryParse(trimmed.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out var bar) && bar >= 1
			&& double.TryParse(trimmed.Substring(colon + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var beat) && beat >= 1)
		{
			position.Bar = bar;
			position.Beat = beat;
			return position;
		}
		why = $"\"{text}\" isn't a position: \"17:1\" (bar and beat), \"section lift\", \"section lift#2\" or \"section lift end\"";
		return null;
	}

	/// <summary>As written.</summary>
	public override string ToString() => Text;
}

/// <summary>What a duration is counted in.</summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
enum DurationUnit
{
	/// <summary>Bars of the song's meter.</summary>
	Bars,

	/// <summary>Beats.</summary>
	Beats,

	/// <summary>Seconds.</summary>
	Seconds,
}

/// <summary>A duration as an export writes it: <c>"2 bars"</c>, <c>"4 beats"</c>, <c>"3s"</c>.</summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
readonly struct ExportDuration
{
	/// <summary>A duration.</summary>
	public ExportDuration(double amount, DurationUnit unit)
	{
		Amount = amount;
		Unit = unit;
	}

	/// <summary>How many.</summary>
	public double Amount { get; }

	/// <summary>Of what.</summary>
	public DurationUnit Unit { get; }

	/// <summary>Reads <paramref name="text"/>, or null when it isn't a duration.</summary>
	public static ExportDuration? Parse(string text)
	{
		var trimmed = text.Trim();
		foreach (var (suffix, unit) in new[] { (" bars", DurationUnit.Bars), (" bar", DurationUnit.Bars), (" beats", DurationUnit.Beats), (" beat", DurationUnit.Beats), ("s", DurationUnit.Seconds) })
			if (trimmed.EndsWith(suffix, StringComparison.Ordinal)
				&& double.TryParse(trimmed.Substring(0, trimmed.Length - suffix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) && amount >= 0)
				return new ExportDuration(amount, unit);
		return null;
	}
}
