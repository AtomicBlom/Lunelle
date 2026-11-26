#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>
/// Applies one derived layer's changes to what lies below it (overrides.md §4): its <c>remove</c>, then its
/// <c>insert</c>, then its <c>set</c>. A path through a keyed item that isn't there is dangling: a warning, skipped. An
/// unknown property, or a value of the wrong shape, is an error, and that change isn't made.
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
static class Overrides
{
	/// <summary>What a derived file may say besides its changes.</summary>
	public static readonly string[] DerivedKeys = { "format", "base", "about", "title", "set", "remove", "insert" };

	/// <summary>
	/// Applies <paramref name="layer"/>'s changes to <paramref name="target"/> (a copy, changed in place), checked against
	/// <paramref name="schema"/>. Each problem is said against <paramref name="file"/>; each path changed is attributed
	/// to <paramref name="layerName"/> in <paramref name="provenance"/>.
	/// </summary>
	public static void Apply(JObject target, JObject layer, OverrideSchema schema, string file, string layerName,
		List<FormatProblem> problems, Dictionary<string, string> provenance)
	{
		void Problem(string message, JNode? at, bool warning = false) =>
			problems.Add(new FormatProblem(file, message, at?.Start ?? 0, at?.Length ?? 0, warning));

		foreach (var removed in layer["remove"]?.Items ?? System.Array.Empty<JNode>())
		{
			if (removed.Str is not { } text)
			{
				Problem("\"remove\" lists paths (\"filter.q\", \"operators[sub]\")", removed);
				continue;
			}
			var path = Checked(text, removed, schema, Problem);
			if (path is null)
				continue;
			if (Walk(target, path, schema, create: false, out var holder, out var last) is { } dangling)
			{
				Problem($"\"remove\": \"{text}\" names {dangling}, which isn't there", removed, warning: true);
				continue;
			}
			var gone = last.Key is null ? holder!.Remove(last.Name) : RemoveItem(holder!, last.Name, last.Key, Under(schema, path));
			if (!gone)
				Problem($"\"remove\": \"{text}\" is gone already", removed, warning: true);
			else
				provenance[text] = layerName;
		}

		foreach (var insert in layer["insert"]?.Items ?? System.Array.Empty<JNode>())
		{
			if (insert["into"]?.Str is not { } into || insert["item"] is not JObject item)
			{
				Problem("an \"insert\" is { \"into\": \"<array>\", \"before\": \"<key>\", \"item\": { … } }", insert);
				continue;
			}
			var path = Checked(into, insert["into"]!, schema, Problem);
			if (path is null)
				continue;
			var items = Under(schema, path);
			if (path.Steps[path.Steps.Count - 1].Key is not null || !OverrideSchema.IsKeyed(items))
			{
				Problem($"\"insert\" goes into a keyed array (\"operators\", \"pieces\"); \"{into}\" isn't one", insert["into"]);
				continue;
			}
			var key = items!.Key!(item);
			if (key is null)
			{
				Problem($"the item inserted into \"{into}\" has no key to be found by", item);
				continue;
			}
			if (Walk(target, path, schema, create: true, out var holder, out var last) is { } dangling)
			{
				Problem($"\"insert\" into \"{into}\" goes through {dangling}, which isn't there", insert, warning: true);
				continue;
			}
			var array = holder!.Get(last.Name) as JArray;
			if (array is null)
				holder.Set(last.Name, array = new JArray());
			if (array.Values.Exists(v => items.Key!(v) == key))
			{
				Problem($"\"insert\" puts '{key}' into \"{into}\", which has one already", item);
				continue;
			}
			var before = insert["before"]?.Str;
			var at = before is null ? array.Values.Count : array.Values.FindIndex(v => items.Key!(v) == before);
			if (at < 0)
			{
				Problem($"\"insert\" goes before '{before}' in \"{into}\", which isn't there", insert["before"], warning: true);
				continue;
			}
			array.Values.Insert(at, item.Clone());
			provenance[$"{into}[{key}]"] = layerName;
		}

		if (layer["set"] is { } setNode)
		{
			if (setNode is not JObject sets)
			{
				Problem("\"set\" is an object of paths and values: { \"filter.cutoff\": 1800 }", setNode);
				return;
			}
			foreach (var member in sets.Members)
			{
				var keyNode = new JString(member.Key) { Start = member.KeyStart, Length = member.Key.Length + 2 };
				var path = Checked(member.Key, keyNode, schema, Problem);
				if (path is null)
					continue;
				var under = Under(schema, path);
				var last = path.Steps[path.Steps.Count - 1];
				if (ShapeProblem(member.Value, under, last.Key is not null) is { } shape)
				{
					Problem($"\"{member.Key}\": {shape}", member.Value);
					continue;
				}
				if (Walk(target, path, schema, create: true, out var holder, out _) is { } dangling)
				{
					Problem($"\"{member.Key}\" goes through {dangling}, which isn't there", keyNode, warning: true);
					continue;
				}
				if (last.Key is null)
					holder!.Set(last.Name, member.Value.Clone());
				else
					SetItem(holder!, last.Name, last.Key, member.Value.Clone(), under!);
				provenance[member.Key] = layerName;
			}
		}
	}

