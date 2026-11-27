// Shared with the music generator (netstandard2.0), which compiles this folder as its own internal copy: keep to what
// netstandard2.0 has (no ranges, no System.Text.Json), and depend on nothing outside this folder.
#if LUNELLE_GENERATOR
namespace BinaryVibrance.Lunelle.Music.Generator.Format;
#else
namespace BinaryVibrance.Lunelle.Format;
#endif

/// <summary>
/// A JSON value in a music file, as format 2's resolver reads, changes and writes it: objects keep their members in
/// order, numbers keep their text, and every value read from a file knows where in it it starts, so a problem can point
/// there. Values the resolver makes point where what they stand for was written.
/// </summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
abstract class JNode
{
	/// <summary>Where the value starts in its file's text; 0 for one made in code.</summary>
	public int Start { get; set; }

	/// <summary>How long the value is in its file's text; 0 for one made in code.</summary>
	public int Length { get; set; }

	/// <summary>The member called <paramref name="key"/> of an object, or null for none (or for anything but an object).</summary>
	public JNode? this[string key] => this is JObject o ? o.Get(key) : null;

	/// <summary>A string's value, or null for anything else.</summary>
	public string? Str => this is JString s ? s.Value : null;

	/// <summary>A number's text as written, or null for anything else.</summary>
	public string? Num => this is JNumber n ? n.Text : null;

	/// <summary>Whether this is <c>true</c>.</summary>
	public bool True => this is JBool { Value: true };

	/// <summary>Whether this is <c>null</c>.</summary>
	public bool IsNull => this is JNull;

	/// <summary>An array's items, or none for anything else.</summary>
	public IReadOnlyList<JNode> Items => this is JArray a ? a.Values : System.Array.Empty<JNode>();

	/// <summary>A deep copy, keeping where each value was.</summary>
	public abstract JNode Clone();

	/// <summary>Whether <paramref name="other"/> holds the same JSON: the same members in any order, the same items in order, numbers by value.</summary>
	public abstract bool Same(JNode? other);

	/// <summary>The value written as JSON, indented as the music files are (two spaces).</summary>
	public override string ToString() => JWriter.Write(this);

	/// <summary>Takes where <paramref name="from"/> was, and returns this.</summary>
	public T At<T>(JNode? from) where T : JNode
	{
		if (from is not null)
		{
			Start = from.Start;
			Length = from.Length;
		}
		return (T)this;
	}
}

/// <summary>A JSON object: members in the order written.</summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class JObject : JNode
{
	/// <summary>The members, in order.</summary>
	public List<JMember> Members { get; } = new();

	/// <summary>The value of member <paramref name="key"/>, or null.</summary>
	public JNode? Get(string key)
	{
		foreach (var member in Members)
			if (member.Key == key)
				return member.Value;
		return null;
	}

	/// <summary>The member <paramref name="key"/>, or null.</summary>
	public JMember? Member(string key)
	{
		foreach (var member in Members)
			if (member.Key == key)
				return member;
		return null;
	}

	/// <summary>Sets member <paramref name="key"/>, in its place if it is there, else at the end; returns this.</summary>
	public JObject Set(string key, JNode value)
	{
		foreach (var member in Members)
			if (member.Key == key)
			{
				member.Value = value;
				return this;
			}
		Members.Add(new JMember(key, value));
		return this;
	}

	/// <summary>Removes member <paramref name="key"/>; whether it was there.</summary>
	public bool Remove(string key) => Members.RemoveAll(m => m.Key == key) > 0;

	/// <inheritdoc/>
	public override JNode Clone()
	{
		var copy = new JObject().At<JObject>(this);
		foreach (var member in Members)
			copy.Members.Add(new JMember(member.Key, member.Value.Clone()) { KeyStart = member.KeyStart });
		return copy;
	}

	/// <inheritdoc/>
	public override bool Same(JNode? other)
	{
		if (other is not JObject o || o.Members.Count != Members.Count)
			return false;
		foreach (var member in Members)
			if (!member.Value.Same(o.Get(member.Key)))
				return false;
		return true;
	}
}

/// <summary>An object's member: its key, its value, and where the key was written.</summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class JMember
{
	/// <summary>A member.</summary>
	public JMember(string key, JNode value)
	{
		Key = key;
		Value = value;
	}

	/// <summary>The key.</summary>
	public string Key { get; }

	/// <summary>The value.</summary>
	public JNode Value { get; set; }

	/// <summary>Where the key starts in its file's text.</summary>
	public int KeyStart { get; set; }
}

/// <summary>A JSON array.</summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class JArray : JNode
{
	/// <summary>An array of <paramref name="values"/>.</summary>
	public JArray(IEnumerable<JNode>? values = null)
	{
		if (values is not null)
			Values.AddRange(values);
	}

	/// <summary>The items, in order.</summary>
	public List<JNode> Values { get; } = new();

	/// <inheritdoc/>
	public override JNode Clone() => new JArray(Values.Select(v => v.Clone())).At<JArray>(this);

	/// <inheritdoc/>
	public override bool Same(JNode? other)
	{
		if (other is not JArray a || a.Values.Count != Values.Count)
			return false;
		for (var i = 0; i < Values.Count; i++)
			if (!Values[i].Same(a.Values[i]))
				return false;
		return true;
	}
}

/// <summary>A JSON string.</summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class JString : JNode
{
	/// <summary>The string <paramref name="value"/>.</summary>
	public JString(string value) => Value = value;

	/// <summary>Its value.</summary>
	public string Value { get; }

	/// <inheritdoc/>
	public override JNode Clone() => new JString(Value).At<JString>(this);

	/// <inheritdoc/>
	public override bool Same(JNode? other) => other is JString s && s.Value == Value;
}

/// <summary>A JSON number, as its text.</summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class JNumber : JNode
{
	/// <summary>The number written <paramref name="text"/>.</summary>
	public JNumber(string text) => Text = text;

	/// <summary>The number <paramref name="value"/>, written the shortest way that reads back the same.</summary>
	public JNumber(double value) => Text = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

	/// <summary>Its text, as written.</summary>
	public string Text { get; }

	/// <summary>Its value.</summary>
	public double Value => double.Parse(Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);

	/// <inheritdoc/>
	public override JNode Clone() => new JNumber(Text).At<JNumber>(this);

	/// <inheritdoc/>
	public override bool Same(JNode? other) => other is JNumber n && n.Value.Equals(Value);
}

/// <summary>A JSON <c>true</c> or <c>false</c>.</summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class JBool : JNode
{
	/// <summary><paramref name="value"/>.</summary>
	public JBool(bool value) => Value = value;

	/// <summary>Its value.</summary>
	public bool Value { get; }

	/// <inheritdoc/>
	public override JNode Clone() => new JBool(Value).At<JBool>(this);

	/// <inheritdoc/>
	public override bool Same(JNode? other) => other is JBool b && b.Value == Value;
}

/// <summary>A JSON <c>null</c>.</summary>
#if LUNELLE_GENERATOR
internal
#else
public
#endif
sealed class JNull : JNode
{
	/// <inheritdoc/>
	public override JNode Clone() => new JNull().At<JNull>(this);

	/// <inheritdoc/>
	public override bool Same(JNode? other) => other is JNull;
}
