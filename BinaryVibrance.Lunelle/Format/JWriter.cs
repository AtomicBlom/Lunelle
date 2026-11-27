using System.Text;

#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>
/// Writes <see cref="JNode"/>s as the music files are written: two spaces a level, a member a line, and an array of
/// plain values (numbers, strings, true, false, null) on one line when it is short.
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
static class JWriter
{
	private const int ShortArray = 100;

	/// <summary><paramref name="node"/> as JSON, with no line break after it.</summary>
	public static string Write(JNode node)
	{
		var text = new StringBuilder();
		Value(text, node, 0);
		return text.ToString();
	}

	private static void Value(StringBuilder text, JNode node, int depth)
	{
		switch (node)
		{
			case JObject obj when obj.Members.Count == 0:
				text.Append("{}");
				break;
			case JObject obj:
				text.Append("{\n");
				for (var i = 0; i < obj.Members.Count; i++)
				{
					Indent(text, depth + 1);
					Quote(text, obj.Members[i].Key);
					text.Append(": ");
					Value(text, obj.Members[i].Value, depth + 1);
					text.Append(i + 1 < obj.Members.Count ? ",\n" : "\n");
				}
				Indent(text, depth);
				text.Append('}');
				break;
			case JArray array when array.Values.Count == 0:
				text.Append("[]");
				break;
			case JArray array when array.Values.All(v => v is not JObject and not JArray) && Inline(array) is { Length: <= ShortArray } line:
				text.Append(line);
				break;
			case JArray array:
				text.Append("[\n");
				for (var i = 0; i < array.Values.Count; i++)
				{
					Indent(text, depth + 1);
					Value(text, array.Values[i], depth + 1);
					text.Append(i + 1 < array.Values.Count ? ",\n" : "\n");
				}
				Indent(text, depth);
				text.Append(']');
				break;
			case JString s:
				Quote(text, s.Value);
				break;
			case JNumber n:
				text.Append(n.Text);
				break;
			case JBool b:
				text.Append(b.Value ? "true" : "false");
				break;
			default:
				text.Append("null");
				break;
		}
	}

	private static string Inline(JArray array)
	{
		var text = new StringBuilder("[");
		for (var i = 0; i < array.Values.Count; i++)
		{
			if (i > 0)
				text.Append(", ");
			Value(text, array.Values[i], 0);
		}
		return text.Append(']').ToString();
	}

	private static void Indent(StringBuilder text, int depth) => text.Append(' ', depth * 2);

	/// <summary>Writes <paramref name="value"/> as a JSON string.</summary>
	public static void Quote(StringBuilder text, string value)
	{
		text.Append('"');
		foreach (var c in value)
		{
			switch (c)
			{
				case '"': text.Append("\\\""); break;
				case '\\': text.Append("\\\\"); break;
				case '\n': text.Append("\\n"); break;
				case '\r': text.Append("\\r"); break;
				case '\t': text.Append("\\t"); break;
				case '\b': text.Append("\\b"); break;
				case '\f': text.Append("\\f"); break;
				default:
					if (c < ' ')
						text.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
					else
						text.Append(c);
					break;
			}
		}
		text.Append('"');
	}
}
