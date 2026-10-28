using DotnetCreateVL.Model;

namespace DotnetCreateVL.Tests;

/// <summary>Fully resolved Phase 1–8 models for generation tests; every slot is set the way the phases would leave it.</summary>
internal sealed class ResolvedModels
{
    public BasicsModel Basics { get; } = new();
    public ApplicationModel App { get; } = new();
    public PackagesModel Packages { get; } = new();
    public ProjectsModel Projects { get; } = new();
    public CSharpModel CSharp { get; } = new();
    public ContentModel Content { get; } = new();
    public PipelineModel Pipeline { get; } = new();

    public string Destination => Basics.Destination.Require();

    public static ResolvedModels Application(FakeEnvironment env, PipelineBackend backend = PipelineBackend.Just, string name = "Gallery")
    {
        var m = new ResolvedModels();
        m.Basics.Kind.Set(RepositoryKind.Application, SlotSource.Flag);
        m.Basics.Name.Set(name, SlotSource.Flag);
        m.Basics.Destination.Set(Path.Combine(env.WorkDir, name), SlotSource.Default);
        m.Basics.Vvvv.Set(env.Install, SlotSource.Flag);
        m.Basics.Pipeline.Set(backend, SlotSource.Flag);
        m.App.Name.Set(name, SlotSource.Default);
        m.App.EntryPath.Set($"{name}.vl", SlotSource.Default);
        m.Content.RepoFiles.Set(new HashSet<RepoFile> { RepoFile.GitIgnore, RepoFile.GitAttributes, RepoFile.NuGetConfig }, SlotSource.Default);
        m.Content.Folders.Set([new ContentFolder(FolderKind.Assets, null, "assets")], SlotSource.Default);
        m.SetPipelineDefaults();
        return m;
    }

    public static ResolvedModels PackageRepository(FakeEnvironment env, PipelineBackend backend = PipelineBackend.Cmd, string name = "VL.Mapper")
    {
        var m = new ResolvedModels();
        m.Basics.Kind.Set(RepositoryKind.Package, SlotSource.Flag);
        m.Basics.Name.Set(name, SlotSource.Flag);
        m.Basics.Destination.Set(Path.Combine(env.WorkDir, name), SlotSource.Default);
        m.Basics.Vvvv.Set(env.Install, SlotSource.Flag);
        m.Basics.Pipeline.Set(backend, SlotSource.Flag);
        m.Content.RepoFiles.Set(new HashSet<RepoFile> { RepoFile.GitIgnore, RepoFile.GitAttributes, RepoFile.NuGetConfig, RepoFile.License }, SlotSource.Default);
        m.Content.License.Set(new LicenseChoice("MIT", "Test Holder"), SlotSource.Flag);
        m.Content.Folders.Set([], SlotSource.Default);
        m.SetPipelineDefaults();
        return m;
    }

    public PackageModel AddPackage(string id, string? root = null, bool wrapper = false)
    {
        var pkg = new PackageModel(id);
        pkg.Root.Set(root ?? id, SlotSource.Flag);
        pkg.Wrapper.Set(wrapper, SlotSource.Flag);
        Packages.Packages.Add(pkg);
        if (wrapper)
            AddProject(id, ProjectRole.PackageWrapper, pkg.WrapperProjectPath, Path.Combine(pkg.Root.Require(), "lib"), VlBinding.Assembly, ownerPackageId: id);
        Pipeline.Editable.Set(Packages.Packages.Select(p => p.Id).ToList(), SlotSource.Default);
        return pkg;
    }

    public ProjectModel AddProject(string name, ProjectRole role, string projectPath, string outputBase, VlBinding binding,
        string? tfm = null, ProjectFeature[]? features = null, string[]? references = null, PackageReference[]? packages = null, string? ownerPackageId = null)
    {
        var p = new ProjectModel(name, role, ownerPackageId);
        p.ProjectPath.Set(projectPath, SlotSource.Default);
        p.OutputBase.Set(outputBase, SlotSource.Default);
        p.Binding.Set(binding, SlotSource.Default);
        p.Tfm.Set(tfm ?? "net8.0", SlotSource.Default);
        p.Features.Set(new HashSet<ProjectFeature>(features ?? []), SlotSource.Default);
        p.References.Set(references ?? [], SlotSource.Default);
        p.Packages.Set(packages ?? [], SlotSource.Default);
        Projects.Projects.Add(p);
        EnsureCSharp();
        return p;
    }

    public void EnsureCSharp(VersionManagement versions = VersionManagement.Shared, string solutionFormat = "slnx")
    {
        if (!CSharp.Dotnet.IsResolved) CSharp.Dotnet.Set(new DotnetSelection("dotnet", "10.0.100", "x64"), SlotSource.Default);
        if (!CSharp.Tfm.IsResolved) CSharp.Tfm.Set("net8.0", SlotSource.Default);
        if (!CSharp.VlVersion.IsResolved) CSharp.VlVersion.Set("2025.7.4", SlotSource.Default);
        if (!CSharp.Configuration.IsResolved) CSharp.Configuration.Set("Debug", SlotSource.Default);
        if (!CSharp.Solution.IsResolved) CSharp.Solution.Set(new SolutionChoice(Basics.Name.Require(), solutionFormat), SlotSource.Default);
        if (!CSharp.Versions.IsResolved) CSharp.Versions.Set(versions, SlotSource.Default);
        if (!CSharp.Sources.IsResolved) CSharp.Sources.Set([NuGetSource.VvvvPublicFeed], SlotSource.Default);
    }

    public TargetModel AddTarget(string name, string? entryPath, string? packageId, bool dev, BuildOperation build, string[] prerequisites, StagingRule[]? staging = null)
    {
        var t = new TargetModel(name, packageId is null ? "application" : $"package {packageId}", entryPath, packageId);
        t.Dev.Set(dev, SlotSource.Default);
        t.Build.Set(build, SlotSource.Default);
        t.Prerequisites.Set(prerequisites, SlotSource.Default);
        t.ExportRoot.Set(PipelineModel.DefaultExportRoot, SlotSource.Default);
        t.Staging.Set(staging ?? [], SlotSource.Default);
        Pipeline.Targets.Add(t);
        Pipeline.DefaultTarget.Set(Pipeline.Targets.Count == 1 ? name : "all", SlotSource.Default);
        return t;
    }

    private void SetPipelineDefaults()
    {
        Pipeline.NugetPath.Set(PipelineModel.ConventionNugetPath, SlotSource.Default);
        Pipeline.ExportSources.Set(PipelineModel.ConventionExportSources, SlotSource.Default);
        Pipeline.PackageRepositories.Set([""], SlotSource.Default);
        Pipeline.Editable.Set([], SlotSource.Default);
        Pipeline.DefaultTarget.Set("all", SlotSource.Default);
    }
}
