# VL Just pipeline summary

Condensed from the [vvvv Just skill](vl-just-skill.md). Describes the expected workflows, shared conventions, and project-specific parameters for VL repositories. Packaging is covered as a pipeline responsibility; manifest authoring and publishing procedures are outside this summary.

## Pipeline model

| Target kind | `dev` | `build` | Optional `pack` |
|---|---|---|---|
| VL package / plugin | Build required C# libraries, then open an existing help/example/test patch in vvvv with the local package editable. | Build prerequisites and export an existing verification host when one is part of the build contract. | Produce the package through its established packing mechanism. |
| VL application | Build required C# libraries, then open the application entry patch in vvvv. | Build prerequisites, export the application, stage required assets/runtime files, and verify the output. | Assemble completed application outputs into a separately requested distribution. |

```text
dev   -> required C# builds -> vvvv.exe --open <entry.vl>
build -> required C# builds -> vvvvc.exe <entry.vl> -> staging -> verification
pack  -> exact required build/package inputs -> distinct distributable
```

- `dev` opens source in the editor; it does not run an exported executable or depend on a full application export.
- Exporting a package's verification host does not create its NuGet package.
- A library-only package without a verification patch needs an explicit build contract; do not invent an application entry or describe a DLL build as an application export.
- A mixed repository can contain package and application targets. Keep their entries, prerequisites, outputs, and packaging responsibilities separate.
- Add `pack` only when it produces a deliverable distinct from `build`. It need not export an application for a package that only requires library outputs.

## Parameterization

Values can be shared across targets where appropriate; keep target-specific values separate. These are recipe inputs, not a prescribed interactive questionnaire or configuration-file schema.

| Parameter group | Values to resolve |
|---|---|
| Repository and targets | Repository root, target names, package/application kind, and each existing entry patch. |
| vvvv installation | Installation directory, preserving an override such as `VVVV_DIR`; derive both `vvvv.exe` and `vvvvc.exe` from it. |
| .NET toolchain | `dotnet` executable and required SDK architecture; PATH alone may select the wrong architecture. |
| C# prerequisites | Solution or explicit project set, build configuration, framework where needed, and expected DLL/symbol paths. Include libraries used by the patch but absent from another project's reference graph. |
| Shared package locations | Parent-level `__nugets` extracted cache and `__sources` package feed. They have different roles. |
| Local package discovery | Actual source package repositories and narrow editable package IDs/patterns. |
| Editor behavior | Entry patch plus existing required debugging, multiple-instance, or other project-specific flags. |
| Export behavior | Entry patch, export root, application output name/location, output type, runtime identifier, platform, optional icon, and asset mode. |
| Export package sources | Whether the parent `__sources` feed is required in the exported application's NuGet configuration. |
| Output staging | Source-to-destination mappings for assets, plugin outputs, and approved runtime configuration; which inputs are required or explicitly optional. |
| Packaging | Existing packing command/mechanism, exact input configuration/framework, intended deliverable, and destination. |
| Extra prerequisites | Only established service checks or other project-required steps; none are implied by unrelated examples. |

Do not copy installed versions, absolute developer paths, patch names, package patterns, or application-specific compiler flags into reusable recipes.

## Paths and recipe organization

The skill's shared-directory convention is:

```text
workspace-parent\
|-- __nugets\                 Extracted vvvv package cache
|-- __sources\                Local NuGet package feed
`-- repository\
    |-- .justfile             Public workflows
    `-- .just\                Optional modules for larger repositories
        |-- common.just       Shared paths/settings
        |-- dev.just          Editor launches
        |-- build.just        Exports and staging
        `-- pack.just         Only for a distinct packaging operation
