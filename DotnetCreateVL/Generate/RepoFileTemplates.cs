using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using DotnetCreateVL.Model;

namespace DotnetCreateVL.Generate;

/// <summary>Housekeeping files: .gitignore, .gitattributes, NuGet.config, CSharpier, LICENSE.</summary>
internal static class RepoFileTemplates
{
    public const string CSharpierVersion = "1.3.0";
    private const string IgnoreMarker = "# dotnet create-vl";

    public static string GitIgnore(IEnumerable<string> extraFolders) =>
        $"""
        ## .NET / Visual Studio
        [Bb]in/
        [Oo]bj/
        .vs/
        .vshistory/
        *.user
        *.suo
        *.userprefs
        *.swp
        *.DotSettings.user
        TestResults/
        *.nupkg
        *.snupkg
        .idea/

        ## vvvv
        *.vl.bak
        *.vl.lock
        .tmp/

        {IgnoreBlock(extraFolders)}
        """;

    public static string? MergeGitIgnore(string existing, IEnumerable<string> extraFolders)
    {
        if (existing.Contains(IgnoreMarker, StringComparison.Ordinal)) return null;
        var sb = new StringBuilder(existing.TrimEnd());
        sb.AppendLine().AppendLine().Append(IgnoreBlock(extraFolders)).AppendLine();
        return sb.ToString();
    }

    private static string IgnoreBlock(IEnumerable<string> extraFolders)
    {
        var bases = new[] { "bin/" };
        var sb = new StringBuilder(IgnoreMarker).AppendLine();
        var extras = extraFolders.Select(ToGitFolder)
            .Where(f => !bases.Any(b => f.StartsWith(b, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !f.Split('/', StringSplitOptions.RemoveEmptyEntries).Last().Equals("bin", StringComparison.OrdinalIgnoreCase));
        foreach (var f in bases.Concat(extras).Distinct(StringComparer.OrdinalIgnoreCase))
            sb.AppendLine(f);
        return sb.ToString().TrimEnd();
    }

    private static string ToGitFolder(string repoRelative) => repoRelative.Replace('\\', '/').Trim('/') + "/";

    public static readonly string[] GitAttributeLines = ["* text=auto", "*.vl diff=html"];

    public static string GitAttributes() => string.Join(Environment.NewLine, GitAttributeLines) + Environment.NewLine;

    public static string? MergeGitAttributes(string existing)
    {
        var present = existing.Split('\n').Select(l => l.Trim()).ToHashSet(StringComparer.Ordinal);
        var missing = GitAttributeLines.Where(l => !present.Contains(l)).ToList();
        if (missing.Count == 0) return null;
        return existing.TrimEnd() + Environment.NewLine + string.Join(Environment.NewLine, missing) + Environment.NewLine;
    }

    public static string NuGetConfig(IReadOnlyList<NuGetSource> sources)
    {
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("configuration",
                new XElement("packageSources", sources.Select(s => new XElement("add", new XAttribute("key", s.Name), new XAttribute("value", s.Location))))));
        return Xml(doc);
    }

    public static string? MergeNuGetConfig(string existing, IReadOnlyList<NuGetSource> sources)
    {
        XDocument doc;
        try { doc = XDocument.Parse(existing); }
        catch (System.Xml.XmlException) { return null; }
        var root = doc.Root;
        if (root is null || root.Name.LocalName != "configuration") return null;
        var packageSources = root.Element("packageSources");
        if (packageSources is null) { packageSources = new XElement("packageSources"); root.Add(packageSources); }
        var keys = packageSources.Elements("add").Select(a => (string?)a.Attribute("key")).Where(k => k is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = false;
        foreach (var s in sources.Where(s => !keys.Contains(s.Name)))
        {
            packageSources.Add(new XElement("add", new XAttribute("key", s.Name), new XAttribute("value", s.Location)));
            added = true;
        }
        return added ? Xml(doc) : null;
    }

    public static string CSharpierRc() =>
        """
        {
          "printWidth": 100,
          "useTabs": false,
          "indentSize": 4,
          "endOfLine": "auto"
        }

        """;

    public static string ToolsManifest() => Json(new JsonObject
    {
        ["version"] = 1,
        ["isRoot"] = true,
        ["tools"] = new JsonObject { ["csharpier"] = CSharpierTool() },
    });

    public static string? MergeToolsManifest(string existing)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(existing); }
        catch (JsonException) { return null; }
        if (node is not JsonObject obj) return null;
        if (obj["tools"] is not JsonObject tools) { tools = new JsonObject(); obj["tools"] = tools; }
        if (tools.ContainsKey("csharpier")) return null;
        tools["csharpier"] = CSharpierTool();
        return Json(obj);
    }

    private static JsonObject CSharpierTool() => new()
    {
        ["version"] = CSharpierVersion,
        ["commands"] = new JsonArray("csharpier"),
        ["rollForward"] = false,
    };

    public static string License(LicenseChoice choice)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Licenses.{choice.Spdx}.txt")
            ?? throw new InvalidOperationException($"No license text for {choice.Spdx}.");
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        var year = DateTime.Now.Year.ToString();
        var holder = choice.Holder ?? "";
        return text
            .Replace("<year>", year).Replace("[yyyy]", year)
            .Replace("<copyright holders>", holder).Replace("<owner>", holder).Replace("[name of copyright owner]", holder)
            .Replace("Copyright (c)  ", "Copyright (c) ").Replace("Copyright  ", "Copyright ");
    }

    public static string Xml(XDocument doc)
    {
        var sb = new StringBuilder();
        using (var writer = System.Xml.XmlWriter.Create(sb, new System.Xml.XmlWriterSettings { Indent = true, IndentChars = "  ", OmitXmlDeclaration = doc.Declaration is null, Encoding = new UTF8Encoding(false) }))
            doc.Save(writer);
        return sb.ToString().Replace("utf-16", "utf-8") + Environment.NewLine;
    }

    private static string Json(JsonNode node) => node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
}
