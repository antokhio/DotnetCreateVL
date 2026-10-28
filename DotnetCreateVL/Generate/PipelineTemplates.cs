using System.Text;
using DotnetCreateVL.Model;

namespace DotnetCreateVL.Generate;

/// <summary>A Phase 8 target with every path resolved, ready for templating.</summary>
internal sealed record TargetPlan(
    string Name,
    string? EntryPath,
    string? AppName,
    bool Dev,
    BuildOperation Build,
    IReadOnlyList<string> ProjectPaths,
    string? ExportRoot,
    IReadOnlyList<StagingRule> Staging)
{
    public bool Builds => Build != BuildOperation.None && ProjectPaths.Count > 0 || Build == BuildOperation.Export;
    public bool Exports => Build == BuildOperation.Export && EntryPath is not null && ExportRoot is not null;
}

/// <summary>Shared pipeline settings with paths in the shape each backend needs.</summary>
internal sealed record PipelineContext(
    string VvvvExe,
    string VvvvcExe,
    string DotnetExe,
    string Configuration,
    string? NugetPath,
    string? ExportSources,
    IReadOnlyList<string> PackageRepositories,
    IReadOnlyList<string> Editable,
    string DefaultTarget,
    IReadOnlyList<TargetPlan> Targets)
{
    public IEnumerable<TargetPlan> DevTargets => Targets.Where(t => t.Dev && t.EntryPath is not null);
    public IEnumerable<TargetPlan> BuildTargets => Targets.Where(t => t.Builds);
    public bool AnyExport => Targets.Any(t => t.Exports);
    public bool AnyProjectBuild => Targets.Any(t => t.ProjectPaths.Count > 0 && t.Build != BuildOperation.None);
    /// <summary>Recipe that launches vvvv without a patch; renamed when a target is already called vvvv.</summary>
    public string BareLaunch => Targets.Any(t => t.Name.Equals("vvvv", StringComparison.OrdinalIgnoreCase)) ? "editor" : "vvvv";
}

internal static class JustTemplates
{
    private const string Shell = """set windows-shell := ["powershell.exe", "-NoLogo", "-NoProfile", "-Command"]""";
    private const string CopyTree = """function Copy-Tree($src, $dst) { Write-Host "[copy] $dst"; robocopy $src $dst /E /NFL /NDL /NJH /NJS /NP | Out-Null; if ($LASTEXITCODE -ge 8) { exit 1 } }""";

    public static string Root(PipelineContext c)
    {
        var sb = new StringBuilder(Shell).AppendLine().AppendLine();
        sb.AppendLine("mod dev '.just/dev.just'");
        if (c.BuildTargets.Any()) sb.AppendLine("mod build '.just/build.just'");
        sb.AppendLine();
        var target = c.DefaultTarget;
        var module = target == "all" || c.BuildTargets.Any(t => t.Name == target) ? "build"
            : c.DevTargets.Any(t => t.Name == target) ? "dev"
            : null;
        if (module is null) { module = "dev"; target = c.BareLaunch; }
        sb.AppendLine($"# Same as `just {module} {target}`").AppendLine($"default *args: ({module}::{target} args)");
        return sb.ToString();
    }

    /// <summary>Dev target run by a bare <c>just dev</c>: the default target when it opens a document, otherwise the first one.</summary>
    public static TargetPlan? DefaultDevTarget(PipelineContext c) =>
        c.DevTargets.FirstOrDefault(t => t.Name == c.DefaultTarget) ?? c.DevTargets.FirstOrDefault();

    public static IEnumerable<TargetPlan> OrderedDevTargets(PipelineContext c)
    {
        var first = DefaultDevTarget(c);
        return first is null ? c.DevTargets : c.DevTargets.Where(t => t == first).Concat(c.DevTargets.Where(t => t != first));
    }

