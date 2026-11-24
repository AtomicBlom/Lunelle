namespace BinaryVibrance.Lunelle.Music;

/// <summary>What a <see cref="Chord"/> adds over its triad.</summary>
public enum ChordExtension
{
	/// <summary>The triad alone.</summary>
	None,
	/// <summary>The major sixth: <c>6</c>.</summary>
	Sixth,
	/// <summary>The sixth and the ninth, with no seventh: <c>6/9</c>.</summary>
	SixNine,
	/// <summary>The ninth, with no seventh: <c>add9</c>.</summary>
	Add9,
	/// <summary>The seventh: <c>7</c>.</summary>
	Seventh,
	/// <summary>The seventh and the ninth: <c>9</c>.</summary>
	Ninth,
	/// <summary>The seventh, ninth and eleventh: <c>11</c>.</summary>
	Eleventh,
	/// <summary>The seventh, ninth and thirteenth, the eleventh left out as players leave it: <c>13</c>.</summary>
	Thirteenth,
}
