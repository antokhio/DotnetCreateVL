# Technical requirements: VL repository scaffolding tool

## 1. Purpose and scope

Provide an installable `dotnet tool` for Windows that interactively creates a VL repository structure and parameterized project/development files.

Support:

- **Application:** an application entry patch, optional C# libraries, and zero or more local VL packages.
- **Package:** one or more local VL packages, each independently VL-only or backed by C#, with optional shared/helper libraries.
- **Pipeline selection:** `none`, `just`, or `cmd`, with equivalent workflow behavior for the two generated backends.

Keep application entries, package documents, authored C# projects, and generated application exports distinct. A package is not synonymous with a C# project, and a solution is not an application executable.

Source references:

- [Application structure](app-structure-vl.md)
- [Package structure](package-structure-vl.md)
- [Just pipeline conventions](vl-just-summary.md)

**Out of scope:** authoring or modifying VL patch contents, implementing application logic, generating publishing metadata or `.nuspec` files, remote publication, and inventing archive/installer formats. Optional `pack` integration may invoke an explicitly supplied existing packaging workflow.

## 2. Interactive workflow

Ask only questions relevant to the selected features; reuse shared answers rather than repeating them for every project.

1. Select repository kind, name, and destination.
2. Discover and select a vvvv installation, or accept an explicit installation path.
3. For an application, collect its entry-document path and optional additional launch/export targets.
4. Collect local packages: none/one/many for an application, one/many for a package repository. For each, collect its identity, root, document path, and whether it needs C# support.
5. Collect application-local/helper C# libraries: none/one/many. For every C# project, resolve its role, names, references, framework, and output settings.
6. If C# is present, select solution generation and shared dependency-version settings.
7. Select optional folders (`assets`, `rnd`, `vl`, `resources`, `sandbox`, `docs`; per-package `help`/`assets`).
8. Select pipeline backend: `none`, `just`, or `cmd`. When enabled, configure targets, prerequisite projects, package locations, and any export/staging or existing pack operation.
9. Validate and preview the resolved layout, files to create, commands, and unresolved user-authored documents. Write only after confirmation.

Explicit command arguments must be able to provide the same inputs without prompts. Noninteractive execution must fail with a list of missing required values instead of guessing. Exact command names, argument spellings, and any persisted configuration format are implementation details, not prescribed here.

## 3. Parameter model

Use one resolved model for interactive input, explicit arguments, project generation, and either pipeline backend. Model packages, C# projects, and runnable/build targets as separate collections linked by stable identifiers.

### Repository and environment

| Parameter | Requirement / default |
|---|---|
| Repository kind | `application` or `package`; package count determines single- versus multi-package layout. |
| Repository name and destination | Explicit name and resolved root; validate Windows path/name rules and existing-file collisions. |
| Pipeline backend | Explicit `none`, `just`, or `cmd`; `none` creates no pipeline files and requires no Just installation. |
| vvvv installation | Selected directory; derive `vvvv.exe` and `vvvvc.exe` from the same installation. Preserve a runtime override such as `VVVV_DIR`. |
| .NET executable | Resolve the required SDK/architecture when C# builds are enabled; allow an explicit executable override. |
| Solution | Optional when C# projects exist; name and format are explicit. Prefer `.slnx` only with a supporting SDK; preserve existing formats. |
| Dependency versions | Exact shared VL version and any selected feature versions; do not copy example versions or infer them solely from the vvvv directory name. |
| vvvv package cache | Opt-in (default none); when enabled, `..\__nugets` relative to the repository root, created by the recipes before use; configurable independently of package sources. |
| Local package feed | Opt-in (default none); when enabled, `..\__sources`, created by the recipes before export; only asked when an entry document can be exported. |
| C# restore sources | Optional explicit source configuration; independent from vvvv's cache and repository paths. |

### Local packages

Each package has its own:

