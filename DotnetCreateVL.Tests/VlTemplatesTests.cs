using System.Xml.Linq;
using DotnetCreateVL.Generate;

namespace DotnetCreateVL.Tests;

public sealed class VlTemplatesTests
{
    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("00000000-0000-0000-0000-000000000001", "AAAAAAAAAAAAAAAAAAAAAB")]
    [InlineData("00000000-0000-0100-0000-000000000000", "AAAAAAAAAABAAAAAAAAAAA")]
    public void Ids_match_vvvv_encoding(string guid, string expected) =>
        Assert.Equal(expected, VlId.From(Guid.Parse(guid)));

    [Fact]
    public void New_ids_are_22_alphanumeric_characters_and_unique()
    {
        var ids = Enumerable.Range(0, 200).Select(_ => VlId.New()).ToList();

        Assert.All(ids, id => Assert.Matches("^[A-Za-z0-9]{22}$", id));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Document_is_the_vvvv_new_document_skeleton()
    {
        var text = VlTemplates.Document("2025.7.4");

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", text);
        var doc = XDocument.Parse(text);
        var root = doc.Root!;
        Assert.Equal("Document", root.Name.LocalName);
        Assert.Equal("property", root.GetNamespaceOfPrefix("p")!.NamespaceName);
        Assert.Equal("reflection", root.GetNamespaceOfPrefix("r")!.NamespaceName);
        Assert.Equal("2025.7.4", (string?)root.Attribute("LanguageVersion"));
        Assert.Equal("0.128", (string?)root.Attribute("Version"));

        var dep = Assert.Single(root.Elements("NugetDependency"));
        Assert.Equal("VL.CoreLib", (string?)dep.Attribute("Location"));
        Assert.Equal("2025.7.4", (string?)dep.Attribute("Version"));

        var canvas = root.Element("Patch")!.Element("Canvas")!;
        Assert.Equal("FullCategory", (string?)canvas.Attribute("CanvasType"));
        Assert.Equal("Main", (string?)canvas.Attribute("DefaultCategory"));

        var app = Assert.Single(root.Element("Patch")!.Elements("Node"));
        Assert.Equal("Application", (string?)app.Attribute("Name"));
        var reference = app.Element(XNamespace.Get("property") + "NodeReference")!;
        Assert.Equal("Process", (string?)reference.Element("Choice")!.Attribute("Name"));
        Assert.Equal("Primitive", (string?)reference.Element("CategoryReference")!.Attribute("Name"));

        var patch = app.Element("Patch")!;
        var create = patch.Elements("Patch").Single(p => (string?)p.Attribute("Name") == "Create");
        var update = patch.Elements("Patch").Single(p => (string?)p.Attribute("Name") == "Update");
        var fragments = patch.Element("ProcessDefinition")!.Elements("Fragment").Select(f => (string?)f.Attribute("Patch")).ToList();
        Assert.Equal([(string?)create.Attribute("Id"), (string?)update.Attribute("Id")], fragments);

        var ids = doc.Descendants().Select(e => (string?)e.Attribute("Id")).Where(id => id is not null).ToList();
        Assert.Equal(12, ids.Count);
        Assert.All(ids, id => Assert.Matches("^[A-Za-z0-9]{22}$", id));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Each_document_gets_fresh_ids()
    {
        var a = XDocument.Parse(VlTemplates.Document("2025.7.4")).Root!.Attribute("Id")!.Value;
        var b = XDocument.Parse(VlTemplates.Document("2025.7.4")).Root!.Attribute("Id")!.Value;

        Assert.NotEqual(a, b);
    }
}
