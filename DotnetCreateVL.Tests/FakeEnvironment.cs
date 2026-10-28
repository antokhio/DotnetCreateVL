using DotnetCreateVL.Discovery;
using DotnetCreateVL.Model;

namespace DotnetCreateVL.Tests;

/// <summary>Builds a fake discovered environment around a temporary vvvv-like directory.</summary>
internal sealed class FakeEnvironment : IDisposable
{
    public string Root { get; }
    public string VvvvDir { get; }
    public string WorkDir { get; }
    public VvvvInstallation Install { get; }

    public FakeEnvironment(bool withEditor = true, bool withCompiler = true, string? hostTfm = "net8.0", string? vlVersion = "2025.7.4")
    {
        Root = Path.Combine(Path.GetTempPath(), "dcvl-tests", Guid.NewGuid().ToString("N"));
        VvvvDir = Path.Combine(Root, "vvvv_gamma_9.9");
        WorkDir = Path.Combine(Root, "work");
        Directory.CreateDirectory(VvvvDir);
        Directory.CreateDirectory(WorkDir);
        if (withEditor) File.WriteAllText(Path.Combine(VvvvDir, "vvvv.exe"), "");
        if (withCompiler) File.WriteAllText(Path.Combine(VvvvDir, "vvvvc.exe"), "");
        if (hostTfm is not null)
            File.WriteAllText(Path.Combine(VvvvDir, "vvvv.runtimeconfig.json"), $$"""{ "runtimeOptions": { "tfm": "{{hostTfm}}", "frameworks": [] } }""");
        if (vlVersion is not null)
        {
            var packs = Path.Combine(VvvvDir, "packs", "VL.Core");
            Directory.CreateDirectory(packs);
            File.WriteAllText(Path.Combine(packs, "VL.Core.nuspec"),
                $"""<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>VL.Core</id><version>{vlVersion}</version></metadata></package>""");
        }
        Install = VvvvInstallation.FromDirectory(VvvvDir, "test");
    }

    public DiscoveredEnvironment Build(bool justFound = true, string? dotnetArchitecture = "x64", params string[] sdks) => new(
        new VvvvDiscoveryResult([Install], null, []),
        new DotnetDiscoveryResult(true, dotnetArchitecture, (sdks.Length == 0 ? ["10.0.100"] : sdks).Select(v => new DotnetSdk(v, @"C:\Program Files\dotnet\sdk")).ToList()),
        new JustDiscoveryResult(justFound, justFound ? "1.0.0" : null),
        WorkDir);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}
