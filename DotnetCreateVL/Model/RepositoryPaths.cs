namespace DotnetCreateVL.Model;

public static class RepositoryPaths
{
    /// <summary>Repository-relative form: backslashes, no leading <c>.\</c>, trimmed.</summary>
    public static string Normalize(string repositoryPath) =>
        repositoryPath.Trim().Replace('/', '\\').TrimStart('.', '\\');
}
