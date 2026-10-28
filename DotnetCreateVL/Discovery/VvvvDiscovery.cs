using DotnetCreateVL.Model;
using Microsoft.Win32;

namespace DotnetCreateVL.Discovery;

/// <summary>
/// Finds vvvv gamma installations from the Program Files convention and the Windows
/// uninstall registry. Candidates are deduplicated by path and validated on disk.
/// Nothing found here is ever selected automatically.
/// </summary>
internal static class VvvvDiscovery
{
    private const string FolderPrefix = "vvvv_gamma_";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static VvvvDiscoveryResult Discover()
    {
        var byPath = new Dictionary<string, VvvvInstallation>(StringComparer.OrdinalIgnoreCase);
        var rejected = new List<string>();

        foreach (var dir in ProgramFilesCandidates())
            Add(byPath, dir, "Program Files", declaredVersion: null, rejected);

        if (OperatingSystem.IsWindows())
        {
            foreach (var (dir, version) in RegistryCandidates())
                Add(byPath, dir, "registry", version, rejected);
        }

        VvvvInstallation? hint = null;
        var hintValue = Environment.GetEnvironmentVariable("VVVV_DIR");
        if (!string.IsNullOrWhiteSpace(hintValue))
        {
            if (Directory.Exists(hintValue))
            {
                hint = VvvvInstallation.FromDirectory(hintValue, "VVVV_DIR");
                byPath.Remove(hint.Path);
            }
            else
            {
                rejected.Add($"VVVV_DIR points to a missing directory: {hintValue}");
            }
        }

        var installs = byPath.Values
            .OrderByDescending(i => i.HasEditor)
            .ThenByDescending(i => ParseVersion(i.Version))
            .ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new VvvvDiscoveryResult(installs, hint, rejected);
    }

    /// <summary>Parses the leading numeric part of a version such as "2025.7.4.1+2e65cda729" or "7.4-win-x64".</summary>
    private static Version ParseVersion(string? version)
    {
        if (string.IsNullOrEmpty(version))
            return new Version(0, 0);

        var end = 0;
        while (end < version.Length && (char.IsDigit(version[end]) || version[end] == '.'))
            end++;

        var numeric = version[..end].Trim('.');
        if (!numeric.Contains('.'))
            numeric += ".0";

        return Version.TryParse(numeric, out var v) ? v : new Version(0, 0);
    }

    private static void Add(
        Dictionary<string, VvvvInstallation> byPath,
        string directory,
        string origin,
        string? declaredVersion,
        List<string> rejected)
    {
        if (!Directory.Exists(directory))
        {
            rejected.Add($"{origin}: {directory} does not exist");
            return;
        }

        var install = VvvvInstallation.FromDirectory(directory, origin, declaredVersion);
        if (byPath.TryGetValue(install.Path, out var existing))
        {
            // Keep the entry with better version information; merge origins for display.
            var keep = existing.Version is null && install.Version is not null ? install : existing;
            byPath[install.Path] = keep with { Origin = $"{existing.Origin} + {origin}" };
            return;
        }

        byPath[install.Path] = install;
    }

    private static IEnumerable<string> ProgramFilesCandidates()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };

        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var vvvvRoot = Path.Combine(root, "vvvv");
            if (!Directory.Exists(vvvvRoot))
                continue;

            IEnumerable<string> dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(vvvvRoot, FolderPrefix + "*");
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var dir in dirs)
                yield return dir;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IEnumerable<(string Directory, string? Version)> RegistryCandidates()
    {
        var results = new List<(string, string?)>();

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = hklm.OpenSubKey(UninstallKey);
            if (uninstall is null)
                continue;

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                if (!subKeyName.StartsWith(FolderPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                using var key = uninstall.OpenSubKey(subKeyName);
                var location = key?.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(location))
                    continue;

                var version = key?.GetValue("DisplayVersion") as string;
                results.Add((location, string.IsNullOrWhiteSpace(version) ? null : version));
            }
        }

        return results;
    }
}

internal sealed record VvvvDiscoveryResult(
    IReadOnlyList<VvvvInstallation> Installations,
    VvvvInstallation? EnvironmentHint,
    IReadOnlyList<string> Rejected);