- Package ID, root, and package-document path; default `<PackageId>\<PackageId>.vl`.
- Optional supplied document to copy unchanged, or a document explicitly marked as still to be authored.
- Optional C# wrapper-project identifier; VL-only packages have no wrapper project or library-build prerequisite.
- Optional help, shader, and other runtime-asset locations.
- Optional development/verification target pointing to an existing help/example/test patch. A package document is not automatically such a target.

Application repositories may place packages directly under the root or under a configured container such as `repos\`. Standalone package repositories default to sibling `<PackageId>\` roots. The resolved package search directories must match the selected layout.

### C# projects

| Parameter | Requirement |
|---|---|
| Project identity and role | Unique identifier; application library, helper library, or package wrapper. A wrapper links to its owning package. |
| Names | Project name, assembly name, valid root namespace, and optional VL import namespace/category. |
| Location | Project directory/path and output base; relative to the repository wherever possible. |
| Target and features | Target framework and only required features/dependencies, such as Windows-specific APIs or Stride support. |
| References | Explicit local project identifiers and external package references with versions/shared properties. |
| VL binding | Known direct-project or assembly binding, or no direct binding for an internal helper. Do not infer patch references from the C# graph. |
| Build inputs | Default configuration and expected assembly/symbol paths; pipeline targets may require different explicit inputs. |

All authored projects are class libraries. Detect duplicate identities, invalid identifiers, missing references, dependency cycles, and conflicting output paths before generation.

### Pipeline targets

Each target resolves:

- Unique target name and application/package ownership.
- Entry patch for editor launch/export, with an explicit pending state allowed for a document not yet supplied.
- Required local project set, including patch-only prerequisites outside the C# reference graph.
- Required build configuration/framework and known DLL paths; do not equate export configuration with prerequisite-library configuration.
- Local package repositories, narrow editable-package names/patterns, and optional editor flags.
- Build operation: application/verification-host export, explicitly library-only, or unavailable. Do not invent an export for a package without a verification host.
- For export: output root, expected application output location, executable type, runtime/platform, optional icon, asset behavior, and whether to include the local feed in generated NuGet configuration.
- For staging: source/destination mappings, required versus optional inputs, and approved runtime configuration.
- For optional pack: existing command/mechanism, prerequisite inputs, intended deliverable, and destination.

Shared settings may supply target defaults, but target overrides must resolve consistently for both backends. Keep executable paths and argument lists separate rather than concatenating unquoted command strings.

## 4. Toolchain discovery and compatibility

- Discover vvvv installations under `C:\Program Files\vvvv\vvvv_gamma_*` and Windows uninstall registry entries under `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` with matching `vvvv_gamma_` keys. Deduplicate candidates and validate their actual installation paths.
- Allow a manually supplied installation when discovery finds none or the desired version is elsewhere. Never silently choose a different host.
- Verify the editor exists for development and the compiler exists when export is enabled. Resolve tool paths once rather than embedding installation paths throughout generated files.
- Determine compatible frameworks and VL dependency versions from verified installation metadata or a supported compatibility mapping. If unresolved, require explicit values and report compatibility as unverified.
- `net8.0` and `net8.0-windows` are observed examples, not permanent defaults for every host. Windows Forms, runtime identifiers, and x64 requirements are conditional.
- Validate SDK architecture and solution-format support. Check the installed Just version and compiler help when validating recipes; do not assume flags or module syntax from another version.
- Scaffolding must not install tools or populate shared caches/feeds automatically. Report missing prerequisites with actionable instructions.

## 5. Layout rules

### Applications

```text
<Repository>\
|-- <AppName>.vl                        Supplied/user-authored entry, not generated XML
|-- <SolutionName>.slnx                 Optional authored solution
|-- Directory.Packages.props            Optional shared version properties
|-- src\<LibraryName>.csproj            Single application-local library
|-- assets\.gitkeep                     Only when an assets folder is requested
|-- <PackageContainer>\<PackageId>\     Optional local packages
`-- <pipeline files>                    Only for the selected backend
```

