using System.Reflection;

namespace BinaryVibrance.Lunelle.Format;

/// <summary>
/// Lunelle's own sounds as a library (project.md §4.1): the project embedded in this assembly, provided by name to any
/// project that lists <c>"lunelle": { "version": "1" }</c>. Its sounds never change within a major version (§4.2).
/// </summary>
public static class BuiltInLibrary
{
	/// <summary>The name a project lists it by.</summary>
	public const string Name = "lunelle";

	private static readonly Lazy<IProjectSource> _source = new(() =>
	{
		var assembly = typeof(BuiltInLibrary).Assembly;
		// %(RecursiveDir) names a resource with the build machine's separator
		var resources = assembly.GetManifestResourceNames()
			.Where(r => r.StartsWith("library/", StringComparison.Ordinal) || r.StartsWith("library\\", StringComparison.Ordinal))
			.ToDictionary(r => r["library/".Length..].Replace('\\', '/'), r => r, StringComparer.Ordinal);
		return new MemorySource(resources.Keys, path => Read(assembly, resources[path]), "Lunelle's library");
	});

	/// <summary>Its files.</summary>
	public static IProjectSource Source => _source.Value;

	/// <summary>
	/// The libraries a host provides by name (<see cref="ProjectLoader.Load"/>'s <c>host</c>): this one as
	/// <see cref="Name"/>, and nothing else.
	/// </summary>
	public static IProjectSource? Host(string name) => name == Name ? Source : null;

	private static byte[] Read(Assembly assembly, string resource)
	{
		using var stream = assembly.GetManifestResourceStream(resource) ?? throw new KeyNotFoundException(resource);
		using var bytes = new MemoryStream();
		stream.CopyTo(bytes);
		return bytes.ToArray();
	}
}
