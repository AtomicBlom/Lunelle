#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>
/// The layer that makes <c>wanted</c> of <c>below</c> (overrides.md §4): what an editor saves for a derived entry, so it
/// stays derived and follows its base where it doesn't differ. A value that differs is a <c>set</c> of its path, one gone
/// a <c>remove</c>, a keyed item that is new an <c>insert</c> before the item after it. A path the layer set before stays
/// set even where it now equals the base's value (a <c>set</c> is pinned, §4); only a revert removes it. A keyed array
/// whose kept items have moved is set whole.
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
static class OverrideDiff
{
	private static readonly string[] Own = { "format", "base", "name", "title", "about", "set", "remove", "insert" };

	/// <summary>
	/// The derived file that makes <paramref name="wanted"/> of <paramref name="below"/>, kept from
	/// <paramref name="layer"/> (the file as it was): its format, base, title and about, and its pinned sets.
	/// </summary>
	public static JObject Layer(JObject below, JObject wanted, OverrideSchema schema, JObject layer)
	{
		var sets = new JObject();
		var removes = new JArray();
		var inserts = new JArray();
		Diff(below, wanted, schema, "", sets, removes, inserts);

		// What the layer set before and still holds, kept: a set is pinned
		if (layer["set"] is JObject before)
			foreach (var member in before.Members)
				if (sets[member.Key] is null && At(wanted, member.Key, schema) is { } now && !removes.Values.Any(r => r.Str == member.Key))
					sets.Set(member.Key, now.Clone());

		var result = new JObject();
		foreach (var key in new[] { "format", "base", "title", "about" })
			if (layer[key] is { } value)
				result.Set(key, value.Clone());
		if (wanted["about"] is { } about && !about.Same(below["about"]))
			result.Set("about", about.Clone());
		if (removes.Values.Count > 0)
			result.Set("remove", removes);
		if (inserts.Values.Count > 0)
			result.Set("insert", inserts);
		if (sets.Members.Count > 0)
			result.Set("set", sets);
		return result;
	}

	private static void Diff(JObject below, JObject wanted, OverrideSchema schema, string prefix, JObject sets, JArray removes, JArray inserts)
	{
		foreach (var name in schema.Properties)
		{
			if (prefix.Length == 0 && System.Array.IndexOf(Own, name) >= 0)
				continue;
			var path = prefix + name;
			var was = below[name];
			var now = wanted[name];
			if (now is null)
			{
				if (was is not null)
					removes.Values.Add(new JString(path));
				continue;
			}
			if (was is not null && was.Same(now))
				continue;
			var under = schema.Under(name);
			if (under is null || was is null || was.GetType() != now.GetType())
			{
				sets.Set(path, now.Clone());
				continue;
			}
			if (!OverrideSchema.IsKeyed(under))
			{
				Diff((JObject)was, (JObject)now, under, path + ".", sets, removes, inserts);
				continue;
			}
			Items(was, now, under, path, sets, removes, inserts);
		}
	}

	// A keyed array: kept items diffed by key, new ones inserted, gone ones removed; set whole if the kept ones moved
	private static void Items(JNode was, JNode now, OverrideSchema items, string path, JObject sets, JArray removes, JArray inserts)
	{
		var key = items.Key!;
		var before = was.Items.Select(i => key(i)).ToList();
		var after = now.Items.Select(i => key(i)).ToList();
		if (before.Contains(null) || after.Contains(null) || before.Distinct().Count() != before.Count || after.Distinct().Count() != after.Count)
		{
			sets.Set(path, now.Clone());
			return;
		}
		var kept = after.Where(before.Contains).ToList();
		if (!kept.SequenceEqual(before.Where(after.Contains)))
		{
			sets.Set(path, now.Clone());
			return;
		}
		foreach (var gone in before.Where(k => !after.Contains(k)))
			removes.Values.Add(new JString($"{path}[{gone}]"));
		for (var i = 0; i < after.Count; i++)
		{
			var item = now.Items[i];
			if (!before.Contains(after[i]))
			{
				var insert = new JObject().Set("into", new JString(path));
				var next = after.Skip(i + 1).FirstOrDefault(before.Contains);
				if (next is not null)
					insert.Set("before", new JString(next));
				insert.Set("item", item.Clone());
				inserts.Values.Add(insert);
				continue;
			}
			var old = was.Items[before.IndexOf(after[i])];
			if (old.Same(item))
				continue;
			if (old is JObject o && item is JObject n && items.Properties.Any())
				Diff(o, n, items, $"{path}[{after[i]}].", sets, removes, inserts);
			else
				sets.Set($"{path}[{after[i]}]", item.Clone());
		}
	}

	// The value at a path in a whole entry, or null where there is none
	private static JNode? At(JObject json, string text, OverrideSchema schema)
	{
		var path = OverridePath.Parse(text, out var problem);
		if (problem is not null)
			return null;
		JNode? node = json;
		OverrideSchema? current = schema;
		foreach (var (name, key) in path.Steps)
		{
			node = node?[name];
			current = current?.Under(name);
			if (key is not null)
				node = node?.Items.FirstOrDefault(i => current?.Key?.Invoke(i) == key);
		}
		return node;
	}
}
