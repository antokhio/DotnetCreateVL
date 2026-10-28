using System.Diagnostics;

namespace DotnetCreateVL.Model;

/// <summary>
/// A validated vvvv installation directory. Editor and compiler presence are reported
/// separately so each operation can require only the executable it needs.
/// </summary>
public sealed record VvvvInstallation(
    string Path,
    string? Version,
    string VersionSource,
    bool HasEditor,
    bool HasCompiler,
    string Origin)
{
    public string EditorPath => System.IO.Path.Combine(Path, "vvvv.exe");
    public string CompilerPath => System.IO.Path.Combine(Path, "vvvvc.exe");

    public string DisplayVersion => ShortVersion ?? "unknown version";

    /// <summary>Version without the "+build" suffix, which is what people recognise an installation by.</summary>
    public string? ShortVersion
    {
        get
        {
            if (Version is null) return null;
            var plus = Version.IndexOf('+');
            return plus > 0 ? Version[..plus] : Version;
        }
    }

    public static VvvvInstallation FromDirectory(string directory, string origin, string? declaredVersion = null)
    {
        var full = System.IO.Path.GetFullPath(directory).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var editor = System.IO.Path.Combine(full, "vvvv.exe");
        var compiler = System.IO.Path.Combine(full, "vvvvc.exe");
        var hasEditor = File.Exists(editor);
        var hasCompiler = File.Exists(compiler);

        string? version = null;
        var versionSource = "none";

        if (hasEditor)
        {
            var product = FileVersionInfo.GetVersionInfo(editor).ProductVersion;
            if (!string.IsNullOrWhiteSpace(product))
            {
                version = product;
                versionSource = "vvvv.exe";
            }
        }

        if (version is null && !string.IsNullOrWhiteSpace(declaredVersion))
        {
            version = declaredVersion;
            versionSource = "registry";
        }

        if (version is null)
        {
            var folder = System.IO.Path.GetFileName(full);
            const string prefix = "vvvv_gamma_";
            if (folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && folder.Length > prefix.Length)
            {
                version = folder[prefix.Length..];
                versionSource = "folder name";
            }
        }

        return new VvvvInstallation(full, version, versionSource, hasEditor, hasCompiler, origin);
    }
}
