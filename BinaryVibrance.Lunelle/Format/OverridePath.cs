#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>
/// A path in an override (overrides.md §3): property names joined by <c>.</c>, an item of a keyed array named by its
/// key in brackets: <c>operators[tine].modulatedBy[strike].index</c>, <c>pieces[kick].layers[0.35].takes</c>.
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class OverridePath
{
	private OverridePath(string text, IReadOnlyList<(string Name, string? Key)> steps)
	{
		Text = text;
		Steps = steps;
	}

	/// <summary>The path as written.</summary>
	public string Text { get; }

	/// <summary>Each step: a property, and the key of the item in it when the property is a keyed array.</summary>
	public IReadOnlyList<(string Name, string? Key)> Steps { get; }

	/// <summary>Reads <paramref name="text"/>, or says why it isn't a path.</summary>
	public static OverridePath Parse(string text, out string? problem)
	{
		problem = null;
		var steps = new List<(string, string?)>();
		var at = 0;
		while (at < text.Length)
		{
			var start = at;
			while (at < text.Length && text[at] != '.' && text[at] != '[')
				at++;
			var name = text.Substring(start, at - start);
			if (name.Length == 0)
			{
				problem = $"\"{text}\" isn't a path: a property's name is missing at {start + 1}";
				return new OverridePath(text, steps);
			}
			string? key = null;
			if (at < text.Length && text[at] == '[')
			{
				var close = text.IndexOf(']', at);
				if (close < 0)
				{
					problem = $"\"{text}\" isn't a path: a '[' isn't closed";
					return new OverridePath(text, steps);
				}
				key = text.Substring(at + 1, close - at - 1);
				if (key.Length == 0)
				{
					problem = $"\"{text}\" isn't a path: the brackets after '{name}' hold no key";
					return new OverridePath(text, steps);
				}
				at = close + 1;
			}
			steps.Add((name, key));
			if (at < text.Length)
			{
				if (text[at] != '.')
				{
					problem = $"\"{text}\" isn't a path: '{text[at]}' follows a step where '.' should";
					return new OverridePath(text, steps);
				}
				at++;
				if (at == text.Length)
				{
					problem = $"\"{text}\" isn't a path: it ends in '.'";
					return new OverridePath(text, steps);
				}
			}
		}
		if (steps.Count == 0)
			problem = "an empty path names nothing";
		return new OverridePath(text, steps);
	}

	/// <summary>The path as written.</summary>
	public override string ToString() => Text;
}
