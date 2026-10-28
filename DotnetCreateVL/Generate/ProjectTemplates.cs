using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using DotnetCreateVL.Model;

namespace DotnetCreateVL.Generate;

/// <summary>C# project files, the VL import attribute, shared versions and the solution.</summary>
internal static class ProjectTemplates
{
    public const string VlVersionProperty = "VLVersion";

    public static string Csproj(ProjectModel p, ProjectsModel all, CSharpModel csharp, bool shared)
    {
        var dir = Path.GetDirectoryName(p.ProjectPath.Require()) ?? "";
        var features = p.Features.Require();

        var props = new XElement("PropertyGroup",
            new XElement("TargetFramework", p.Tfm.Require()),
            new XElement("ImplicitUsings", "enable"),
            new XElement("Nullable", "enable"),
            new XElement("BaseOutputPath", Relative(dir, p.OutputBase.Require()) + "\\"));
        if (features.Contains(ProjectFeature.WinForms))
            props.Add(new XElement("UseWindowsForms", "true"));

        var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), props);

        var packages = new List<(string Id, string Version)>();
        if (p.ExposesVL) packages.Add(("VL.Core", shared ? $"$({VlVersionProperty})" : csharp.VlVersion.Require()));
        if (features.Contains(ProjectFeature.Stride)) packages.Add(("VL.Stride", shared ? $"$({VlVersionProperty})" : csharp.VlVersion.Require()));
        foreach (var r in p.Packages.Require())
            packages.Add((r.Id, shared ? $"$({VersionProperty(r.Id)})" : r.Version));
        if (packages.Count > 0)
            project.Add(new XElement("ItemGroup", packages.Select(x => new XElement("PackageReference", new XAttribute("Include", x.Id), new XAttribute("Version", x.Version)))));

        var refs = p.References.Require().Select(all.Find).Where(r => r is not null).Select(r => r!.ProjectPath.Require()).ToList();
        if (refs.Count > 0)
            project.Add(new XElement("ItemGroup", refs.Select(r => new XElement("ProjectReference", new XAttribute("Include", Relative(dir, r))))));

        return RepoFileTemplates.Xml(new XDocument(project));
    }

    public static string AssemblyInfo() =>
        """
        using VL.Core.Import;

        // Lets vvvv import every public type of this assembly as nodes.
        [assembly: ImportAsIs]

        """;

    public static string DirectoryPackagesProps(IEnumerable<ProjectModel> projects, string vlVersion)
    {
        var group = new XElement("PropertyGroup", new XElement(VlVersionProperty, vlVersion));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in projects.SelectMany(p => p.Packages.Require()))
            if (seen.Add(r.Id)) group.Add(new XElement(VersionProperty(r.Id), r.Version));
        return RepoFileTemplates.Xml(new XDocument(new XElement("Project", group)));
    }

    /// <summary>MessagePack → MessagePackVersion, VL.Skia → VLSkiaVersion.</summary>
    public static string VersionProperty(string packageId) =>
        new string(packageId.Where(char.IsLetterOrDigit).ToArray()) + "Version";

    public static string Slnx(IEnumerable<ProjectModel> projects) =>
        RepoFileTemplates.Xml(new XDocument(new XElement("Solution",
            projects.Select(p => new XElement("Project", new XAttribute("Path", p.ProjectPath.Require().Replace('\\', '/')))))));

    public static string Sln(IEnumerable<ProjectModel> projects)
    {
        const string sdkProjectType = "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}";
        var list = projects.ToList();
        var sb = new StringBuilder();
        sb.AppendLine().AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00")
          .AppendLine("# Visual Studio Version 17")
          .AppendLine("VisualStudioVersion = 17.0.31903.59")
          .AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");
        foreach (var p in list)
            sb.AppendLine($"Project(\"{sdkProjectType}\") = \"{p.Name}\", \"{p.ProjectPath.Require()}\", \"{StableGuid(p.Name)}\"").AppendLine("EndProject");
        sb.AppendLine("Global")
          .AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution")
          .AppendLine("\t\tDebug|Any CPU = Debug|Any CPU")
          .AppendLine("\t\tRelease|Any CPU = Release|Any CPU")
          .AppendLine("\tEndGlobalSection")
          .AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
        foreach (var p in list)
        {
            var g = StableGuid(p.Name);
            foreach (var c in new[] { "Debug", "Release" })
                sb.AppendLine($"\t\t{g}.{c}|Any CPU.ActiveCfg = {c}|Any CPU").AppendLine($"\t\t{g}.{c}|Any CPU.Build.0 = {c}|Any CPU");
        }
        sb.AppendLine("\tEndGlobalSection")
          .AppendLine("\tGlobalSection(SolutionProperties) = preSolution")
          .AppendLine("\t\tHideSolutionNode = FALSE")
          .AppendLine("\tEndGlobalSection")
          .AppendLine("EndGlobal");
        return sb.ToString();
    }

    private static string StableGuid(string seed) =>
        new Guid(MD5.HashData(Encoding.UTF8.GetBytes(seed))).ToString("B").ToUpperInvariant();

    /// <summary>Relative path between two repository-relative locations, with backslashes.</summary>
    public static string Relative(string fromDir, string to)
    {
        const string root = @"C:\__repo";
        var rel = Path.GetRelativePath(Path.Combine(root, fromDir), Path.Combine(root, to));
        return rel == "." ? "." : rel;
    }
}