    public static string Dev(PipelineContext c)
    {
        var sb = new StringBuilder(Shell).AppendLine().AppendLine();
        sb.AppendLine($"vvvv_exe := '{Fwd(c.VvvvExe)}'");
        sb.AppendLine("root := justfile_directory()");
        AppendSharedVariables(sb, c, includeSources: false);
        sb.AppendLine();

        // `just dev` runs the module's first recipe, so the default dev target comes before the bare launch.
        var first = DefaultDevTarget(c);
        foreach (var t in OrderedDevTargets(c))
            sb.AppendLine(t == first ? $"# Open {t.EntryPath} (also `just dev`)" : $"# Open {t.EntryPath}")
              .AppendLine($"{t.Name} *args: (_launch '{Fwd(t.EntryPath!)}' args)").AppendLine();

        var bare = c.BareLaunch;
        sb.AppendLine(first is null ? "# Launch vvvv without a patch (also `just dev`); extra args are forwarded" : "# Launch vvvv without a patch; extra args are forwarded")
          .AppendLine($"{bare} *args: (_launch '' args)").AppendLine();

        sb.AppendLine("[private]").AppendLine("_launch patch args:");
        sb.AppendLine("    @$a = @(); \\");
        // Start-Process joins the list into one command line, so values are quoted explicitly.
        foreach (var line in SharedArgLines(c, includeSources: false, quoted: true)) sb.AppendLine($"    {line} \\");
        sb.AppendLine("    if ('{{patch}}') { $a += @('--open', '\"{{root / patch}}\"') }; \\");
        sb.AppendLine("    $a += '--debug'; if ('{{args}}') { $a += '{{args}}' }; \\");
        sb.AppendLine("    Start-Process -FilePath '{{vvvv_exe}}' -ArgumentList $a -WorkingDirectory '{{root}}'");
        return sb.ToString();
    }

