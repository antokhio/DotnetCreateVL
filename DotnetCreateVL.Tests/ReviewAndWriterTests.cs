using System.Text;
using DotnetCreateVL.Flow;
using DotnetCreateVL.Generate;
using Spectre.Console.Testing;

namespace DotnetCreateVL.Tests;

public sealed class ReviewAndWriterTests
{
    private static GenerationPlan SamplePlan(string destination)
    {
        var plan = new GenerationPlan(destination);
        plan.Folder("assets");
        plan.File("assets\\.gitkeep", "");
        plan.File("src\\Gallery.Core.csproj", "<Project />");
        plan.File("README.md", "# Gallery\n");
        plan.File("Gallery.vl", "<Document />");
        return plan;
    }

    [Fact]
    public void Writer_creates_folders_and_files_without_bom_and_leaves_kept_files_alone()
    {
        using var env = new FakeEnvironment();
        var dest = Path.Combine(env.WorkDir, "Gallery");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "README.md"), "mine");
        var plan = SamplePlan(dest);

        var written = PlanWriter.Write(plan);

        Assert.Equal(["assets", "assets\\.gitkeep", "src\\Gallery.Core.csproj", "Gallery.vl"], written.Select(w => w.Path));
        Assert.True(Directory.Exists(Path.Combine(dest, "assets")));
        var bytes = File.ReadAllBytes(Path.Combine(dest, "src", "Gallery.Core.csproj"));
        Assert.Equal("<Project />"u8.ToArray(), bytes);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(dest, "README.md")));
        Assert.Equal("<Document />", File.ReadAllText(Path.Combine(dest, "Gallery.vl")));
    }

    [Fact]
    public async Task Review_prints_tree_with_actions_and_returns_generate_on_yes()
    {
        using var env = new FakeEnvironment();
        var dest = Path.Combine(env.WorkDir, "Gallery");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "README.md"), "mine");
        var console = new TestConsole().Interactive().Width(200);
        console.Input.PushTextWithEnter("y");

        var decision = await new ReviewPhase(console, new SpectrePrompter(console)).RunAsync(SamplePlan(dest), yes: false, dryRun: false, CancellationToken.None);

        Assert.Equal(ReviewDecision.Generate, decision);
        Assert.Contains("[9/9] Review", console.Output);
        Assert.Contains("assets\\", console.Output);
        Assert.Contains("Gallery.Core.csproj", console.Output);
        Assert.Contains("README.md (exists)", console.Output);
        Assert.Contains("Gallery.vl", console.Output);
        Assert.Contains("3 new · 1 kept", console.Output);
        Assert.Contains("Generate?", console.Output);
    }

    [Fact]
    public async Task Review_returns_declined_on_no()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive().Width(200);
        console.Input.PushTextWithEnter("n");

        var decision = await new ReviewPhase(console, new SpectrePrompter(console)).RunAsync(SamplePlan(Path.Combine(env.WorkDir, "G")), yes: false, dryRun: false, CancellationToken.None);

        Assert.Equal(ReviewDecision.Declined, decision);
    }

    [Fact]
    public async Task Review_honours_dry_run_yes_and_missing_terminal_without_prompting()
    {
        using var env = new FakeEnvironment();
        var plan = SamplePlan(Path.Combine(env.WorkDir, "G"));
        var console = new TestConsole().Width(200);

        Assert.Equal(ReviewDecision.DryRun, await new ReviewPhase(console, null).RunAsync(plan, yes: true, dryRun: true, CancellationToken.None));
        Assert.Equal(ReviewDecision.Generate, await new ReviewPhase(console, null).RunAsync(plan, yes: true, dryRun: false, CancellationToken.None));
        Assert.Equal(ReviewDecision.NeedsApproval, await new ReviewPhase(console, null).RunAsync(plan, yes: false, dryRun: false, CancellationToken.None));
        Assert.DoesNotContain("Generate?", console.Output);
    }
}
