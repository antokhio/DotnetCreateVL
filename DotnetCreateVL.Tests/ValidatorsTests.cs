using DotnetCreateVL.Model;

namespace DotnetCreateVL.Tests;

public sealed class ValidatorsTests
{
    [Theory]
    [InlineData("Gallery")]
    [InlineData("VL.Shapes")]
    [InlineData("my-app_2")]
    public void FolderName_accepts_valid_names(string name) =>
        Assert.Null(Validators.FolderName(name));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(" Gallery")]
    [InlineData("Gallery.")]
    [InlineData("bad:name")]
    [InlineData("a/b")]
    [InlineData("CON")]
    [InlineData("com1.txt")]
    public void FolderName_rejects_invalid_names(string name) =>
        Assert.NotNull(Validators.FolderName(name));

    [Theory]
    [InlineData("app", RepositoryKind.Application)]
    [InlineData("Application", RepositoryKind.Application)]
    [InlineData("PACKAGE", RepositoryKind.Package)]
    [InlineData("pkg", RepositoryKind.Package)]
    public void ParseKind_accepts_aliases(string input, RepositoryKind expected) =>
        Assert.Equal(expected, Validators.ParseKind(input));

    [Fact]
    public void ParseKind_rejects_unknown() => Assert.Null(Validators.ParseKind("library"));

    [Theory]
    [InlineData("none", PipelineBackend.None)]
    [InlineData("Just", PipelineBackend.Just)]
    [InlineData("CMD", PipelineBackend.Cmd)]
    public void ParsePipeline_is_case_insensitive(string input, PipelineBackend expected) =>
        Assert.Equal(expected, Validators.ParsePipeline(input));

    [Fact]
    public void DestinationPath_rejects_existing_file()
    {
        var file = Path.GetTempFileName();
        try
        {
            Assert.NotNull(Validators.DestinationPath(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void DestinationPath_accepts_missing_directory() =>
        Assert.Null(Validators.DestinationPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
}
