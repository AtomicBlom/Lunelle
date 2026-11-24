using BinaryVibrance.Lunelle.Chip;

namespace BinaryVibrance.Lunelle.Music;

/// <summary>
/// A piece of music as code: its tempo, meter and scale, its tracks (patches and kits, in groups of effects), its
/// sections and the parts played in each, and where each section goes as it ends (<see cref="Section.Exits"/>). Build one
/// in a method that makes it anew each time (so hot reload rebuilds it), then hand it to a <see cref="MusicPlayer"/>.
/// <code>
/// var song = new Song("groove") { Tempo = 128, Scale = Scale.Minor(Note.E) };
/// var drums = song.Track("drums", kit);
/// var bass = song.Track("bass", bassPatch).Octave(2);
/// var verse = song.Section("verse", 4, "i | VI | III | VII");
/// verse.Play(drums, Beat.Eighths((Kit.Kick, "x... x..."), (Kit.Snare, "..x. ..x.")));
/// verse.Play(bass, Line.Eighths("1 1 . 1 5, 1 . 1").OverChords());
/// var chorus = song.Section("chorus", 4, "VI | VII | i | i");
/// chorus.Play(drums, Beat.Eighths((Kit.Kick, "x.x. x.x."), (Kit.Snare, "..x. ..x.")));
/// verse.Exit(verse, Pass.Below(2));
/// verse.Then(chorus);
/// chorus.Then(verse);
/// </code>
/// </summary>
/// <param name="name">Its name.</param>
public sealed class Song(string name)
{
	private readonly List<SongGroup> _groups = [];
	private readonly List<SongTrack> _tracks = [];
	private readonly List<Section> _sections = [];
	private readonly List<Cue> _rolls = [];

	// As many as a chip has instruments
	internal const int MaxTracks = 64;

	/// <summary>Its name.</summary>
	public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

	// Fixed once a player has compiled it: what plays was made from it, and a change after would play nowhere, or past the
	// end of what was compiled. A rebuild makes a new song
	internal bool IsCompiled { get; private set; }

	internal void MarkCompiled() => IsCompiled = true;

	// Every change to the song, its sections, tracks, macros and placements asks this first
	internal void CheckOpen()
	{
		if (IsCompiled)
			throw new InvalidOperationException($"Song '{Name}' has been played, and is fixed: build a new one to change it.");
	}

	/// <summary>Its tempo, in beats (quarter notes) a minute. Default 120.</summary>
	public float Tempo
	{
		get;
		set
		{
			CheckOpen();
			field = value;
		}
	} = 120f;

	/// <summary>Its time signature. Default 4/4. Set it before placing any part: parts are checked against it.</summary>
	/// <exception cref="InvalidOperationException">A part is already placed, checked against the meter it replaces.</exception>
	public Meter Meter
	{
		get;
		set
		{
			CheckOpen();
			if (_sections.Exists(s => s.Placements.Count > 0))
				throw new InvalidOperationException(
					$"Song '{Name}': its parts were placed against {field}; set the meter before placing any part.");
			field = value;
		}
	} = Meter.FourFour;

	/// <summary>The scale it is written in; a player can play it in another.</summary>
	public Scale Scale
	{
		get;
		set
		{
			CheckOpen();
			field = value;
		}
	} = Scale.Major(Note.C);

	/// <summary>How many notes sound at once. Default 32.</summary>
	public int Voices
	{
		get;
		set
		{
			CheckOpen();
			field = value;
		}
	} = 32;

	/// <summary>The space send's effects: <see cref="ChipOptions"/>'s default reverb unless set.</summary>
	public Func<AudioEffect>[] SpaceEffects
	{
		get;
		set
		{
			CheckOpen();
			field = value;
		}
	} = new ChipOptions().SpaceEffects;

	/// <summary>The echo send's effects: <see cref="ChipOptions"/>'s default delay unless set.</summary>
	public Func<AudioEffect>[] EchoEffects
	{
		get;
		set
		{
			CheckOpen();
			field = value;
		}
	} = new ChipOptions().EchoEffects;

	/// <summary>Effects over the whole song. Default none.</summary>
	public Func<AudioEffect>[] MasterEffects
	{
		get;
		set
		{
			CheckOpen();
			field = value;
		}
	} = [];

	/// <summary>Its tracks, in the order made.</summary>
	public IReadOnlyList<SongTrack> Tracks => _tracks;

	/// <summary>Its sections, in the order made.</summary>
	public IReadOnlyList<Section> Sections => _sections;

	/// <summary>Its groups, in the order made.</summary>
	public IReadOnlyList<SongGroup> Groups => _groups;

