using DotnetCreateVL.Model;

namespace DotnetCreateVL.Tests;

public sealed class VvvvInstallationTests
{
    [Fact]
    public void Version_falls_back_to_registry_then_folder_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "dcvl-tests", Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "vvvv_gamma_7.4-win-x64");
        Directory.CreateDirectory(dir);
        try
        {
            var fromRegistry = VvvvInstallation.FromDirectory(dir, "registry", "2025.7.4.1");
            Assert.Equal("2025.7.4.1", fromRegistry.Version);
            Assert.Equal("registry", fromRegistry.VersionSource);

            var fromFolder = VvvvInstallation.FromDirectory(dir, "Program Files");
            Assert.Equal("7.4-win-x64", fromFolder.Version);
            Assert.Equal("folder name", fromFolder.VersionSource);
            Assert.False(fromFolder.HasEditor);
            Assert.False(fromFolder.HasCompiler);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