```

- Keep a small project flat. Modules are useful for multiple targets or reusable steps, not mandatory.
- Resolve paths from the repository root, not the caller's working directory or a module's `.just\` directory.
- Use `__nugets` with `--nuget-path`; `.nupkg` files belong in a feed, not in the extracted cache as a substitute. Never clean either shared directory.
multiple targets can use commands such as `just dev server`. Verify the installed Just version's module/import syntax and scope.
- Just treats every word after a module name as a recipe inside that module, so a public `mod dev` rejects `just dev --allow-multiple`. Keep modules private (`[private] mod _dev`) and expose `dev *args` / `build *args` recipes that select a target when the first argument names one and otherwise forward all arguments to the default target.
- Use distinct private helpers for library compilation, editor launch, export, and staging. Both `dev` and `build` must invoke their C# prerequisites; exposing a manual library-build helper alone is insufficient.

## Command and output contracts

### C# prerequisites

Build the configuration that supplies the DLL paths actually referenced by the selected patch. Do not automatically choose Release for an export that consumes Debug assemblies. Debugging needs matching assemblies and symbols. Preserve established output layouts; missing required projects or unresolved project globs must fail explicitly.

### Editor launch

Launch `vvvv.exe --open <entry.vl>` with the shared cache, relevant source package repositories, editable-package patterns, and required project flags. Package development must exercise the local source package rather than accidentally use a cached installed copy.

Preserve established `--debug` settings where applicable, but do not treat launching vvvv as attaching a managed debugger. Keep any required debugger-attach workflow separate.

### Application export and staging

Invoke `vvvvc.exe <entry.vl>` with the selected output directory, package environment, and project-specific compiler options. Confirm available flags with the installed compiler's help; do not universally impose `WinExe`, `win-x64`, or one asset mode.

For executable exports, the skill describes `<output-directory>\<AppName>\<AppName>.exe`, not an executable directly inside the export root. Resolve and verify the actual application directory before copying files or running the result.

Stage required asset trees and runtime files into that directory without flattening their hierarchy or creating `assets\assets`. Keep staging consistent with the selected asset mode: `RelativeToOutput` can suit portable applications, while an existing `RelativeToDocument` setup must not be changed implicitly. Missing required sources fail the build; only declared optional inputs may be skipped.

Verify the executable and required staged files. Do not copy development credentials or `.env` files by default. An optional exported-app run helper uses the application directory as its working directory.

### Packaging boundary

Use the target's existing packing mechanism and exact prerequisite outputs. Use `--no-build` only when the required configuration/framework has already been built. Application packaging consumes completed, staged exports; archives and installers are not implicit.

`--export-package-sources` adds sources to generated NuGet configuration. It does **not** create, copy, or publish packages. Writing a package to the parent `__sources` feed must be an explicit configured operation, not a side effect of editor launch or export.

## Execution and validation rules

- Use PowerShell explicitly on Windows. Prefer argument arrays with `&`; preserve paths with spaces, semicolon-separated lists, and forwarded arguments as intact arguments.
- For asynchronous launches, `Start-Process -ArgumentList` builds a command line: an array alone does not preserve embedded spaces. Quote correctly, set the working directory, and report launch failures.
- Use `$ErrorActionPreference = 'Stop'` for script errors and check `$LASTEXITCODE` immediately after native commands. Stop dependent steps on failure; do not suppress compiler errors. For `robocopy`, exit codes below 8 are nonfatal and codes 8 or above are failures.
- Prefer compiler-supported cleanup. Manual cleanup must be limited to a verified, target-specific generated directory, never the repository or shared cache/feed.
- Check Just syntax, public targets, and prerequisite ordering using the installed version's listing/show/dry-run facilities. Syntax validation alone must not launch a GUI, export an application, or publish a package.
- When execution is requested, verify the actual result: required DLLs/symbols for development, executable and assets for application builds, and contents of the packaging deliverable. A successful prerequisite build is not proof that later stages work.
- Do not read, generate, or edit `.vl` contents. Obtain entry paths and known assembly bindings from existing metadata, documentation, or the user; report unresolved information instead of guessing.