    public static string Build(PipelineContext c)
    {
        var sb = new StringBuilder(Shell).AppendLine().AppendLine();
        if (c.AnyProjectBuild)
        {
            sb.AppendLine($"dotnet := '{Fwd(c.DotnetExe)}'");
            sb.AppendLine($"configuration := '{c.Configuration}'");
        }
        sb.AppendLine("root := justfile_directory()");
        if (c.AnyExport)
        {
            sb.AppendLine($"vvvvc := '{Fwd(c.VvvvcExe)}'");
            sb.AppendLine("icon := root / 'resources/icon.ico'");
            AppendSharedVariables(sb, c, includeSources: true);
        }
        sb.AppendLine();

        var targets = c.BuildTargets.ToList();
        if (targets.Count > 1)
            sb.AppendLine("# Build every target").AppendLine($"all *args: {string.Join(" ", targets.Select(t => $"({t.Name} args)"))}").AppendLine();

        foreach (var t in targets)
        {
            var deps = new List<string>();
            var projects = string.Join(";", t.ProjectPaths.Select(Fwd));
            if (t.ProjectPaths.Count > 0) deps.Add(t.Exports ? $"(_build '{projects}' '')" : $"(_build '{projects}' args)");
            if (t.Exports) deps.Add($"(_export '{Fwd(t.EntryPath!)}' '{Fwd(t.ExportRoot!)}' '{t.AppName}' args)");

            var what = new List<string>();
            if (t.ProjectPaths.Count > 0) what.Add($"build {string.Join(", ", t.ProjectPaths.Select(p => Path.GetFileNameWithoutExtension(p)))}");
            if (t.Exports) what.Add($"export {t.EntryPath} to {t.ExportRoot}\\{t.AppName}\\" + (t.Staging.Count == 0 ? "" : $" and copy {string.Join(", ", t.Staging.Select(s => s.Source))} into it"));
            sb.AppendLine($"# {Capitalize(string.Join(", then ", what))}");
            sb.Append($"{t.Name} *args:").Append(deps.Count > 0 ? " " + string.Join(" ", deps) : "").AppendLine();
            if (t.Exports && t.Staging.Count > 0)
            {
                sb.AppendLine($"    @$app = '{{{{root / '{Fwd(t.ExportRoot!)}/{t.AppName}'}}}}'; \\");
                sb.AppendLine($"    {CopyTree}; \\");
                foreach (var s in t.Staging)
                    sb.AppendLine($"    Copy-Tree '{{{{root / '{Fwd(s.Source)}'}}}}' \"$app/{Fwd(s.Destination)}\"; \\");
                sb.AppendLine("    exit 0");
            }
            sb.AppendLine();
        }

        if (c.AnyProjectBuild)
        {
            sb.AppendLine("[private]").AppendLine("_build projects args:");
            sb.AppendLine("    @$extra = @('{{args}}' -split '\\s+' | Where-Object { $_ }); \\");
            sb.AppendLine("    foreach ($p in ('{{projects}}' -split ';')) { \\");
            sb.AppendLine("        Write-Host \"[build] $(Split-Path $p -Leaf)\"; \\");
            sb.AppendLine("        & '{{dotnet}}' build ('{{root}}/' + $p) -c {{configuration}} -nologo -v minimal @extra; \\");
            sb.AppendLine("        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } \\");
            sb.AppendLine("    }; \\");
            sb.AppendLine("    exit 0").AppendLine();
        }

        if (c.AnyExport)
        {
            // vvvvc writes <out>/<app>/<app>.exe and keeps its generated C# in <out>/src (cleaned by --clean).
            sb.AppendLine("[private]").AppendLine("_export patch out app args:");
            sb.AppendLine("    @$app = '{{root / out / app}}'; if (Test-Path $app) { Remove-Item $app -Recurse -Force }; \\");
            sb.AppendLine("    $a = @('{{root / patch}}', '--output-directory', '{{root / out}}', '--asset-behavior', 'RelativeToDocument', '--output-type', 'WinExe', '--rid', 'win-x64', '--clean', 'true'); \\");
            foreach (var line in SharedArgLines(c, includeSources: true)) sb.AppendLine($"    {line} \\");
            sb.AppendLine("    if (Test-Path '{{icon}}') { $a += @('--app-icon', '{{icon}}') }; \\");
            sb.AppendLine("    $extra = @('{{args}}' -split '\\s+' | Where-Object { $_ }); \\");
            sb.AppendLine("    Write-Host \"[export] {{patch}} -> {{out}}/{{app}}\"; & '{{vvvvc}}' @a @extra; exit $LASTEXITCODE");
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendSharedVariables(StringBuilder sb, PipelineContext c, bool includeSources)
    {
        if (c.NugetPath is not null) sb.AppendLine($"nuget_path := root / '{Fwd(c.NugetPath)}'");
        if (includeSources && c.ExportSources is not null) sb.AppendLine($"sources_path := root / '{Fwd(c.ExportSources)}'");
        if (c.PackageRepositories.Count > 0)
            sb.AppendLine("package_repositories := " + string.Join(" + ';' + ", c.PackageRepositories.Select(r => r.Length == 0 ? "root" : $"root / '{Fwd(r)}'")));
        if (c.Editable.Count > 0) sb.AppendLine($"editable_packages := '{string.Join(";", c.Editable)}'");
    }

    private static IEnumerable<string> SharedArgLines(PipelineContext c, bool includeSources, bool quoted = false)
    {
        // Parenthesized: inside @(...) the comma binds tighter than +, so an unparenthesized
        // '"' + x + '"' would become three separate elements. Folders are created first: vvvv
        // refuses to start with a missing nuget path.
        var (q, qe) = quoted ? ("('\"' + ", " + '\"')") : ("", "");
        if (c.NugetPath is not null) yield return $"$null = New-Item -ItemType Directory -Force '{{{{nuget_path}}}}'; $a += @('--nuget-path', {q}[IO.Path]::GetFullPath('{{{{nuget_path}}}}'){qe});";
        if (includeSources && c.ExportSources is not null) yield return $"$null = New-Item -ItemType Directory -Force '{{{{sources_path}}}}'; $a += @('--export-package-sources', {q}[IO.Path]::GetFullPath('{{{{sources_path}}}}'){qe});";
        if (c.PackageRepositories.Count > 0) yield return quoted ? "$a += @('--package-repositories', '\"{{package_repositories}}\"');" : "$a += @('--package-repositories', '{{package_repositories}}');";
        if (c.Editable.Count > 0) yield return "$a += @('--editable-packages', '{{editable_packages}}');";
    }

    private static string Fwd(string path) => path.Replace('\\', '/');
    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

internal static class CmdTemplates
{
    /// <summary>File names: `_dev.cmd` / `_build.cmd` with a single target, suffixed with the target otherwise.</summary>
    public static string DevFile(PipelineContext c, TargetPlan t) => c.DevTargets.Count() == 1 ? "_dev.cmd" : $"_dev_{t.Name}.cmd";
    public static string BuildFile(PipelineContext c, TargetPlan t) => c.BuildTargets.Count() == 1 ? "_build.cmd" : $"_build_{t.Name}.cmd";

    public static string Launch(PipelineContext c, TargetPlan? t)
    {
        var sb = Header();
        sb.AppendLine($"set VVVV=\"{c.VvvvExe}\"");
        sb.AppendLine();
        AppendEnsureFolders(sb, c, includeSources: false);
        sb.Append("start \"\" %VVVV%");
        foreach (var a in SharedArgs(c, includeSources: false)) sb.Append(' ').Append(a);
        if (t?.EntryPath is not null) sb.Append($" --open \"%ROOT%{t.EntryPath}\"");
        sb.AppendLine(" --debug %*");
        return sb.ToString();
    }

    public static string Build(PipelineContext c, TargetPlan t)
    {
        var sb = Header();
        if (t.ProjectPaths.Count > 0)
        {
            sb.AppendLine($"set DOTNET=\"{c.DotnetExe}\"");
            sb.AppendLine($"set CONFIGURATION={c.Configuration}");
        }
        if (t.Exports)
        {
            sb.AppendLine($"set VVVVC=\"{c.VvvvcExe}\"");
            sb.AppendLine($"set OUT=%ROOT%{t.ExportRoot}");
            sb.AppendLine($"set APP=%OUT%\\{t.AppName}");
            sb.AppendLine("set ICON=");
            sb.AppendLine("if exist \"%ROOT%resources\\icon.ico\" set ICON=--app-icon \"%ROOT%resources\\icon.ico\"");
        }
        sb.AppendLine();
        foreach (var p in t.ProjectPaths)
        {
            sb.AppendLine($"echo [build] {Path.GetFileName(p)}");
            sb.AppendLine($"%DOTNET% build \"%ROOT%{p}\" -c %CONFIGURATION% -nologo -v minimal{(t.Exports ? "" : " %*")} || exit /b 1");
        }
        if (t.Exports)
        {
            sb.AppendLine();
            sb.AppendLine("rem vvvvc writes %OUT%\\<App>\\<App>.exe and keeps its generated C# in %OUT%\\src");
            sb.AppendLine("if exist \"%APP%\" rmdir /s /q \"%APP%\"");
            AppendEnsureFolders(sb, c, includeSources: true);
            sb.AppendLine($"echo [export] {t.EntryPath} -^> {t.ExportRoot}\\{t.AppName}");
            sb.Append($"%VVVVC% \"%ROOT%{t.EntryPath}\"");
            foreach (var a in SharedArgs(c, includeSources: true)) sb.Append(' ').Append(a);
            sb.AppendLine(" --output-directory \"%OUT%\" %ICON% --asset-behavior RelativeToDocument --output-type WinExe --rid win-x64 --clean true %* || exit /b 1");
            foreach (var s in t.Staging)
            {
                sb.AppendLine($"echo [copy] {s.Destination}");
                sb.AppendLine($"robocopy \"%ROOT%{s.Source}\" \"%APP%\\{s.Destination}\" /E /NFL /NDL /NJH /NJS /NP");
                sb.AppendLine("if %ERRORLEVEL% GEQ 8 exit /b 1");
            }
        }
        sb.AppendLine("exit /b 0");
        return sb.ToString();
    }

    public static string BuildAll(PipelineContext c)
    {
        var sb = Header();
        foreach (var t in c.BuildTargets)
            sb.AppendLine($"call \"%ROOT%{BuildFile(c, t)}\" %* || exit /b 1");
        sb.AppendLine("exit /b 0");
        return sb.ToString();
    }

    private static StringBuilder Header() => new StringBuilder().AppendLine("@echo off").AppendLine("setlocal").AppendLine("set ROOT=%~dp0");

    /// <summary>vvvv refuses to start with a missing nuget path, so shared folders are created before use.</summary>
    private static void AppendEnsureFolders(StringBuilder sb, PipelineContext c, bool includeSources)
    {
        if (c.NugetPath is not null) sb.AppendLine($"if not exist \"%ROOT%{c.NugetPath}\" mkdir \"%ROOT%{c.NugetPath}\"");
        if (includeSources && c.ExportSources is not null) sb.AppendLine($"if not exist \"%ROOT%{c.ExportSources}\" mkdir \"%ROOT%{c.ExportSources}\"");
    }

    private static IEnumerable<string> SharedArgs(PipelineContext c, bool includeSources)
    {
        if (c.NugetPath is not null) yield return $"--nuget-path \"%ROOT%{c.NugetPath}\"";
        if (includeSources && c.ExportSources is not null) yield return $"--export-package-sources \"%ROOT%{c.ExportSources}\"";
        if (c.PackageRepositories.Count > 0) yield return $"--package-repositories \"{string.Join(";", c.PackageRepositories.Select(r => r.Length == 0 ? "%ROOT%." : $"%ROOT%{r}"))}\"";
        if (c.Editable.Count > 0) yield return $"--editable-packages \"{string.Join(";", c.Editable)}\"";
    }
}
