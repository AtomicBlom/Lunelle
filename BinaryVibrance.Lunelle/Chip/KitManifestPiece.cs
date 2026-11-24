namespace BinaryVibrance.Lunelle.Chip;

internal sealed class KitManifestPiece
{
	public string Piece { get; set; } = "";
	public float GainDb { get; set; }
	public float Pan { get; set; }
	public int Choke { get; set; }
	public List<KitManifestLayer> Layers { get; set; } = [];
	// A patch played at note 60 in place of layers, by the name the loader's patches know it by
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public string? Patch { get; set; }
}