	/// <summary>Its rolls (<see cref="Roll"/>), in the order made.</summary>
	public IReadOnlyList<Cue> Rolls => _rolls;

	/// <summary>Whether <paramref name="cue"/> is one of its rolls, drawn by the sequencer rather than told by the game.</summary>
	public bool IsRoll(CueBase cue) => cue is Cue level && _rolls.Contains(level);

	/// <summary>
	/// A roll: a level from 0 to 1 the song draws for itself at the start of every section, held until the next, so a part
	/// can be picked afresh each time round: <c>fill.Below(0.33f)</c> for one fill, <c>fill.Above(0.33f) &amp; fill.Below(0.66f)</c>
	/// for the next. Unlike a cue's, its comparisons are exact, with no margin: <c>Above(x)</c> holds at x or more and
	/// <c>Below(x)</c> below x, so slices that meet never both hold, nor both miss. What it draws comes from the seed the
	/// song was prepared with (<see cref="MusicPlayer.Prepare"/>), the roll's name and how many sections have started.
	/// </summary>
	/// <exception cref="ArgumentException">The song already has a roll of that name.</exception>
	public Cue Roll(string name)
	{
		CheckOpen();
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		if (_rolls.Exists(r => r.Name == name))
			throw new ArgumentException($"Song '{Name}' already has a roll named '{name}'.", nameof(name));
		var roll = new Cue(name);
		_rolls.Add(roll);
		return roll;
	}

	/// <summary>
	/// Every cue a condition (a part's or an exit's), a level or a macro names, each once, in the order first named: the
	/// cues the game tells it, so not its <see cref="Rolls"/>, which it draws for itself.
	/// </summary>
	public IReadOnlyList<CueBase> Cues
	{
		get
		{
			var cues = new List<CueBase>();
			void Add(CueBase cue)
			{
				if (!cues.Contains(cue) && !IsRoll(cue))
					cues.Add(cue);
			}
			foreach (var track in _tracks)
				foreach (var macro in track.Macros)
					if (macro.Cue is { } cue)
						Add(cue);
			foreach (var section in _sections)
				foreach (var placement in section.Placements)
				{
					if (placement.Condition is { } condition)
						foreach (var cue in condition.Cues)
							Add(cue);
					if (placement.LevelCue is { } level)
						Add(level);
				}
			foreach (var section in _sections)
				foreach (var exit in section.Exits)
					if (exit.When is { } when)
						foreach (var cue in when.Cues)
							Add(cue);
			return cues;
		}
	}


	/// <summary>A group of tracks sharing <paramref name="effects"/> (<see cref="Sirrow.Effects.Fx"/> makes them).</summary>
	public SongGroup Group(string name, params Func<AudioEffect>[] effects)
	{
		CheckOpen();

		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(effects);
		var group = new SongGroup(name, _groups.Count + 1, effects);
		_groups.Add(group);
		return group;
	}

	/// <summary>A track that plays <paramref name="patch"/>: give it <see cref="Line"/>s and <see cref="Strum"/>s.</summary>
	public SongTrack Track(string name, Patch patch, SongGroup? group = null)
	{
		CheckOpen();

		ArgumentNullException.ThrowIfNull(patch);
		return AddTrack(name, patch, null, group);
	}

	/// <summary>A track that plays <paramref name="kit"/>: give it <see cref="Beat"/>s.</summary>
	public SongTrack Track(string name, DrumKit kit, SongGroup? group = null)
	{
		CheckOpen();

		ArgumentNullException.ThrowIfNull(kit);
		return AddTrack(name, null, kit, group);
	}

	private SongTrack AddTrack(string name, Patch? patch, DrumKit? kit, SongGroup? group)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		if (group is not null && !_groups.Contains(group))
			throw new ArgumentException($"Group '{group.Name}' belongs to another song.", nameof(group));
		if (_tracks.Count == MaxTracks)
			throw new InvalidOperationException($"A song has at most {MaxTracks} tracks.");
		var track = new SongTrack(this, name, _tracks.Count, patch, kit, group);
		_tracks.Add(track);
		return track;
	}

	/// <summary>
	/// A section <paramref name="bars"/> long over <paramref name="chords"/>: bars separated by <c>|</c>, each one chord or
	/// two or four sharing the bar (<see cref="Chord.Parse"/>), repeated to fill the section if fewer.
	/// </summary>
	/// <exception cref="NotationException">The chords have a mistake.</exception>
	public Section Section(string name, int bars, string chords = "I")
	{
		CheckOpen();

		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bars);
		var section = new Section(this, name, _sections.Count, bars, new SectionChords(chords));
		_sections.Add(section);
		return section;
	}

	/// <inheritdoc/>
	public override string ToString() => Name;
}
