namespace BinaryVibrance.Lunelle.Music;

/// <summary>
/// A choice the game tells the music, one of options named as data rather than an enum's values: what a music project of
/// files alone declares (<see cref="MusicFolder"/>). Option <c>i</c> is the value <c>i</c>, as an enum numbered from 0
/// would be, so a song plays the same against it as against a <see cref="Cue{T}"/> of that enum.
/// </summary>
public sealed class ChoiceCue : CueBase
{
	private readonly string[] _options;

	/// <summary>A choice named <paramref name="name"/> among <paramref name="options"/>, at <paramref name="initial"/> until told.</summary>
	/// <exception cref="ArgumentException">No options, an option named twice or blank, or an initial option that isn't one.</exception>
	public ChoiceCue(string name, IReadOnlyList<string> options, string? initial = null) : base(name)
	{
		ArgumentNullException.ThrowIfNull(options);
		if (options.Count == 0)
			throw new ArgumentException($"Cue '{name}' is a choice with no options.", nameof(options));
		_options = [.. options];
		for (var i = 0; i < _options.Length; i++)
		{
			if (string.IsNullOrWhiteSpace(_options[i]))
				throw new ArgumentException($"Cue '{name}' has a blank option.", nameof(options));
			if (Array.IndexOf(_options, _options[i]) != i)
				throw new ArgumentException($"Cue '{name}' has the option '{_options[i]}' twice.", nameof(options));
		}
		Initial = initial is null ? 0 : IndexOf(initial) is var at and >= 0
			? at
			: throw new ArgumentException($"Cue '{name}' starts at '{initial}', which isn't one of its options ({string.Join(", ", _options)}).", nameof(initial));
	}

	/// <summary>The option it holds before the game sets one, by its index.</summary>
	public int Initial { get; }

	internal override float InitialValue => Initial;

	/// <inheritdoc/>
	public override IReadOnlyList<string> OptionNames => _options;

	/// <summary>The index of the option named <paramref name="option"/> (ignoring case, as a condition reads it), or -1.</summary>
	public int IndexOf(string option)
	{
		for (var i = 0; i < _options.Length; i++)
			if (string.Equals(_options[i], option, StringComparison.OrdinalIgnoreCase))
				return i;
		return -1;
	}

	/// <summary>True while the cue is the option named <paramref name="option"/>.</summary>
	/// <exception cref="ArgumentException">It has no such option.</exception>
	public Condition Is(string option) => IndexOf(option) is var at and >= 0
		? new IsCondition(this, at)
		: throw new ArgumentException($"Cue '{Name}' has no option '{option}' ({string.Join(", ", _options)}).", nameof(option));
}
