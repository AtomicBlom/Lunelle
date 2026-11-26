using BinaryVibrance.Lunelle.Chip;
using BinaryVibrance.Lunelle.Format;

namespace BinaryVibrance.Lunelle.Music;

/// <summary>How a song export is written: its sample rate, and its samples as 24-bit integers or 32-bit floats.</summary>
public sealed record ExportOptions
{
	/// <summary>The sample rate: 48 kHz, or 44.1 kHz on request.</summary>
	public int SampleRate { get; init; } = 48_000;

	/// <summary>32-bit float samples rather than 24-bit integers.</summary>
	public bool Float { get; init; }
}

/// <summary>What a song export rendered: how long, and where its loop is in the file, in frames.</summary>
public sealed record ExportResult(long Frames, int SampleRate, long? LoopStart, long? LoopEnd);

/// <summary>
/// Renders a song's export (docs/spec/exports.md): the song played offline, from where the export starts, with its cues
/// and macros changed at exact frames as its events say, to its end, its fade and its tail; written as a WAV with its
/// loop points and its tags. The same export of the same project renders the same samples every time, on one build of
/// Lunelle, at one sample rate, on one kind of processor (§6). The studio, a player, a test and a command line all
/// render through this.
/// </summary>
public static class SongExport
{
	private const int RampFrames = 64;
	private const int TailChunk = 4_800;
	private const double MaxTailSeconds = 10.0;
	private const float SilentPeak = 3.1623e-5f;   // -90 dBFS

	/// <summary>The file name an export is written as: <c>song--export.wav</c>, each <c>/</c> in an id a <c>-</c>.</summary>
	public static string FileName(string song, string export) => $"{song.Replace('/', '-')}--{export}.wav";

	/// <summary>
	/// Renders song <paramref name="song"/>'s export <paramref name="export"/> from <paramref name="folder"/> and writes it
	/// to <paramref name="output"/> as a WAV.
	/// </summary>
	/// <exception cref="ArgumentException">There is no such song, or no such export of it.</exception>
	/// <exception cref="FormatException">An event's position is never reached, or the loop's points don't fall in the file.</exception>
	public static ExportResult Render(MusicFolder folder, string song, string export, Stream output, ExportOptions? options = null, ExportTags? tags = null)
	{
		var samples = Render(folder, song, export, options, out var result, out var spec);
		var title = spec.Title ?? folder.Project.Root.Entry(EntryKind.Song, song)?.Json["title"]?.Str ?? song;
		ExportWav.Write(output, samples, (options ?? new()).SampleRate, (options ?? new()).Float, result.LoopStart, result.LoopEnd, new ExportTags
		{
			Title = tags?.Title ?? title,
			Artist = tags?.Artist ?? folder.Project.Root.Manifest.Author,
			Comment = tags?.Comment ?? spec.About,
			Album = tags?.Album,
			Track = tags?.Track,
		});
		return result;
	}

	/// <summary>
	/// Renders the project's album (its manifest's <c>"album"</c>, exports.md §8): each track in order into
	/// <paramref name="directory"/>, numbered, named for its export and tagged with the album's title. Returns the files written.
	/// </summary>
	/// <exception cref="InvalidOperationException">The project has no album.</exception>
	public static IReadOnlyList<string> RenderAlbum(MusicFolder folder, string directory, ExportOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(folder);
		var manifest = folder.Project.Root.Manifest;
		if (manifest.AlbumTracks.Count == 0)
			throw new InvalidOperationException("The project's manifest has no \"album\" of tracks.");
		Directory.CreateDirectory(directory);
		var written = new List<string>();
		for (var i = 0; i < manifest.AlbumTracks.Count; i++)
		{
			var track = manifest.AlbumTracks[i].Track;
			var cut = track.LastIndexOf('/');
			var (song, export) = (track[..cut], track[(cut + 1)..]);
			var path = Path.Combine(directory, $"{i + 1:00} {FileName(song, export)}");
			using (var file = File.Create(path))
				Render(folder, song, export, file, options, new ExportTags { Album = manifest.AlbumTitle ?? manifest.Title ?? manifest.Name, Track = i + 1 });
			written.Add(path);
		}
		return written;
	}

	/// <summary>The export rendered, as interleaved stereo samples, without writing a file.</summary>
	public static float[] Render(MusicFolder folder, string song, string export, ExportOptions? options, out ExportResult result) =>
		Render(folder, song, export, options, out result, out _);

