# VL application structure

Reference for understanding VL application repositories, synthesized from four project summaries. These are shared structural conventions and observed variations, not a mandatory folder layout or a specification for the project-generation tool.

## Application model

- A VL application has an **entry patch** opened by vvvv gamma, with optional supporting VL documents, C# libraries, local VL packages, and runtime assets.
- Distinguish the **application root**, **local package roots**, **authored C# projects**, and **generated application projects**. They are not interchangeable.
- Entry patches are selected by launch/export configuration, not inferred from every root `.vl` filename. An application may have alternate entries, such as a separate render patch.
- A package document provides reusable definitions; it is not automatically the application entry. Setting the application root as a package repository does not make its entry patch a package document.
- Authored C# projects in the examples are class libraries. Building them does not run the VL application or produce its standalone executable.

## Representative layout

```text
application\
|-- <AppName>.vl                     Application entry patch
|-- <AppName>.props                  Optional application compiler/export settings
|-- <SolutionName>.slnx              Optional authored solution; .sln also occurs
|-- Directory.Packages.props         Optional shared dependency-version configuration
|-- src\                            Optional application-local C# and supporting documents
|   |-- <LibraryName>.csproj         Single-library layout; may instead be nested
|   |-- *.cs
|   `-- Properties\
|       |-- AssemblyInfo.cs          VL import configuration
|       `-- launchSettings.json      Optional development/debug profile
|-- repos\                          Optional local VL package repository
|   `-- <PackageId>\                 Package document, optional C# sources and help
|-- assets\                         Optional source data, media, and configuration
|-- rnd\ · vl\ · sandbox\ · docs\    Optional experimental/test VL documents, sub-patches, notes
|-- resources\                      Optional icon.ico and other export resources
|-- lib\<Configuration>\<TFM>\       Generated application-local library outputs
|-- bin\                            Optional generated application export
|   |-- src\                        Compiler-generated solution and C# projects
|   `-- <AppName>\                   Executable, dependencies, assets, runtime data
|-- _dev.cmd / _build.cmd            Optional CMD launch/export scripts
`-- .justfile / .just\               Alternative Just-based orchestration
```

This tree combines common roles; no example requires every entry. Local packages also occur under `packages\` or directly at the application root. Supporting patches may live under `src\`, `vl\`, or other folders. Solutions may be at the root or alongside a nested C# project. Package internals follow the [VL package structure reference](package-structure-vl.md).

## C# libraries, references, and naming

- Applications may have no C# project, one application-local library, multiple libraries, or a mixture of local libraries and reusable VL packages.
- Use the actual `ProjectReference` graph to establish C# build relationships. Sharing a solution or directory does not establish a dependency.
- Application libraries may be referenced from VL through `.csproj` or DLL references. **A directly referenced `.csproj` sharing a VL document's name causes a name collision.** Keep application-library names distinct from the VL documents they accompany, for example `MyApp.vl` and `MyApp.Core.csproj`.
- **Packages use a different convention:** `<PackageId>.vl` and `<PackageId>.csproj` may share the name when the compiled assembly is imported through `lib`, rather than through a direct `.csproj` reference.
- Assembly-level `ImportAsIs` configuration exposes C# APIs to VL; namespace/category mappings and additional node attributes depend on the library. These attributes do not establish which patches consume the API.
- Observed library targets are `net8.0` and `net8.0-windows`. Windows Forms and other platform-specific features belong only to libraries that need them. The exported application can have a different target/platform configuration from its supporting libraries.
- Dependency versions may be inline or shared as MSBuild properties. `Directory.Packages.props` containing `VLVersion` is not, by itself, enabled NuGet Central Package Management. Compiler-generated central version files are separate from authored dependency configuration.
- C# restore sources in `NuGet.config` and vvvv's package location selected by `--nuget-path` serve different purposes; do not conflate them.

## Library output locations

| Authored project arrangement | Observed output arrangement |
|---|---|
| Application-local `src\<Library>.csproj` | Root `lib\<Configuration>\<TFM>\` |
| Nested `src\<Library>\<Library>.csproj` | Root `lib\<Configuration>\<TFM>\`, through an adjusted output base |
| Local package `<PackageId>\src\<PackageId>.csproj` | Package-local `lib\<Configuration>\<TFM>\` |
| Conventional helper C# project | Project-local `bin\<Configuration>\<TFM>\` |

These locations follow project settings, not directory names alone. `BaseOutputPath` is relative to the project directory; configuration, target framework, and command-line overrides affect the final path. Intermediate `obj\` directories belong to their respective projects.

An application's root export `bin\` and a helper project's C# `bin\` have different roles. Existing DLLs and generated projects may be stale; their presence is not proof of a current successful build.

## Build, editor launch, and export

| Operation | Responsibility |
|---|---|
| `dotnet build <project-or-solution>` | Build authored C# libraries and their project dependencies |
| `vvvv.exe --open <entry-patch>` | Open the selected application in the vvvv editor |
| `vvvvc.exe <entry-patch> ...` | Compile/export a standalone VL application |
| Run the exported executable | Start an existing export; does not rebuild it |

- CMD scripts and Just recipes are observed orchestration alternatives; neither is required by the application structure. Some launchers build prerequisite libraries first, others only open the patch.
- DLL-based application bindings require the library at the referenced path. A Release library build does not update a binding to `lib\Debug\<TFM>\`; export configuration and prerequisite-library configuration are separate.
- Application `.props` files and compiler arguments can specify output location, executable type, runtime/platform, asset behavior, and clean/error policies. These are application settings, not the authored C# library's settings.
- `bin\src\` contains compiler-generated projects, not a source template or an authoritative portable dependency manifest. Observed exports include absolute references to installations, caches, local packages, and Debug library DLLs.

### Development environment

Launchers may configure:

- `--package-repositories`: locations containing local VL packages.
- `--nuget-path`: the vvvv package location, either project-local or shared externally.
- `--editable-packages`: local package names/patterns enabled for editing.
- `--open`: the selected editor entry patch.
- `--allowmultiple`: optional multiple-instance development behavior.

These are observed settings, not a universal minimum. Installation paths, package locations, editable-name patterns, and working directories are environment-specific. Paths should resolve relative to an explicit working directory or launcher location, with quoting for spaces. Shell continuation syntax belongs in scripts, not in executable argument strings.

## Assets and structural boundaries

- Assets may include images, models, shaders, video, fonts, spreadsheets, or configuration data. Their organization is application-specific; an empty folder or ignore rule does not establish a runtime requirement.
- Source assets and exported assets are different locations. Relative-to-output asset settings alone do not prove copying or font provisioning is implemented.
- Local package directories may contain source, binaries, or manually supplied dependencies, including ignored files. The tracked repository alone may not reproduce the local environment.
- Treat `.vl` documents as opaque, user-authored files: do not read, generate, or edit their contents. Identify roles through filenames and external build/launch metadata; internal graph wiring and dependency bindings remain unknown unless independently supplied or verified in vvvv.
- Keep authored files distinct from generated library/export outputs and caches. Do not copy generated projects, machine-specific paths, administrator elevation, error-suppression policies, or destructive cleanup scripts into general conventions.
- C# build success does not validate patch behavior, node loading, runtime assets, or export correctness. The source summaries are read-only observations; none establishes a verified end-to-end build or launch.

## Source summaries

- [Lidar Calibrator](app-structure-lidar-calibrator.md): multiple local packages and a C# project-reference graph.
- [Older application](app-structure-old.md): nested helper library, reusable packages, and substantial source assets.
- [Teliks](app-structure-teliks.md): one application-local library and Just-based launch/export.
- [Industry](app-structure-industry.md): nested library, alternate entry, application props, and CMD-based launch/export.
