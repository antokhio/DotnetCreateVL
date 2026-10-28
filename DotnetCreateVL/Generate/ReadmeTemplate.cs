using System.Text;
using DotnetCreateVL.Model;

namespace DotnetCreateVL.Generate;

internal static class ReadmeTemplate
{
    public static string Render(BasicsModel basics, ApplicationModel app, PackagesModel packages, ProjectsModel projects, GenerationPlan plan)
    {
        var kind = basics.Kind.Require();
        var sb = new StringBuilder();
        sb.AppendLine($"# {basics.Name.Require()}").AppendLine();
        sb.AppendLine(kind == RepositoryKind.Application
            ? $"vvvv gamma application `{app.Name.Require()}` for vvvv {basics.Vvvv.Require().DisplayVersion}."
            : $"vvvv gamma package repository for vvvv {basics.Vvvv.Require().DisplayVersion}.");
        sb.AppendLine();

        sb.AppendLine("## Layout").AppendLine();
        if (kind == RepositoryKind.Application)
            sb.AppendLine($"- `{app.EntryPath.Require()}` — entry patch");
        foreach (var p in packages.Packages)
            sb.AppendLine($"- `{p.Root.Require()}\\` — package `{p.Id}`{(p.HasWrapper ? $" with C# project `{p.WrapperProjectPath}`" : "")}");
        foreach (var p in projects.Libraries)
            sb.AppendLine($"- `{p.ProjectPath.Require()}` — C# {(p.Role == ProjectRole.HelperLibrary ? "helper library" : "library")}");
        sb.AppendLine();

        if (plan.Commands.Count > 0)
        {
            sb.AppendLine("## Commands").AppendLine();
            sb.AppendLine("| Command | Does |").AppendLine("|---|---|");
            foreach (var (command, description) in plan.Commands)
                sb.AppendLine($"| `{command}` | {description} |");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