	private static float[] Render(MusicFolder folder, string song, string export, ExportOptions? options, out ExportResult result, out ExportSpec exported)
	{
		ArgumentNullException.ThrowIfNull(folder);
		options ??= new ExportOptions();
		var resolved = folder.Resolved(song) ?? throw new ArgumentException($"There is no song '{song}'.", nameof(song));
		var spec = exported = resolved.Exports.Find(e => e.Name == export)
			?? throw new ArgumentException($"Song '{song}' has no export '{export}'{(resolved.Json["exports"] is null ? "" : " that is free of problems (see the folder's problems)")}.", nameof(export));
		var built = folder.Songs.Single(s => s.Name == song).Build();
		var cues = folder.CuesOf(song).ToDictionary(c => c.Import.Reference, c => c, StringComparer.Ordinal);
		var rate = options.SampleRate;

		using var session = new OfflineSession(rate);
		var player = new MusicPlayer(session.Engine, session.Engine.Master);
		// Where each cue starts: the export's, else the song's preview, else its own
		foreach (var (reference, (import, cue)) in cues)
		{
			var given = spec.Cues.FirstOrDefault(c => c.Cue == reference).Value;
			player.Set(cue, given is null ? (float)import.PreviewValue : CueValue(import.Definition, given));
		}
		var scale = spec.Scale is { } scaleText ? SongFile.ParseScaleText(scaleText) ?? throw new FormatException($"\"{scaleText}\" isn't a scale.") : null;
		var prepared = player.Prepare(built, scale, spec.Seed);
		var sections = built.Sections;
		player.Play(prepared, fadeSeconds: 0f);
		// Steered between blocks on this thread, which is the one an offline session renders on
		var source = player.Source!;
		if (spec.Start is { } start)
			source.StartAt(sections.Single(s => s.Name == start).Index);

		var meter = built.Meter;
		var ticksPerFrame = built.Tempo * Ticks.PerBeat / (60.0 * rate);
		var ticksPerBeat = Ticks.PerBeat * 4.0 / meter.Unit;
		long FrameOf(double ticks) => (long)Math.Ceiling(ticks / ticksPerFrame - 1e-9);
		long BarFrame(ExportPosition p) => FrameOf((p.Bar - 1) * (double)meter.TicksPerBar + (p.Beat - 1) * ticksPerBeat);
		long Frames(ExportDuration d) => d.Unit switch
		{
			DurationUnit.Bars => (long)Math.Round(d.Amount * meter.TicksPerBar / ticksPerFrame),
			DurationUnit.Beats => (long)Math.Round(d.Amount * ticksPerBeat / ticksPerFrame),
			_ => (long)Math.Round(d.Amount * rate),
		};

		var pending = spec.Events.Select((e, i) => (Event: e, Order: i)).ToList();
		var ramps = new List<Ramp>();
		var times = new Dictionary<string, int>(StringComparer.Ordinal);
		var current = source.CurrentSection;
		times[sections[current].Name] = 1;
		long? endFrame = null, loopStart = null, loopEnd = null;

		bool At(ExportPosition p, long frame, string? ended, int endedTime, string? started, int startedTime) =>
			p.Section is null
				? BarFrame(p) == frame
				: p.IsEnd ? p.Section == ended && p.Time == endedTime : p.Section == started && p.Time == startedTime;

		void Apply(ExportEvent e, long frame)
		{
			if (e.Jump is { } jump)
			{
				player.Jump(sections.Single(s => s.Name == jump));
				return;
			}
			if (e.Cue is { } reference)
			{
				var (import, cue) = cues[reference];
				var to = CueValue(import.Definition, e.To!);
				ramps.RemoveAll(r => r.Cue == cue);
				if (e.Over is { } over && Frames(over) > 0)
					ramps.Add(new Ramp(cue, -1, -1, player.Get(cue), to, frame, frame + Frames(over)));
				else
					player.Set(cue, to);
				return;
			}
			var track = built.Tracks.Single(t => t.Name == e.Track).Index;
			var macro = e.Macro!.Num is { } number ? int.Parse(number, System.Globalization.CultureInfo.InvariantCulture) : folder.Macros[e.Macro.Str!].Id;
			ramps.RemoveAll(r => r.Track == track && r.Macro == macro);
			if (e.To is not JNumber value)
			{
				source.Hold(track, macro, float.NaN);
				return;
			}
			var from = prepared.State.HeldMacros[track * Macro.Count + macro] is var held && !float.IsNaN(held) ? held : prepared.State.Instruments[track].Macros[macro];
			if (e.Over is { } overMacro && Frames(overMacro) > 0)
				ramps.Add(new Ramp(null, track, macro, from, (float)value.Value, frame, frame + Frames(overMacro)));
			else
				source.Hold(track, macro, (float)value.Value);
		}

		// What happens at a frame: positions reached, a section ended or started there
		void Reach(long frame, string? ended, int endedTime, string? started, int startedTime)
		{
			if (endFrame is null && At(spec.End, frame, ended, endedTime, started, startedTime))
				endFrame = frame;
			if (spec.Loop is { } loop)
			{
				if (loopStart is null && At(loop.From, frame, ended, endedTime, started, startedTime))
					loopStart = frame;
				if (loopEnd is null && At(loop.To, frame, ended, endedTime, started, startedTime))
					loopEnd = frame;
			}
			if (endFrame == frame)
				return;
			foreach (var (e, _) in pending.Where(p => At(p.Event.At, frame, ended, endedTime, started, startedTime)).OrderBy(p => p.Order).ToList())
			{
				Apply(e, frame);
				pending.RemoveAll(p => p.Event == e);
			}
		}

		long frameNow = 0;
		Reach(0, null, 0, sections[current].Name, 1);
		while (endFrame is null)
		{
			// The next frame anything happens: a bar position, a change of section, a ramp's next step
			var toChange = source.FramesToChange();
			var next = frameNow + Math.Max(1, toChange);
			foreach (var (e, _) in pending)
				if (e.At.Section is null && BarFrame(e.At) > frameNow)
					next = Math.Min(next, BarFrame(e.At));
			foreach (var p in new[] { spec.End, spec.Loop?.From, spec.Loop?.To })
				if (p is { Section: null } && BarFrame(p) > frameNow)
					next = Math.Min(next, BarFrame(p));
			if (ramps.Count > 0)
				next = Math.Min(next, frameNow + RampFrames);
			if (toChange == 0)
			{
				// A section changes as the next block starts: what ends and what starts are known now
				var ended = sections[source.CurrentSection].Name;
				var endedTime = times[ended];
				var starting = sections[source.PeekNext()].Name;
				var startingTime = (starting == ended && source.PeekNext() == source.CurrentSection ? times[ended] : times.GetValueOrDefault(starting)) + 1;
				Reach(frameNow, ended, endedTime, starting, startingTime);
				if (endFrame is not null)
					break;
				next = frameNow + 1;
				foreach (var (e, _) in pending)
					if (e.At.Section is null && BarFrame(e.At) > frameNow)
						next = Math.Min(next, BarFrame(e.At));
				session.Render((int)(next - frameNow));
				frameNow = next;
				var now = sections[source.CurrentSection].Name;
				times[now] = times.GetValueOrDefault(now) + 1;
				Step(ramps, player, source, frameNow);
				continue;
			}
			session.Render((int)Math.Min(int.MaxValue, next - frameNow));
			frameNow = next;
			Step(ramps, player, source, frameNow);
			Reach(frameNow, null, 0, null, 0);
			if (frameNow > 3600L * rate)
				throw new FormatException($"The export '{song}/{export}' never reaches its end ({spec.End}) within an hour.");
		}

		// The end: a fade over the music, then every note let go and the tail rendered
		var fadeFrames = (long)Math.Round(spec.FadeSeconds * rate);
		if (fadeFrames > 0)
		{
			for (var left = fadeFrames; left > 0;)
			{
				var n = (int)Math.Min(left, 4096);
				session.Render(n);
				left -= n;
			}
		}
		source.Silence();
		// After a fade the music is silent already: no tail unless one is asked for
		var tailMax = (long)Math.Round((spec.TailSeconds ?? (fadeFrames > 0 ? 0 : MaxTailSeconds)) * rate);
		for (long done = 0; done < tailMax;)
		{
			var n = (int)Math.Min(spec.TailSeconds is null ? TailChunk : 4096, tailMax - done);
			session.Render(n);
			done += n;
			if (spec.TailSeconds is null && Peak(session.Samples[^(n * 2)..]) < SilentPeak)
				break;
		}

		var unreached = pending.Select(p => p.Event.At.Text).ToList();
		if (unreached.Count > 0)
			throw new FormatException($"The export '{song}/{export}' never reaches {string.Join(", ", unreached.Select(u => $"\"{u}\""))}: its events there don't happen.");
		if (spec.Loop is { } loopPoints && (loopStart is null || loopEnd is null || loopEnd <= loopStart))
			throw new FormatException($"The export '{song}/{export}' loops from \"{loopPoints.From}\" to \"{loopPoints.To}\", which don't both come, in that order, before it ends.");

		var samples = session.Samples.ToArray();
		// The fade, from the end's frame over its length: silence after
		var end = endFrame!.Value;
		if (fadeFrames > 0)
			for (var f = end; f < samples.Length / 2; f++)
			{
				var gain = Math.Max(0f, 1f - (float)(f - end) / fadeFrames);
				samples[f * 2] *= gain;
				samples[f * 2 + 1] *= gain;
			}
		result = new ExportResult(samples.Length / 2, rate, loopStart, loopEnd);
		return samples;
	}

	private static float CueValue(CueDefinition cue, JNode value) =>
		cue.IsChoice ? cue.OptionIndex(value.Str ?? "") : (float)((JNumber)value).Value;

	// Each ramp moved to where it is at this frame; one at its end set there and done
	private static void Step(List<Ramp> ramps, MusicPlayer player, MusicSource source, long frame)
	{
		for (var i = ramps.Count - 1; i >= 0; i--)
		{
			var ramp = ramps[i];
			var through = Math.Clamp((double)(frame - ramp.Start) / (ramp.End - ramp.Start), 0.0, 1.0);
			var value = (float)(ramp.From + (ramp.To - ramp.From) * through);
			if (ramp.Cue is { } cue)
				player.Set(cue, value);
			else
				source.Hold(ramp.Track, ramp.Macro, value);
			if (frame >= ramp.End)
				ramps.RemoveAt(i);
		}
	}

	private static float Peak(ReadOnlySpan<float> samples)
	{
		var peak = 0f;
		foreach (var s in samples)
			peak = Math.Max(peak, Math.Abs(s));
		return peak;
	}

	private sealed record Ramp(CueBase? Cue, int Track, int Macro, float From, float To, long Start, long End);
}