	/// <summary>Problems with keys in <paramref name="json"/>'s keyed arrays: one used twice, or an item with none.</summary>
	public static void CheckKeys(JNode json, OverrideSchema schema, string file, List<FormatProblem> problems)
	{
		if (json is not JObject obj)
			return;
		foreach (var member in obj.Members)
		{
			if (!schema.Has(member.Key) || schema.Under(member.Key) is not { } under)
				continue;
			if (!OverrideSchema.IsKeyed(under))
			{
				CheckKeys(member.Value, under, file, problems);
				continue;
			}
			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (var item in member.Value.Items)
			{
				var key = under.Key!(item);
				if (key is null)
					problems.Add(new FormatProblem(file, $"an item of \"{member.Key}\" has no key to be found by", item.Start, item.Length));
				else if (!seen.Add(key))
					problems.Add(new FormatProblem(file, $"\"{member.Key}\" has '{key}' twice: each is named once", item.Start, item.Length));
				CheckKeys(item, under, file, problems);
			}
		}
	}

	// The path, or null when it names something the schema hasn't (an error)
	private static OverridePath? Checked(string text, JNode at, OverrideSchema schema, Action<string, JNode?, bool> problem)
	{
		var path = OverridePath.Parse(text, out var bad);
		if (bad is not null)
		{
			problem(bad, at, false);
			return null;
		}
		var current = schema;
		for (var i = 0; i < path.Steps.Count; i++)
		{
			var (name, key) = path.Steps[i];
			if (current is null || !current.Has(name))
			{
				problem($"\"{text}\" names '{name}', which {(i == 0 ? "the file" : $"'{path.Steps[i - 1].Name}'")} doesn't have"
					+ (current is null ? "" : $" ({string.Join(", ", current.Properties)})"), at, false);
				return null;
			}
			var under = current.Under(name);
			if (key is not null && !OverrideSchema.IsKeyed(under))
			{
				problem($"\"{text}\": '{name}' isn't a keyed array, so [{key}] names nothing", at, false);
				return null;
			}
			if (i + 1 < path.Steps.Count && (under is null || (key is null && OverrideSchema.IsKeyed(under))))
			{
				problem($"\"{text}\": '{name}' is a value, with nothing under it", at, false);
				return null;
			}
			current = under;
		}
		return path;
	}

	// The schema the path ends at: an object's, an item's, or null for a value
	private static OverrideSchema? Under(OverrideSchema schema, OverridePath path)
	{
		OverrideSchema? current = schema;
		foreach (var (name, _) in path.Steps)
			current = current?.Under(name);
		return current;
	}

	// Goes to the object holding the path's last step; returns what is missing on the way (dangling), or null
	private static string? Walk(JObject root, OverridePath path, OverrideSchema schema, bool create, out JObject? holder, out (string Name, string? Key) last)
	{
		holder = root;
		last = path.Steps[path.Steps.Count - 1];
		OverrideSchema? current = schema;
		for (var i = 0; i < path.Steps.Count - 1; i++)
		{
			var (name, key) = path.Steps[i];
			current = current?.Under(name);
			if (key is null)
			{
				if (holder.Get(name) is JObject next)
				{
					holder = next;
					continue;
				}
				if (!create)
					return $"'{name}'";
				var made = new JObject();
				holder.Set(name, made);
				holder = made;
				continue;
			}
			var item = holder.Get(name)?.Items.FirstOrDefault(v => current!.Key!(v) == Normal(key, current));
			if (item is not JObject found)
				return $"'{name}[{key}]'";
			holder = found;
		}
		return null;
	}

	private static string Normal(string key, OverrideSchema? items) =>
		items is not null && items.Has("upTo") ? OverrideSchema.Number(key) : key;

	private static bool RemoveItem(JObject holder, string name, string key, OverrideSchema? items)
	{
		if (holder.Get(name) is not JArray array || items is null)
			return false;
		return array.Values.RemoveAll(v => items.Key!(v) == Normal(key, items)) > 0;
	}

	private static void SetItem(JObject holder, string name, string key, JNode value, OverrideSchema items)
	{
		var array = holder.Get(name) as JArray;
		if (array is null)
			holder.Set(name, array = new JArray());
		var normal = Normal(key, items);
		// An item's key is part of it: a value given without it takes the path's
		if (value is JObject obj && items.Key!(obj) is null)
		{
			var keyName = items.Has("name") ? "name" : items.Has("piece") ? "piece" : items.Has("upTo") ? "upTo" : items.Has("op") ? "op" : null;
			if (keyName == "upTo")
				obj.Members.Insert(0, new JMember(keyName, new JNumber(normal)));
			else if (keyName is not null)
				obj.Members.Insert(0, new JMember(keyName, new JString(key)));
		}
		var at = array.Values.FindIndex(v => items.Key!(v) == normal);
		if (at >= 0)
			array.Values[at] = value;
		else
			array.Values.Add(value);
	}

	// Why a value can't go where a path says, or null when it can
	private static string? ShapeProblem(JNode value, OverrideSchema? under, bool item)
	{
		if (under is null)
			return value is JObject ? "a value goes here, not an object" : null;
		if (OverrideSchema.IsKeyed(under) && !item)
			return value is JArray ? null : "a list goes here";
		if (OverrideSchema.IsKeyed(under) && under.Key!(new JObject()) is null && under.Properties.Any())
			return value is JObject ? null : "an object goes here";
		return under.Properties.Any() && value is not JObject && !(item && value is JString) ? "an object goes here" : null;
	}
}
