using System.Buffers.Binary;
using System.Xml.Linq;

namespace DotnetCreateVL.Generate;

/// <summary>
/// VL's 22-character document IDs. Mirrors VL.Lang's GUIDEncoders.GuidTobase62: the two 8-byte
/// halves of <see cref="Guid.ToByteArray"/> are read big-endian and encoded separately into 11
/// characters each — not one 128-bit base62 number.
/// </summary>
internal static class VlId
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static string New() => From(Guid.NewGuid());

    public static string From(Guid guid)
    {
        var bytes = guid.ToByteArray();
        Span<char> chars = stackalloc char[22];
        Encode(BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(0, 8)), chars[..11]);
        Encode(BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(8, 8)), chars[11..]);
        return new string(chars);
    }

    private static void Encode(ulong value, Span<char> target)
    {
        for (var i = target.Length - 1; i >= 0; i--)
        {
            target[i] = Alphabet[(int)(value % 62)];
            value /= 62;
        }
    }
}

/// <summary>
/// The document vvvv itself writes for File → New: a VL.CoreLib dependency and an
/// <c>Application</c> process with its Create/Update lifecycle. Shape taken from documents saved
/// by vvvv gamma 7.x; no nodes, pins or links are invented.
/// </summary>
internal static class VlTemplates
{
    private static readonly XNamespace Property = "property";

    /// <param name="languageVersion">The VL version of the selected installation (VL.Core.nuspec), e.g. <c>2025.7.4</c>.</param>
    public static string Document(string languageVersion)
    {
        var create = VlId.New();
        var update = VlId.New();

        var document = new XElement("Document",
            new XAttribute(XNamespace.Xmlns + "p", Property),
            new XAttribute(XNamespace.Xmlns + "r", "reflection"),
            new XAttribute("Id", VlId.New()),
            new XAttribute("LanguageVersion", languageVersion),
            new XAttribute("Version", "0.128"),
            new XElement("NugetDependency",
                new XAttribute("Id", VlId.New()),
                new XAttribute("Location", "VL.CoreLib"),
                new XAttribute("Version", languageVersion)),
            new XElement("Patch",
                new XAttribute("Id", VlId.New()),
                new XElement("Canvas",
                    new XAttribute("Id", VlId.New()),
                    new XAttribute("DefaultCategory", "Main"),
                    new XAttribute("BordersChecked", "false"),
                    new XAttribute("CanvasType", "FullCategory")),
                new XElement("Node",
                    new XAttribute("Name", "Application"),
                    new XAttribute("Bounds", "100,100"),
                    new XAttribute("Id", VlId.New()),
                    new XElement(Property + "NodeReference",
                        new XElement("Choice", new XAttribute("Kind", "ContainerDefinition"), new XAttribute("Name", "Process")),
                        new XElement("CategoryReference", new XAttribute("Kind", "Category"), new XAttribute("Name", "Primitive"))),
                    new XElement("Patch",
                        new XAttribute("Id", VlId.New()),
                        new XElement("Canvas", new XAttribute("Id", VlId.New()), new XAttribute("CanvasType", "Group")),
                        new XElement("Patch", new XAttribute("Id", create), new XAttribute("Name", "Create")),
                        new XElement("Patch", new XAttribute("Id", update), new XAttribute("Name", "Update")),
                        new XElement("ProcessDefinition",
                            new XAttribute("Id", VlId.New()),
                            new XElement("Fragment", new XAttribute("Id", VlId.New()), new XAttribute("Patch", create), new XAttribute("Enabled", "true")),
                            new XElement("Fragment", new XAttribute("Id", VlId.New()), new XAttribute("Patch", update), new XAttribute("Enabled", "true")))))));

        return RepoFileTemplates.Xml(new XDocument(new XDeclaration("1.0", "utf-8", null), document));
    }
}
