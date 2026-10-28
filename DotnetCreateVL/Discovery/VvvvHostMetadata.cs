using System.Text.Json;
using System.Xml.Linq;
using DotnetCreateVL.Model;

namespace DotnetCreateVL.Discovery;

/// <summary>Verified facts about a vvvv installation's runtime, read from files it ships, never from its folder name.</summary>
internal sealed record VvvvHostMetadata(string? Tfm, string? VlVersion)
{
    public static VvvvHostMetadata Read(VvvvInstallation install) => new(ReadTfm(install.Path), ReadVlVersion(install.Path));

    private static string? ReadTfm(string root)
    {
        var file = Path.Combine(root, "vvvv.runtimeconfig.json");
        if (!File.Exists(file)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            return doc.RootElement.TryGetProperty("runtimeOptions", out var o) && o.TryGetProperty("tfm", out var tfm) && tfm.ValueKind == JsonValueKind.String
                ? tfm.GetString()
                : null;
        }
        catch (JsonException) { return null; }
    }

    private static string? ReadVlVersion(string root)
    {
        var file = Path.Combine(root, "packs", "VL.Core", "VL.Core.nuspec");
        if (!File.Exists(file)) return null;
        try
        {
            var version = XDocument.Load(file).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata")?
                .Elements().FirstOrDefault(e => e.Name.LocalName == "version")?.Value.Trim();
            return string.IsNullOrEmpty(version) ? null : version;
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException) { return null; }
    }

    /// <summary>Major .NET version implied by a TFM such as <c>net8.0</c> or <c>net8.0-windows</c>.</summary>
    public static int? MajorOf(string? tfm)
    {
        if (tfm is null || !tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase)) return null;
        var rest = tfm[3..];
        var end = rest.IndexOfAny(['.', '-']);
        return int.TryParse(end < 0 ? rest : rest[..end], out var major) ? major : null;
    }
}
