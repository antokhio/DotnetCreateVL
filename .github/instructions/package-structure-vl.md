# VL package structure for project generation

Reference for an agent implementing a `dotnet tool` that generates VL package project files. Scope: source layout, project configuration, and local development builds.

The conventions below are synthesized from three existing projects. The proposed scaffold is a default for new projects, not a requirement to reorganize existing ones.

## Package model

- A VL package contains VL documents (`.vl` patches), usually help/example patches, and optionally compiled .NET nodes and runtime assets.
- Distinguish the **repository root**, **VL package root**, and **C# project directory**. They can be different directories.
- `<PackageId>\<PackageId>.vl` is the entry-document convention in the examples, not a universal discovery rule. Accept an explicit package root.
- C# nodes are exposed through VL import configuration; the entry document can supply additional VL-defined nodes and dependencies.
- A VL-only package does not require a `.csproj`, C# sources, or an assembly output directory.

## Suggested source layout

```text
repository\
|-- <SolutionName>.slnx                Optional solution; preserve existing solution format
|-- Directory.Packages.props          Optional shared dependency-version configuration
|-- <PackageId>\                      VL package root
|   |-- <PackageId>.vl                User-authored entry document, when supplied
|   |-- help\                        User-authored examples and reference patches
|   |-- src\                         Only for packages with C# nodes
|   |   |-- <PackageId>.csproj        SDK-style .NET class library
|   |   |-- *.cs                     Implementation and VL-facing nodes
|   |   `-- Properties\
|   |       |-- AssemblyInfo.cs       Assembly-level VL import configuration, when needed
|   |       `-- launchSettings.json   Optional local vvvv launch profile
|   |-- lib\                         Generated build output, not source
|   |   `-- <Configuration>\<TFM>\
|   `-- shaders\                     Optional runtime assets for shader-based packages
|-- README.md
|-- LICENSE
`-- icon.png                         Optional; assets\ is another observed convention
```

Optional entries are not generated unless the chosen features need them. `lib\` and `src\obj\` are created by the build, not populated with template binaries.

## C# project and dependency rules

- Generate a class library, not an executable. Keep package identity, project name, assembly name, and root namespace consistent; validate identifiers rather than blindly reusing a display name.
- All three examples target `net8.0`. Treat that as an observed baseline, not a permanent VL requirement: select the target framework and dependency versions for the intended vvvv gamma host.
- For a project under `<PackageId>\src\`, `BaseOutputPath` set to `..\lib\` produces the configuration/framework layout above under normal SDK defaults. Keep intermediate files in `src\obj\`.
- Add only dependencies required by the chosen node implementation. `VL.Core` and any Stride dependencies must use versions compatible with the intended host; do not copy sample version numbers into every generated project.
- Respect existing version-management conventions. A `Directory.Packages.props` file may hold shared properties such as `VLVersion` or `StrideVersion`; its presence alone does not establish NuGet central package management.
- Use `ProjectReference` for source projects in the same repository and `PackageReference` for external dependencies. Separate core and rendering projects are optional architecture, not a mandatory scaffold.
- Include VL import configuration appropriate to the selected VL API and node category. Do not invent attribute syntax or duplicate configuration already present in the project.

## VL documents and optional features

- Treat `.vl` files as opaque, user-owned visual-programming documents authored in vvvv gamma. The generator must not parse, generate, or rewrite their contents.
- Preserve existing entry and help documents. Copy user-supplied documents only when requested, without modifying them. Do not create an empty or guessed XML entry document; report that it still needs to be authored in vvvv.
- Help patches may be grouped by topic. No particular example names or subfolder structure are required.
- Stride-based projects may need a `.sdpkg` descriptor under `src\`, runtime shaders, and build steps that copy dependency assets into `shaders\`. Include these only for a feature/template that needs them; do not add them to every VL package.
- Local launch profiles may use vvvv's `--package-repositories` and `--editable-packages` options. Obtain executable and machine-specific paths from the user; never copy them from another repository.

## Build output must be explicit

The examples use two different output conventions:

| Convention | Assembly directory relative to the package root |
|---|---|
| Configuration-specific local build | `lib\<Configuration>\<TFM>\` |
| Explicit output override | `lib\<TFM>\` |

Use the configuration-specific convention as the new-project default. Preserve an existing project's intentional override. Do not infer the actual assembly path from the target framework alone: project properties and build-command overrides both matter.

For the suggested layout, run from the repository root:

```powershell
dotnet build .\<PackageId>\src\<PackageId>.csproj --configuration Debug
```

With `BaseOutputPath` configured as above, the expected output is `<PackageId>\lib\Debug\<TFM>\`. A Release build normally uses `lib\Release\<TFM>\`; `--output` changes that location.

VL documents can reference particular assembly paths. If a known reference expects `lib\Debug\<TFM>\` but the build writes to `lib\<TFM>\`, report the mismatch instead of assuming the locations are interchangeable or rewriting the document.

## Generator acceptance checklist

1. Resolve the destination, package identity, package root, C# support, target framework, dependency versions, and optional features before writing files. Require framework/dependency inputs only when relevant.
2. Preserve existing files and layout; report collisions rather than overwrite user content silently.
3. Keep generated project references and paths relative where possible. Do not embed another developer's absolute paths.
4. For C# scaffolds, build the generated project and verify the expected assembly at the configured path. Report build failures explicitly.
5. Verify enabled runtime-asset copy steps produce their required files. Do not treat a successful C# compilation as proof that assets are present.
6. Confirm existing `.vl` files remain unchanged and clearly report any entry/help documents the user still needs to provide.
7. Validate node discovery, document dependencies, and help patches in vvvv gamma when available. Otherwise report host validation as not performed; `dotnet build` does not validate VL integration.

## Source summaries

- [Nurbsy](package-structure-vl-nurbsy.md): separate core/rendering projects and an assembly-path mismatch.
- [Redux](package-structure-vl-redux.md): configuration-specific output and local development launch settings.
- [Stride Voxels](package-structure-vl-stride-voxels.md): optional Stride integration and shader asset copying.