For multiple application-local/helper libraries, default to separate `<ProjectName>\src\<ProjectName>.csproj` roots with project-local `lib\` outputs. Package wrappers retain their package layout regardless of the number of other projects. A single application-local library under root `src\` uses root `lib\`.

Choose the layout before generation; adding packages must not silently relocate existing projects. Explicit locations and existing intentional layouts take precedence over new-project defaults.

### Single or multiple packages

```text
<Repository>\
|-- <SolutionName>.slnx                 Optional; includes authored C# projects
|-- Directory.Packages.props            Optional shared version properties
|-- <PackageA>\
|   |-- <PackageA>.vl                  Supplied/user-authored package document
|   |-- src\<PackageA>.csproj          Only when C# support is enabled
|   `-- help\                         Optional user-authored examples
|-- <PackageB>\                        Additional independent package, if selected
`-- <pipeline files>
```

Generate only selected source/configuration files. Builds create `lib\<Configuration>\<TFM>\`, project `obj\`, and any application export `bin\`; never seed these with sample binaries or compiler-generated projects.

## 6. Project and document generation

- Application-library names must be distinct from known accompanying VL document names. A direct `.csproj` reference with the same name as a VL document causes a collision; validate names case-insensitively on Windows. `MyApp.vl` and `MyApp.Core.csproj` illustrate a valid distinction.
- A package wrapper may share its package document's name when imported as an assembly through `lib`, not through a direct `.csproj` reference.
- Set `BaseOutputPath` relative to each project so the resolved outputs match the chosen layout. Default new builds to configuration/framework-separated outputs; preserve an explicitly requested existing override.
- Use `ProjectReference` for local source dependencies and `PackageReference` for external packages. Only add dependencies required by the selected features.
- When shared version properties are selected, generate `Directory.Packages.props` and matching project references consistently. Do not enable NuGet Central Package Management merely because that filename is used; preserve an existing version-management approach.
- Generate VL import metadata only for libraries exposing VL APIs, using a template verified against the selected VL dependency version. Internal helper libraries need not expose nodes.
- Stride descriptors, shader-copy steps, launch profiles, and application `.props` are conditional. Keep application export settings separate from authored C# library settings and avoid conflicting copies of the same setting.
- Treat `.vl` documents as opaque. Never inspect, generate, or rewrite their contents; copy supplied documents only when requested and preserve them byte-for-byte.
- When no entry/package/help document is supplied, create no empty placeholder `.vl`. Report its intended path and the required authoring/import steps in vvvv. The scaffold can be generated, but affected dev/export operations must fail clearly until the document exists.

## 7. Pipeline generation

### Backends and public operations

| Backend | Generated surface |
|---|---|
| `none` | Project/repository files only; no pipeline scripts. |
| `just` | `.justfile`; optional `.just\common.just`, `dev.just`, `build.just`, and `pack.just` when modularization is useful. Public operations: `just dev`, `just build`, and configured `just pack`. |
| `cmd` | `_dev.cmd`, `_build.cmd`, and `_pack.cmd` only when configured. No Just dependency. |

Both generated backends must support named targets for multi-target repositories. A default target must be explicit; do not select the first package/application arbitrarily. Emit only applicable operations, with unavailable operations explained in the generated usage documentation.

| Operation | Required behavior |
|---|---|
| `dev` | Build required C# prerequisites, verify required outputs, then open the target patch using `vvvv.exe --open`. No export prerequisite. |
| Export `build` | Build required C# prerequisites, invoke `vvvvc.exe`, stage required assets/runtime files, and verify the actual application output. |
| Library-only `build` | Build and verify the declared library outputs; report explicitly that no VL application was exported. |
| Optional `pack` | Obtain the exact required library/package inputs or completed application exports, then invoke the supplied packing mechanism and verify its deliverable. |

- Use the configured DLL paths to choose library build configuration; do not automatically change Debug bindings to Release. Package development requires usable matching symbols.
- Supply `--nuget-path`, actual local package repositories, and relevant editable patterns consistently. Apply editor-only flags only to the editor. Launching vvvv is not debugger attachment.
- Resolve all paths from the repository root, independent of the caller's working directory. Anchor CMD scripts to their location; use PowerShell explicitly in Just Windows recipes.
- Preserve spaces, semicolon-separated lists, and argument boundaries in both backends. Shell continuation characters must not leak into executable launch-profile arguments.
- For executable export, verify the actual `<output-root>\<AppName>\<AppName>.exe` location rather than assuming an executable directly in the export root.
- Stage assets without flattening paths or nesting `assets\assets`. Preserve the configured asset mode; compiler success alone does not prove assets or runtime files are present.
- `--export-package-sources` adds feed configuration, not packages. Cache/feed creation or local feed writes require an explicit operation; neither shared directory may be cleaned.
- No implicit remote publication, credentials staging, administrative elevation, ignored compiler errors, services, watch loops, archives, or installers.

## 8. Safety, validation, and completion

- Validate the complete generation plan before writing. Provide a preview/dry-run that creates nothing and executes no builds, launches, exports, or packing commands.
- Keep generated files within the chosen destination. External tool/cache/feed/input locations may be referenced but must not be overwritten by scaffolding.
- Never silently overwrite existing content. Repeated generation must preserve identical files and report conflicting changes for explicit resolution; do not rewrite VL documents under any overwrite option.
- On write or validation failure, report what was created and what remains incomplete. Do not leave an apparent success or use broad cleanup against pre-existing user files.
- Propagate native command failures before dependent steps. PowerShell must check `$LASTEXITCODE`; CMD must check exit status. Handle `robocopy` codes below 8 as nonfatal and codes 8 or above as failures.
- Prefer compiler-supported cleanup; any manual cleanup must target a verified target-specific generated directory, never repository roots or shared package locations.
- Validate generated C# projects with focused builds and verify configured assembly paths. Build/restore execution must be explicit; when skipped or unavailable, report the scaffold as not build-validated.
- Validate Just syntax/public targets without executing recipes. Test CMD argument handling and failure propagation without requiring GUI launch.
- Emit a concise repository README describing layout, parameters/overrides, available commands, expected outputs, prerequisites, and pending user-authored documents. Do not imply a successful C# build verifies VL graph behavior.
- Generation must not automatically launch the editor, export an application, or pack/publish. Report file generation, C# validation, pipeline validation, and host/runtime validation as separate outcomes.

## 9. Acceptance scenarios

| Scenario | Expected result |
|---|---|
| VL-only application, backend `none` | No C# project, solution, library output, or pipeline files; intended entry path and pending authoring are documented. |
| Application with one library | Root `src\` project and root `lib\` output base; library name distinct from the entry document. |
| Application with multiple libraries/packages | Independent roots, correct solution/project references and package search paths; no output/name collisions. |
| One VL-only package | Package root and selected content folders; no fabricated `.csproj`, binaries, or verification patch. |
| Multiple mixed packages | Each package independently supports VL-only/C# content, dependencies, and applicable named targets. |
| Package without a verification host | Explicit library-only build when C# exists; no fabricated export or runnable package-document assumption. |
| Just and CMD parity | Same resolved targets, prerequisites, flags, output paths, staging, and failure behavior; CMD runs without Just. |
| Spaces and alternate working directory | Commands resolve the same repository/entry paths and preserve list arguments. |
| Missing entry or failed C# prerequisite | Affected operation fails before editor/compiler launch; no success-shaped continuation. |
| Known Debug DLL binding during export | Prerequisite build updates and verifies that Debug path even if application export uses different settings. |
| Missing required asset | Build fails explicitly; declared optional assets may be skipped. |
| Existing files and repeated generation | Supplied `.vl` bytes and unrelated files remain unchanged; conflicting writes are reported. |
| Missing/incompatible toolchain | Actionable failure or explicitly unverified generation state; no silent host/SDK substitution. |
| Optional existing pack workflow | Correct inputs and deliverable are verified; no manifest generation or implicit publication. |

Use controlled fixtures for generation and backend tests, and an explicitly enabled integration run for the selected vvvv toolchain. Keep generated scaffolds, validated C# builds, and verified VL runtime behavior distinguishable in test results.
