using System.Buffers.Binary;
using System.Text;

namespace BinaryVibrance.Lunelle.Music;

/// <summary>The tags a song export's file carries (exports.md §7), each left out where null.</summary>
public sealed record ExportTags
{
	/// <summary>The track's title (<c>INAM</c>).</summary>
	public string? Title { get; init; }

	/// <summary>Who made it (<c>IART</c>).</summary>
	public string? Artist { get; init; }

	/// <summary>What it is (<c>ICMT</c>).</summary>
	public string? Comment { get; init; }

	/// <summary>The album it is on (<c>IPRD</c>).</summary>
	public string? Album { get; init; }

	/// <summary>Its number on the album (<c>ITRK</c>).</summary>
	public int? Track { get; init; }
}

/// <summary>
/// Writes a song export as a WAV file: stereo, 24-bit integer or 32-bit float samples, its loop points in a
/// <c>smpl</c> chunk (as a game streaming it loops), and its tags in a <c>LIST</c>/<c>INFO</c> chunk.
/// </summary>
public static class ExportWav
{
	/// <summary>Writes interleaved stereo <paramref name="samples"/> at <paramref name="rate"/> to <paramref name="output"/>.</summary>
	public static void Write(Stream output, ReadOnlySpan<float> samples, int rate, bool asFloat, long? loopStart, long? loopEnd, ExportTags? tags = null)
	{
		ArgumentNullException.ThrowIfNull(output);
		var bytesPerSample = asFloat ? 4 : 3;
		var data = new byte[samples.Length * bytesPerSample];
		for (var i = 0; i < samples.Length; i++)
		{
			if (asFloat)
				BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i * 4), samples[i]);
			else
			{
				var value = (int)Math.Round(Math.Clamp(samples[i], -1f, 1f) * 8_388_607f);
				data[i * 3] = (byte)value;
				data[i * 3 + 1] = (byte)(value >> 8);
				data[i * 3 + 2] = (byte)(value >> 16);
			}
		}

		var chunks = new List<(string Id, byte[] Body)>();
		var format = new byte[16];
		BinaryPrimitives.WriteUInt16LittleEndian(format, (ushort)(asFloat ? 3 : 1));
		BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(2), 2);
		BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(4), (uint)rate);
		BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(8), (uint)(rate * 2 * bytesPerSample));
		BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(12), (ushort)(2 * bytesPerSample));
		BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(14), (ushort)(bytesPerSample * 8));
		chunks.Add(("fmt ", format));
		chunks.Add(("data", data));
		if (loopStart is { } start && loopEnd is { } end)
			chunks.Add(("smpl", Smpl(rate, start, end)));
		if (tags is not null && Info(tags) is { Length: > 4 } info)
			chunks.Add(("LIST", info));

		var size = 4 + chunks.Sum(c => 8 + c.Body.Length + (c.Body.Length & 1));
		Span<byte> header = stackalloc byte[12];
		Encoding.ASCII.GetBytes("RIFF", header);
		BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)size);
		Encoding.ASCII.GetBytes("WAVE", header[8..]);
		output.Write(header);
		Span<byte> chunkHeader = stackalloc byte[8];
		foreach (var (id, body) in chunks)
		{
			Encoding.ASCII.GetBytes(id, chunkHeader);
			BinaryPrimitives.WriteUInt32LittleEndian(chunkHeader[4..], (uint)body.Length);
			output.Write(chunkHeader);
			output.Write(body);
			if ((body.Length & 1) == 1)
				output.WriteByte(0);
		}
	}

	/// <summary>The loop points a WAV's <c>smpl</c> chunk holds, as frames: its first loop's start and end; null where it has none.</summary>
	public static (long Start, long End)? ReadLoop(ReadOnlySpan<byte> wav)
	{
		for (var at = 12; at + 8 <= wav.Length;)
		{
			var id = Encoding.ASCII.GetString(wav.Slice(at, 4));
			var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(wav[(at + 4)..]);
			if (id == "smpl" && length >= 60)
			{
				var body = wav.Slice(at + 8, length);
				return (BinaryPrimitives.ReadUInt32LittleEndian(body[44..]), BinaryPrimitives.ReadUInt32LittleEndian(body[48..]) + 1);
			}
			at += 8 + length + (length & 1);
		}
		return null;
	}

	// A sampler chunk with one forward loop: its end is the last frame played, so one before the frame it goes back from
	private static byte[] Smpl(int rate, long start, long end)
	{
		var body = new byte[36 + 24];
		BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), (uint)(1_000_000_000L / rate));   // sample period, ns
		BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(12), 60);                               // MIDI unity note
		BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(28), 1);                                // one loop
		BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(36 + 4), 0);                            // forward
		BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(36 + 8), (uint)start);
		BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(36 + 12), (uint)(end - 1));
		return body;
	}

	private static byte[] Info(ExportTags tags)
	{
		using var body = new MemoryStream();
		body.Write("INFO"u8);
		void Tag(string id, string? value)
		{
			if (string.IsNullOrEmpty(value))
				return;
			var text = Encoding.UTF8.GetBytes(value + "\0");
			Span<byte> header = stackalloc byte[8];
			Encoding.ASCII.GetBytes(id, header);
			BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)text.Length);
			body.Write(header);
			body.Write(text);
			if ((text.Length & 1) == 1)
				body.WriteByte(0);
		}
		Tag("INAM", tags.Title);
		Tag("IART", tags.Artist);
		Tag("ICMT", tags.Comment);
		Tag("IPRD", tags.Album);
		Tag("ITRK", tags.Track?.ToString(System.Globalization.CultureInfo.InvariantCulture));
		return body.ToArray();
	}
}
