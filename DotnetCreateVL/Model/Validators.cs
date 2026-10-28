namespace DotnetCreateVL.Model;

/// <summary>Slot validators. Each returns <c>null</c> on success or a one-line reason on failure.</summary>
public static class Validators
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string? FolderName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A name is required.";
        if (value != value.Trim())
            return "Leading or trailing whitespace is not allowed.";
        if (value.EndsWith('.'))
            return "A Windows folder name cannot end with '.'.";
        var invalid = value.IndexOfAny(Path.GetInvalidFileNameChars());
        if (invalid >= 0)
            return $"Character '{value[invalid]}' is not allowed in a folder name.";
        if (ReservedNames.Contains(Path.GetFileNameWithoutExtension(value)))
            return $"'{value}' is a reserved Windows device name.";
        return null;
    }

    public static string? DestinationPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A destination path is required.";
        string full;
        try
        {
            full = Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return $"Not a valid path: {ex.Message}";
        }
        if (File.Exists(full))
            return "The destination is an existing file.";
        if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
            return "The destination already contains files; choose a new or empty folder.";
        return null;
    }

    public static string? VvvvDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A vvvv installation directory is required.";
        if (!Directory.Exists(value))
            return "The directory does not exist.";
        return null;
    }

    /// <summary>A VL document name without extension; also the application name.</summary>
    public static string? DocumentName(string? value)
    {
        if (FolderName(value) is { } e)
            return e;
        if (value!.EndsWith(".vl", StringComparison.OrdinalIgnoreCase))
            return "Give the name without the .vl extension.";
        return null;
    }

    /// <summary>A repository-relative path to a .vl document that stays inside the repository.</summary>
    public static string? RepositoryVlPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A document path is required.";
        var trimmed = value.Trim();
        if (Path.IsPathRooted(trimmed))
            return "Use a path relative to the repository root.";
        if (!trimmed.EndsWith(".vl", StringComparison.OrdinalIgnoreCase))
            return "The document path must end with .vl.";
        if (trimmed.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return "The path contains invalid characters.";
        var segments = trimmed.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s == ".."))
            return "The path must not leave the repository.";
        foreach (var segment in segments.Where(s => s != "."))
        {
            if (FolderName(segment) is { } e)
                return e;
        }
        return null;
    }

    /// <summary>A package name: NuGet-style ID that is also usable as a folder and document name.</summary>
    public static string? PackageId(string? value, IEnumerable<string> existingIds)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A package name is required.";
        if (DocumentName(value) is { } e)
            return e;
        if (!char.IsLetter(value![0]) || value.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '_' or '-')))
            return "Package names start with a letter and contain only letters, digits, '.', '_' or '-'.";
        if (existingIds.Contains(value, StringComparer.OrdinalIgnoreCase))
            return $"A package named '{value}' already exists.";
        return null;
    }

    /// <summary>A repository-relative folder path that stays inside the repository; empty means the root itself.</summary>
    public static string? RepositoryFolder(string? value, bool allowEmpty)
    {
        if (string.IsNullOrWhiteSpace(value))
            return allowEmpty ? null : "A folder is required.";
        var trimmed = value.Trim();
        if (Path.IsPathRooted(trimmed))
            return "Use a path relative to the repository root.";
        if (trimmed.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return "The path contains invalid characters.";
        var segments = trimmed.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s == ".."))
            return "The path must not leave the repository.";
        if (!allowEmpty && segments.All(s => s == "."))
            return "A folder below the repository root is required.";
        foreach (var segment in segments.Where(s => s != "."))
        {
            if (FolderName(segment) is { } e)
                return e;
        }
        return null;
    }

    /// <summary>
    /// A C# project name: valid folder name, dotted identifier segments, unique across projects and
    /// case-insensitively distinct from every VL document name it could be referenced next to.
    /// </summary>
    public static string? ProjectName(string? value, IEnumerable<string> existingProjects, IEnumerable<string> documentNames)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A library name is required.";
        if (FolderName(value) is { } e)
            return e;
        var segments = value!.Split('.');
        if (segments.Any(s => s.Length == 0 || !(char.IsLetter(s[0]) || s[0] == '_') || s.Any(c => !(char.IsLetterOrDigit(c) || c == '_'))))
            return "Use dot-separated identifiers such as Gallery.Core (letters, digits and '_', each part starting with a letter).";
        if (existingProjects.Contains(value, StringComparer.OrdinalIgnoreCase))
            return $"A project named '{value}' already exists.";
        if (documentNames.Contains(value, StringComparer.OrdinalIgnoreCase))
            return $"'{value}' is already the name of a VL document; a directly referenced library must differ (for example {value}.Core).";
        return null;
    }

    /// <summary>A repository-relative .csproj path.</summary>
    public static string? RepositoryProjectPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A project path is required.";
        if (!value.Trim().EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return "The project path must end with .csproj.";
        return RepositoryFolder(Path.GetDirectoryName(value.Trim()), allowEmpty: true)
            ?? FolderName(Path.GetFileName(value.Trim()));
    }

    public static RepositoryKind? ParseKind(string value) => value.Trim().ToLowerInvariant() switch
    {
        "app" or "application" => RepositoryKind.Application,
        "package" or "pkg" or "packages" => RepositoryKind.Package,
        _ => null,
    };

    public static PipelineBackend? ParsePipeline(string value) => value.Trim().ToLowerInvariant() switch
    {
        "none" => PipelineBackend.None,
        "just" => PipelineBackend.Just,
        "cmd" => PipelineBackend.Cmd,
        _ => null,
    };

    public static string? DotnetExecutable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A dotnet executable path is required.";
        if (!File.Exists(value.Trim()))
            return "The file does not exist.";
        return null;
    }

    /// <summary>A .NET target framework moniker such as net8.0 or net8.0-windows.</summary>
    public static string? Tfm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A target framework is required.";
        var v = value.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(v, @"^net\d+\.\d+(-[a-z]+(\d+(\.\d+)*)?)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return "Expected a moniker like net8.0 or net8.0-windows.";
        return null;
    }

    /// <summary>An exact NuGet package version (no ranges, no wildcards).</summary>
    public static string? PackageVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A version is required.";
        var v = value.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d+(\.\d+){1,3}(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$"))
            return "Expected an exact version like 2025.7.4.";
        return null;
    }

    public static string? Configuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A configuration is required.";
        var v = value.Trim();
        return v.Equals("Debug", StringComparison.OrdinalIgnoreCase) || v.Equals("Release", StringComparison.OrdinalIgnoreCase)
            ? null
            : "Expected Debug or Release.";
    }

    /// <summary>A solution file name without extension, or <c>none</c>.</summary>
    public static string? SolutionName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A solution name or 'none' is required.";
        var v = value.Trim();
        if (v.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;
        if (v.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || v.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            return "Give the name without extension; the format is a separate setting.";
        return FolderName(v);
    }

    public static string? SolutionFormat(string? value) =>
        value?.Trim().ToLowerInvariant() is "sln" or "slnx" ? null : "Expected sln or slnx.";

    public static VersionManagement? ParseVersions(string value) => value.Trim().ToLowerInvariant() switch
    {
        "shared" => VersionManagement.Shared,
        "inline" => VersionManagement.Inline,
        "keep" => VersionManagement.Keep,
        _ => null,
    };

    /// <summary>A restore source as <c>name=url-or-path</c>, or the shorthands <c>vvvv</c> / <c>none</c>. Credentials embedded in the URL are refused.</summary>
    public static string? NuGetSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A source is required.";
        var v = value.Trim();
        if (v.Equals("vvvv", StringComparison.OrdinalIgnoreCase) || v.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;
        var eq = v.IndexOf('=');
        if (eq <= 0 || eq == v.Length - 1)
            return "Expected name=url, name=path, vvvv or none.";
        var name = v[..eq].Trim();
        var location = v[(eq + 1)..].Trim();
        if (name.Length == 0 || name.Any(char.IsWhiteSpace))
            return "The source name must not contain whitespace.";
        if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            if (!string.IsNullOrEmpty(uri.UserInfo))
                return "Credentials in a source URL are not accepted; configure them outside the repository.";
            if (uri.Scheme is not ("http" or "https"))
                return "Expected an http(s) URL or a directory path.";
            return null;
        }
        try { Path.GetFullPath(location); }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return $"Not a valid URL or path: {ex.Message}";
        }
        return null;
    }

    /// <summary>Validates a comma-separated list of sources; empty or <c>none</c> means none.</summary>
    public static string? NuGetSourceList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (NuGetSource(item) is { } e) return $"'{item}': {e}";
        return null;
    }

    /// <summary>Builds the source list from raw items: <c>vvvv</c> expands to the public feed, <c>none</c> clears what was collected so far.</summary>
    public static string? ParseNuGetSources(IEnumerable<string> items, IReadOnlyList<NuGetSource> baseline, out List<NuGetSource> sources)
    {
        sources = [.. baseline];
        foreach (var raw in items)
        {
            if (NuGetSource(raw) is { } e) return $"'{raw}': {e}";
            var v = raw.Trim();
            if (v.Equals("none", StringComparison.OrdinalIgnoreCase)) { sources.Clear(); continue; }
            var s = v.Equals("vvvv", StringComparison.OrdinalIgnoreCase) ? Model.NuGetSource.VvvvPublicFeed : ParseNuGetSource(v);
            if (sources.Any(x => x.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase)))
            {
                if (s == Model.NuGetSource.VvvvPublicFeed) continue;
                return $"'{raw}': duplicate source name.";
            }
            sources.Add(s);
        }
        return null;
    }

    public static NuGetSource ParseNuGetSource(string value)
    {
        var v = value.Trim();
        var eq = v.IndexOf('=');
        return new NuGetSource(v[..eq].Trim(), v[(eq + 1)..].Trim());
    }

    public static VlBinding? ParseBinding(string value) => value.Trim().ToLowerInvariant() switch
    {
        "direct" or "csproj" => VlBinding.Direct,
        "assembly" or "dll" or "lib" => VlBinding.Assembly,
        "none" => VlBinding.None,
        _ => null,
    };

    public static ProjectFeature? ParseFeature(string value) => value.Trim().ToLowerInvariant() switch
    {
        "windows" => ProjectFeature.Windows,
        "winforms" => ProjectFeature.WinForms,
        "stride" => ProjectFeature.Stride,
        _ => null,
    };

    /// <summary>A NuGet reference as <c>Id@Version</c> with an exact version.</summary>
    public static string? PackageReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A package reference is required.";
        var v = value.Trim();
        var at = v.IndexOf('@');
        if (at <= 0 || at == v.Length - 1)
            return "Expected Id@Version, for example MessagePack@2.5.124.";
        if (PackageId(v[..at], []) is { } e)
            return e;
        return PackageVersion(v[(at + 1)..]);
    }

    /// <summary>Validates a comma-separated list of package references; empty or <c>none</c> means none.</summary>
    public static string? PackageReferenceList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (PackageReference(item) is { } e) return $"'{item}': {e}";
        return null;
    }

    public static PackageReference ParsePackageReference(string value)
    {
        var v = value.Trim();
        var at = v.IndexOf('@');
        return new PackageReference(v[..at].Trim(), v[(at + 1)..].Trim());
    }

    public static RepoFile? ParseRepoFile(string value) => value.Trim().ToLowerInvariant() switch
    {
        "gitignore" or ".gitignore" => RepoFile.GitIgnore,
        "gitattributes" or ".gitattributes" => RepoFile.GitAttributes,
        "nuget-config" or "nuget.config" or "nuget" => RepoFile.NuGetConfig,
        "csharpier" => RepoFile.CSharpier,
        "license" => RepoFile.License,
        _ => null,
    };

    /// <summary>An SPDX identifier from the supported set, matched case-insensitively and returned canonically.</summary>
    public static string? ParseLicense(string value) =>
        LicenseChoice.KnownSpdx.FirstOrDefault(s => s.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string? LicenseHolder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Any(char.IsControl) ? "The holder must not contain control characters." : null;
    }

    /// <summary>A folder path that may leave the repository (for example <c>..\__nugets</c>) or <c>none</c>.</summary>
    public static string? FolderOrNone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A folder or none is required.";
        var v = value.Trim();
        if (v.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        if (v.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return "The path contains invalid characters.";
        return null;
    }

    /// <summary>Comma-separated repository folders or <c>none</c>.</summary>
    public static string? RepositoryFolderList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (RepositoryFolder(item, allowEmpty: false) is { } e) return $"'{item}': {e}";
        return null;
    }

    /// <summary>A vvvv editable-package pattern: a package id with an optional trailing wildcard; broad wildcards are refused.</summary>
    public static string? EditablePattern(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A package id pattern is required.";
        var v = value.Trim();
        var core = v.EndsWith('*') ? v[..^1] : v;
        if (core.Length < 2 || core.Contains('*') || core.Contains('?'))
            return "Use a package id or a prefix followed by *, for example VL.Shapes or VL.My*.";
        return PackageId(core.TrimEnd('.'), []);
    }

    public static string? EditablePatternList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (EditablePattern(item) is { } e) return $"'{item}': {e}";
        return null;
    }

    public static BuildOperation? ParseBuildOperation(string value) => value.Trim().ToLowerInvariant() switch
    {
        "export" => BuildOperation.Export,
        "lib" or "library" or "libraries" => BuildOperation.Library,
        "none" => BuildOperation.None,
        _ => null,
    };

    public static bool? ParseSwitch(string value) => value.Trim().ToLowerInvariant() switch
    {
        "on" or "yes" or "true" or "y" => true,
        "off" or "no" or "false" or "n" => false,
        _ => null,
    };

    /// <summary>A staging rule <c>src[:dst]</c>, both repository/export-relative folders.</summary>
    public static string? StagingRule(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A folder is required.";
        var v = value.Trim();
        var colon = v.IndexOf(':');
        if (colon == 0 || colon == v.Length - 1)
            return "Expected src or src:dst, for example assets or media:assets.";
        var src = colon < 0 ? v : v[..colon];
        var dst = colon < 0 ? null : v[(colon + 1)..];
        if (RepositoryFolder(src, allowEmpty: false) is { } e) return e;
        return dst is null ? null : RepositoryFolder(dst, allowEmpty: false);
    }

    public static string? StagingRuleList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (StagingRule(item) is { } e) return $"'{item}': {e}";
        return null;
    }

    public static StagingRule ParseStagingRule(string value)
    {
        var v = value.Trim();
        var colon = v.IndexOf(':');
        var src = NormalizeFolder(colon < 0 ? v : v[..colon]);
        var dst = colon < 0 ? Path.GetFileName(src) : NormalizeFolder(v[(colon + 1)..]);
        return new StagingRule(src, dst);
    }

    public static string NormalizeFolder(string path) =>
        string.Join('\\', path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Where(s => s != "."));
}
