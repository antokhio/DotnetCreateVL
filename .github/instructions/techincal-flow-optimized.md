# Technical flow: console-optimized Q&A

Interactive flow for the [VL scaffolding requirements](techincal-requirements.md), redesigned for a `dotnet tool` console experience (ConsoleAppFramework + Spectre.Console). It supersedes the question script in [technical-flow-plan.md](../excluded/technical-flow-plan.md); an ID mapping is in section 17.

## 1. Design principles

1. **Slot-driven, not script-driven.** Every prompt fills one field ("slot") of the resolved model. A slot is asked only when it is *applicable* and *unresolved*. CLI flags, environment hints, and prompts fill the same slot and pass the same validator. Noninteractive mode is simply "prompting disabled".
2. **Short common path.** Five prompts reach a valid VL-only scaffold. Everything else is reached through add-loops ("Add another …?") and **summary cards** (`Use these settings` / `Customize…`), never through a wall of sequential questions.
3. **Discover first, ask second.** All toolchain discovery runs once, silently, before the first prompt, so every list contains real values.
4. **Identities before references.** Entries, packages, and projects are registered in their own phases; reference/prerequisite prompts appear only after every identity exists.
5. **Validate continuously, block in place.** Each answer is validated on entry; cross-item issues (collisions, cycles, missing references) are reported by the phase that owns them and stop the run (exit `3`), so the final review has nothing left to fix and asks a single question.
6. **Scrollable transcript.** Never clear the screen. Each phase prints a header (`[3/9] Packages`; nine user-visible phases, Basics through Review) and ends with one summary line (`✓ Packages: VL.Shapes (C#), VL.Patterns (VL-only)`), so the terminal history is itself the running preview.
7. **Suggestions are visible and editable; facts are never inferred.** Defaults appear in brackets with resolved absolute paths. No discovered toolchain and no first list item is ever selected implicitly, and the destination must be new or empty so nothing on disk is read, merged, or preserved.

## 2. Command surface

The tool packs as `dotnet-create-vl` (see `ToolCommandName` in the csproj), so the SDK resolves `dotnet create-vl` to it with no risk of colliding with a future built-in `dotnet create` verb. The scaffolder is the root ConsoleAppFramework command:

```text
dotnet create-vl [options]          interactive when a TTY is present and slots are unresolved
dotnet create-vl --help | --version
```

| Option | Meaning |
|---|---|
| `--dry-run` | Run every phase and print the review tree; write nothing; exit 0. |
| `--yes` | Required for generation without a TTY; in a TTY it skips the final `Generate?` question. |
| `--no-input` | Disable prompting even with a TTY (section 15). |
| `--verbose` | Print discovery details and resolved slot sources (flag / prompt / default). |

Slot flags follow ConsoleAppFramework conventions: names are `--lower-kebab-case`, `bool` options are flags, enums parse case-insensitively, and **list-valued options take one comma-separated value** (`--package VL.Shapes,VL.Patterns`) rather than repeated flags. Items with sub-fields use `Id:root` / `Name=value` syntax as shown in the slot tables. Exact flag spellings in this document are proposals until bound in code.

## 3. Prompt primitives and console behavior

| Primitive | Spectre mapping | Rules |
|---|---|---|
| `Select` | `SelectionPrompt<T>` | Single choice. Required answers have no preselection. May include `Enter … manually`, `← Back`, and `Cancel` items. |
| `MultiSelect` | `MultiSelectionPrompt<T>` | Zero or more. Nothing preselected unless the text says "suggested". Includes `None` when applicable. |
| `Text` / `Path` | `TextPrompt<string>` | Shows `[default]`; empty input accepts the default. Validators re-prompt with a one-line reason. `Path` normalizes, rejects invalid Windows names, and prints the resolved absolute path. |
| `Confirm` | `ConfirmationPrompt` | Default shown as `(y/N)` or `(Y/n)`. |
| `Card` | `Panel`/`Table` + `Select` | Shows a group of resolved values, then `Use these settings` / `Customize…`. Customize opens a `MultiSelect` of fields to change; only chosen fields are prompted. |
| `Loop` | `Confirm` + item prompts | `Add a <thing>?` → item prompts → `Add another <thing>?` (default No). Mandatory first items skip the opening question. Each item ends with a one-line summary. |
| `Review` | `Tree` + `Confirm` | Destination tree and `Generate? (Y/n)`; see section 13. |

- **Cancel:** `Ctrl+C` exits immediately with code 130; a `Cancel` menu item asks `Discard and exit? (y/N)`. In both cases nothing is written.
- **Back:** `← Back` in `Select` prompts returns to the previous slot of the same phase. There is no cross-phase editing: the review only confirms; to change an earlier answer, decline and rerun (flags make that a one-liner). After a name, path, host, or feature changes, dependent slots are revalidated and re-asked only if they became invalid or inapplicable.
- **Status:** discovery and validation run under a `Status` spinner; results are printed, not hidden.
- **Non-TTY:** when `Console.IsInputRedirected`, `--no-input` is given, or no TTY is present, prompting is disabled and section 15 applies.

## 4. Phase 0: Discovery (no prompts)

Runs once under a spinner. Results populate choice lists and card defaults; they never auto-fill a required slot.

| Probe | Source | Feeds |
|---|---|---|
| vvvv installations | `C:\Program Files\vvvv\vvvv_gamma_*`; `HKLM\…\Uninstall\vvvv_gamma_*`; `VVVV_DIR` (marked *hint*) | `vvvv.install` list; editor/compiler presence per candidate |
| .NET SDKs | `dotnet --list-sdks` on PATH, architecture check | `csharp.sdk` list; `.slnx` support |
| Just | `just --version` on PATH | pipeline validation availability |
| Working directory | `Environment.CurrentDirectory` | `repo.destination` default |
| Destination contents | existing files at the candidate destination | empty-destination check (section 5) |

Printed as a short table, including warnings such as `just not found: Just recipe validation will be skipped unless supplied`.

## 5. Phase 1: Basics

| Slot | Prompt | Asked when | Default / choices | Validation | CLI |
|---|---|---|---|---|---|
| `repo.kind` | Select | always | `Application`, `Package repository` (required) | — | `--kind app\|package` |
| `repo.name` | Text | always | required | valid Windows folder name | `--name` |
| `repo.destination` | Path | always | `<cwd>\<name>` shown absolute | valid path; must not exist or must be empty (prompt re-asks; flag/default value is a blocking error, exit `3`) | `--output` |
| `vvvv.install` | Select | always, even with one candidate | discovered `version — path` items; `VVVV_DIR` hint item first if present; `Enter installation path…` | directory exists; `vvvv.exe` presence reported; `vvvvc.exe` presence reported | `--vvvv-dir` |
| `vvvv.path` | Path | manual choice only | — | as above | (same) |
| `pipeline.backend` | Select | always | `None`, `Just`, `CMD` (required) | `Just` without a detected Just prints a validation-unavailable warning, not a block | `--pipeline none\|just\|cmd` |

Summary line: `✓ Application "Gallery" → D:\Work\Gallery · vvvv 7.1 · pipeline: CMD`.

Asking the backend here lets the tool announce which later phases apply and lets a noninteractive run fail early on missing pipeline inputs.

## 6. Phase 2: Documents and entries

### Generated documents

Every application entry and package document is written as the document vvvv itself creates for *File → New*: `LanguageVersion` and a `VL.CoreLib` `NugetDependency` taken from the selected installation (`csharp.vlVersion` when Phase 5 ran, else the install's `VL.Core.nuspec`, else its short version), and one `Application` process node with empty `Create`/`Update` patches. Every `Id` is a fresh 22-character VL id. No further dependencies (local packages, C# projects) are declared; the user wires those in vvvv. Because the destination is empty, the question of existing documents never arises and the user is never asked about document sources.

### Application entry (`repo.kind = Application`)

| Slot | Prompt | Default | Validation | CLI |
|---|---|---|---|---|
| `app.name` | Text | `repo.name` | valid document name; may differ from any library name | `--app-name` |
| `app.entry.path` | Path | `<AppName>.vl` | repository-relative `.vl` | `--entry <path>` |

Summary: `✓ Entry: Gallery.vl`.

Only one entry is collected here. Additional launch/export patches (a render host, an editor tool, …) are pipeline targets: `Add target…` in Phase 7 asks for the target's own `.vl` path, so roles are never inferred from filenames and no entry registry is needed up front.

## 7. Phase 3: Local VL packages

Opening: application → `Does this application require an additional VL package (e.g. VL.Shapes)? (y/N)`; package repository → the loop starts immediately with the first package (at least one is required).

| Slot | Prompt | Default | Validation | CLI |
|---|---|---|---|---|
| `pkg[i].id` | Text `Package name (e.g. VL.Shapes):` | none | NuGet-style name, also a valid folder/document name; unique, case-insensitive | `--package <Name>` |
| `pkg[i].root` | Path `Folder for <Name>:` | `<container>\<Name>`; the container is `--package-container` or, after the first package, the parent folder of the previous package | repo-relative folder below the root; unique across packages | `--package <Name>:<folder>`, `--package-container repos` |
| `pkg[i].document` | automatic | `<folder>\<Name>.vl`, generated (see Phase 2) | must not be the application entry | — |
| `pkg[i].csharp` | Confirm `Add a C# project to <Name> (<folder>\src\<Name>.csproj)?` | No | registers project `<Name>` with role *package wrapper*, owner `pkg[i]`; never asked again | `--package-csharp <Name,…>` (when given, undecided flagged packages are VL-only) |
| loop | `Add another package?` | No | — | repeat `--package` |

Prompts stay one line; where vocabulary needs anchoring, an example goes inline in the question (`e.g. VL.Shapes`) rather than in extra description lines.

There is no separate layout question: accepting the root default gives "directly under the repository"; typing `repos\VL.Shapes` once makes `repos\` the default container for the rest. Per-item line: `• VL.Shapes → VL.Shapes\VL.Shapes.vl, C# wrapper`. Summary: `✓ Packages: VL.Shapes (C#), VL.Colors` or `✓ Packages: none`.

## 8. Phase 4: C# libraries

Package wrappers chosen in Phase 3 are already registered; this phase only adds free-standing libraries. Opening: application → `Does this application require a C# library (e.g. <AppName>.Core)? (y/N)`; package repository → `Does this repository require a shared C# library besides the package projects (e.g. <RepoName>.Common)? (y/N)`.

| Slot | Prompt | Default | Validation | CLI |
|---|---|---|---|---|
| `lib[i].name` | Text `Library name:` | application, first library: `<AppName>.Core`; otherwise none | dot-separated identifier; unique across projects; case-insensitively different from every `.vl` document name (application, entry, package ids) | `--lib <Name,…>` |
| `lib[i].role` | derived, never asked | application repository → *application library*; package repository → *helper library* | — | — |
| loop | `Add another library?` | No | — | repeat `--lib` |
| `project[i].path` / `project[i].output` | layout table + Confirm `Use this layout? (Y/n)`; on **n** `Project file for <Name>:` and `Output folder for <Name>:` per project | per the layout rules below | repo-relative `.csproj` / folder; no duplicate project files or output folders across projects | `--project-path <Name>=<csproj>`, `--output-base <Name>=<dir>` |

Layout defaults (wrappers listed first in the table):

| Arrangement | Project path | Output base |
|---|---|---|
| Single application library | `src\<Lib>.csproj` | `lib\` |
| Several libraries, or helper in a package repository | `<Lib>\src\<Lib>.csproj` | `<Lib>\lib\` |
| Package wrapper | `<PackageRoot>\src\<Id>.csproj` | `<PackageRoot>\lib\` |

No projects → grey note `No C# projects; the C# settings and project phases will be skipped.` Summary: `✓ C# projects: Gallery.Core → src\Gallery.Core.csproj, VL.Shapes → VL.Shapes\src\VL.Shapes.csproj` or `✓ C# projects: none`. Phases 5 and 6 are skipped entirely when no project is registered.

## 9. Phase 5: C# settings (one card)

Skipped entirely when Phase 4 registered no project. Defaults come from files the installation ships — `vvvv.runtimeconfig.json` (host TFM) and `packs\VL.Core\VL.Core.nuspec` (exact VL version) — never from the folder name. Slots that this metadata cannot resolve are asked **before** the table as plain prompts (`Target framework (e.g. net8.0):`, `VL version (e.g. 2025.7.4):`, `dotnet executable (x64, SDK 8.0+):`); everything resolved is shown once:

| Slot | Row | Default | Validation | CLI |
|---|---|---|---|---|
| `csharp.sdk` | `.NET SDK` `10.0.100 x64 (dotnet on PATH)` | dotnet on PATH when it is x64 and has an SDK ≥ the host .NET major; stable releases win over previews | an explicit executable is probed (`--info`): must run, be x64, carry a suitable SDK; no silent PATH fallback | `--dotnet <exe>` |
| `csharp.tfm` | `Target framework` `net8.0 (vvvv host)` | runtimeconfig `tfm`, else required | `netX.Y[-platform]` | `--tfm` |
| `csharp.vlVersion` | `VL version` `2025.7.4 (VL.Core.nuspec)` | nuspec version, else required | exact version | `--vl-version` |
| `csharp.configuration` | `Configuration` | `Debug` | `Debug` or `Release` | `--configuration` |
| `csharp.solution` | `Solution` `Gallery.slnx` | `<repo.name>` as `.slnx` with SDK ≥ 9.0.200, otherwise `.sln` | name without extension or `none`; `.slnx` on an older SDK is blocking | `--solution <name>\|none`, `--solution-format slnx\|sln` |
| `csharp.versions` | `Version management` | `shared (Directory.Packages.props)` | `shared`, `inline` (`keep` is still parsed but, with an empty destination, behaves like `inline`) | `--versions` |
| `csharp.sources` | `NuGet sources` `vvvv-public-feed` | `vvvv-public-feed` (`https://teamcity.vvvv.org/guestAuth/app/nuget/v1/FeedService.svc/`); written to `NuGet.config` in Phase 7 | `name=url-or-path`, comma-separated, appended to the baseline; `vvvv` is a shorthand for the feed; `none` drops everything; URLs with credentials refused | `--nuget-source` |

Then `Use these settings? (Y/n)`. On **n** a MultiSelect `Change which settings?` lists the seven rows; only the picked ones are prompted (`dotnet executable:`, `Target framework:`, `VL version:`, `Configuration:` select, `Solution (name or none):` + `Solution format:` select when the SDK supports `.slnx`, `Version management:` select, `Additional NuGet sources (e.g. local=D:\feed, empty for none):`) and the table is printed again. Summary: `✓ C#: net8.0 · VL 2025.7.4 · Debug · Gallery.slnx · SDK 10.0.100 x64`.

## 10. Phase 6: Project review

Skipped when Phase 4 registered no project. Nothing is asked up front: every slot has a role-derived default, shown once in a table (`Project | Role | VL binding | Framework | References | Packages`):

| Slot | Default | Validation | CLI |
|---|---|---|---|
| `proj[x].binding` | application library → `direct (.csproj)`, package project → `assembly (lib\)`, helper → `none` | a direct binding whose project name equals a VL document name (entry, package) is blocking | `--vl-binding <Name>=direct\|assembly\|none` |
| `proj[x].features` | none | `windows`, `winforms`, `stride`; `windows`/`winforms` require a `-windows` framework (blocking otherwise) | `--feature <Name>=<feature>` (repeat the name for several) |
| `proj[x].tfm` | Phase 5 framework, plus `-windows` when a feature needs it | `netX.Y[-platform]` | `--project-tfm <Name>=<tfm>` |
| `proj[x].references` | none | other registered projects only; self and unknown names are flag errors, cycles are blocking | `--ref <Name>=<Other>` (repeat the name for several) |
| `proj[x].packages` | none | `Id@Version`, exact version, no duplicate ids per project | `--package-ref <Name>=<Id>@<Version>` |

Then `Use these project settings? (Y/n)`. On **n**: `Edit project:` select (skipped with a single project) → MultiSelect `Change what for <Name>?` with the groups `VL binding` (select), `Framework & features` (features MultiSelect with the current ones preselected, then `Target framework for <Name>:` defaulting to the derived value), `Local references` (MultiSelect of the other projects, current ones preselected) and `NuGet packages` (`NuGet packages for <Name> (e.g. MessagePack@2.5.124, or none):`). The table is printed again and the question repeats. Assembly name and namespace are the project name; whether a project exposes VL follows from its binding. Summary: `✓ Projects: Gallery.Core (direct), VL.Shapes (assembly)`.

## 11. Phase 7: Repository files and content

Runs right after the project review. Two MultiSelects, applicable items only; the license questions appear only when LICENSE is ticked:

| Slot | Prompt | Default / choices | CLI |
|---|---|---|---|
| `content.repoFiles` | MultiSelect `Repository files:` | `[x] .gitignore` · `[x] .gitattributes` · `[x] NuGet.config` (C# projects) · `[ ] CSharpier (.csharpierrc + .config/dotnet-tools.json)` (C# projects) · `[ ] LICENSE` (package repositories) | `--repo-files gitignore,gitattributes,nuget-config,csharpier,license`; `--repo-files none`; inapplicable items are flag errors |
| `content.license` | Select `License:` `MIT` / `Apache-2.0` / `BSD-3-Clause` / `LGPL-3.0-or-later`, then Text `Copyright holder:` (default `git config user.name`, may be left empty) | only when LICENSE is ticked; `--license` alone also ticks it | `--license <SPDX>`, `--license-holder <name>` |
| `content.folders` | MultiSelect `Optional folders:` | applications: `[x] assets\` · `[ ] rnd\` · `[ ] vl\` · `[ ] resources\` · `[ ] sandbox\` · `[ ] docs\`; per local package: `[x] <root>\help\` · `[ ] <root>\assets\`. Labels are the paths only | `--folders assets,rnd,vl,resources,sandbox,docs,help:<Id>,assets:<Id>` (each `[=path]`); `--folders none`; noninteractive default = the preselected ones |

Paths are fixed in the prompt and overridable only through the flag; empty folders get `.gitkeep` at generation and carry no further semantics (`resources\icon.ico` is picked up by the export when present). Two folders on one path, or a path that is an existing file, are blocking. Nothing is copied into the repository and no further questions are asked. Summary: `✓ Content: .gitignore, .gitattributes, NuGet.config · assets\, VL.Shapes\help\`.

What each repository file contains (generated from the plan, not copied verbatim):

| File | Content | Existing file |
|---|---|---|
| `.gitignore` | embedded VisualStudio template (offline) plus a marked `# dotnet create-vl` block: every project `OutputBase` (`lib/`), `export/`, `bin/`, `obj/` | block appended if missing; template part not re-added |
| `.gitattributes` | `* text=auto` and `*.vl diff=html` | missing lines appended |
| `NuGet.config` | `packageSources` from Phase 5 `csharp.sources`; the vvvv public feed is the baseline entry; no `<clear/>` so nuget.org stays | sources merged by key, nothing removed |
| `.csharpierrc` + `.config/dotnet-tools.json` | `printWidth 100, useTabs false, indentSize 4, endOfLine auto`; manifest with `csharpier` (newest version resolvable offline is not attempted; version is a template constant reviewed per release) | manifest merged, existing tools kept |
| `LICENSE` | SPDX text with year and holder | never overwritten |

Deselecting `NuGet.config` while non-default sources were configured is a warning, not an error.

## 12. Phase 8: Pipeline (skipped when `pipeline.backend = None`)

Two tables, each confirmed once. Nothing else is asked up front except the two opt-in folder questions below; the vvvv executables, dotnet and configuration come from Phases 1 and 5 and are shown read-only.

### Shared folders (opt-in, default **n**)

The sibling folders `..\__nugets` and `..\__sources` live outside the repository, so they are never assumed. Interactively two confirms precede the settings table: `Use a shared nuget folder outside the repository (..\__nugets)? (y/N)` and — only when some target has an entry document to export — `Export package sources outside the repository (..\__sources)? (y/N)`. **y** fills the convention path, **n** (and `--no-input`) leaves the slot at `none`. The generated recipes create whichever folder is enabled before launching vvvv or exporting (`New-Item -ItemType Directory -Force` / `if not exist … mkdir`), so a missing folder never blocks the patch from opening.

### Settings table (`Use these pipeline settings? (Y/n)`)

| Slot | Default | Validation | CLI |
|---|---|---|---|
| `pipeline.nugetPath` | `none`; `..\__nugets` when opted in (vvvv `--nuget-path`) | folder (may leave the repository) or `none` | `--nuget-path` |
| `pipeline.exportSources` | `none`; `..\__sources` when opted in (vvvvc `--export-package-sources`) | folder or `none` | `--export-sources` |
| `pipeline.packageRepos` | the folders containing the local package roots (the repository root shows as `(repository root)`) | repository-relative folders or `none` | `--package-repo` |
| `pipeline.editable` | the local package ids | ids or `Prefix*`; bare wildcards refused | `--editable` |

On **n** a MultiSelect `Change which settings?` lists the four rows and only the picked ones are prompted (`nuget path (e.g. ..\__nugets, or none):`, `Export package sources (e.g. ..\__sources, or none):`, `Package repositories (e.g. repos, or none):`, `Editable packages (e.g. VL.Shapes, VL.My*, or none):`).

### Targets table (`Use these targets? (Y/n)`)

Targets are derived, never added or removed; names keep their case and are backend-safe (`Gallery` → `Gallery`, `VL.Shapes` → `Shapes`, `My App 2` → `My-App-2`, suffix `-2` on collisions):

| Owner | Target | Dev | Build | Prerequisites | Export |
|---|---|---|---|---|---|
| Application entry | `<app>` | on (opens the entry) | **export** | every registered project, references first | `bin\<App>` + staging (`assets` when Phase 7 selected it) |
| Package with a C# project | `<id without VL.>` | off | **libraries** | the package project plus its references | — |
| VL-only package | — | | | | |

On **n**: `Edit target:` (skipped with one target) → MultiSelect `Change what for <t>?` with `Dev command` (confirm), `Build operation` (select; export only with an entry), `Prerequisites` (MultiSelect of projects, current ones preselected, references expanded), `Export root` (text, default `bin`; vvvvc places the app in `<root>\<App>\` and its generated C# work folder in `<root>\src\`) and `Staging` (`Staging for <t> (e.g. assets, media:assets, or none):`; a staged source that neither exists nor is a Phase 7 folder is a warning). Groups that do not apply print a grey note instead of a prompt. Default target: the only target, otherwise `all` (`--default-target <name|all>`), not asked.

Blocking: export without an entry, export when the installation lacks `vvvvc.exe`, dev without `vvvv.exe`, a libraries build with no prerequisites, two export targets landing in the same `<root>\<App>` folder, or no target doing anything. No targets at all (package repository with VL-only packages) is a warning; the files then contain only the shared vvvv launch command.

Fixed at generation (not asked): `--output-type WinExe`, `--rid win-x64`, `--asset-behavior RelativeToOutput`, `--clean true`, `resources\icon.ico` as the app icon when present. The export goes straight into the export root (`<root>\<App>\` is removed first, staging is copied into it afterwards); there is no intermediate folder and no mirror copy.

CLI for targets: `--target-dev <t>=on|off`, `--target-build <t>=export|lib|none`, `--prereq <t>=<Project>` (repeat), `--export-root <t>=<dir>`, `--stage <t>=<src>[:<dst>]` (repeat). Summary: `✓ Pipeline (Just): Gallery (dev, export), Shapes (libraries) · default all`.

## 13. Phase 9: Review

Rule `[9/9] Review`, then one `Tree` of the destination and one question. Nothing else: every setting was already confirmed in its own phase, and blocking issues were reported there (exit `3`), so no issue panel, edit menu or validation plan is needed here.

The tree lists every file the generator knows about, folders first. Because the destination is empty, every entry is a green new file (`Create`); the planner also knows `Update`/`Keep` markers (yellow `(…)`, grey `(exists)`) but they cannot occur in a normal run. A counts line follows (`17 new`), then `? Generate? (Y/n)`.

- `y` → Phase 10. `n` → `Nothing was written.`, exit `0`.
- `--dry-run` → prints the tree and `Dry run. Nothing was written.`, exit `0`, never prompts.
- `--yes` → skips the question.
- No terminal and no `--yes` → `error: no terminal to confirm generation; pass --yes to generate or --dry-run to preview.`, exit `2`.

## 14. Phase 10: Generate and report

The plan is computed completely before anything is written, so the tree in Phase 9 is exactly what Phase 10 does.

1. **Folders**: package roots and the content folders of Phase 7; a `.gitkeep` is added to each content folder.
2. **VL documents**: the application entry and every package document, as described in Phase 2 (vvvv's new-document skeleton with the installation's `LanguageVersion` and `VL.CoreLib` dependency, fresh ids per document).
3. **Repository files** (per Phase 7 selection): `.gitignore` (compact .NET/vvvv template plus a `# dotnet create-vl` block with `bin/`, every export root and every project output folder), `.gitattributes` (`* text=auto`, `*.vl diff=html`), `NuGet.config`, `.csharpierrc` + `.config\dotnet-tools.json` (CSharpier 1.3.0), `LICENSE` (SPDX text with year and holder).
4. **C# projects**: `<Project>.csproj` (`Microsoft.NET.Sdk`, target framework, `BaseOutputPath` relative to the output folder, `UseWindowsForms` for WinForms, `VL.Core` when the project is reachable from VL, `VL.Stride` for the Stride feature, NuGet references, project references) and `Properties\AssemblyInfo.cs` with `[assembly: ImportAsIs]` for VL-reachable projects. Shared versions → `Directory.Packages.props` holding `<VLVersion>` and one `<IdVersion>` property per NuGet package (plain MSBuild properties, no Central Package Management). Solution: `.slnx` or a minimal `.sln`.
5. **Pipeline** (Phase 8 targets): Just → `.justfile` (shell = PowerShell, `mod dev`, `mod build`, `default` → the default target), `.just\dev.just` (one recipe per dev target — the default target first so a bare `just dev` opens it — then the bare `vvvv` launch, all passing `--nuget-path` when set, `--package-repositories`, `--editable-packages`, `--open`, `--debug` and forwarding extra args) and `.just\build.just` (`all`, one recipe per build target: `dotnet build` of the prerequisites in order, then `vvvvc` export with `--export-package-sources` when set, `--app-icon resources\icon.ico` when present, `--output-directory <export root>` so the app lands in `<root>\<App>\` next to vvvvc's `src\` work folder, staging copied into the app folder). Recipes create the opted-in `__nugets` / `__sources` folders before use and run from the repository root. Cmd → `_dev.cmd` / `_build.cmd`, suffixed `_<Target>` when several, plus `_build_all.cmd`.
6. `README.md`: layout and commands table.
7. Report: `✓ <n> files written to <destination>` and a `Command | Does` table. When Just is installed the written `.justfile` is checked with `just --list` (warning on failure). No C# build, no vvvv launch, no export.

## 15. Noninteractive contract

- Triggered by `--no-input` or a non-TTY. Same slots, defaults, applicability rules, and validators.
- Generation requires `--yes`; `--dry-run` prints the review tree and exits without it.
- Missing required slots → one error listing each slot, its flag, and allowed values; exit code `2`. Blocking issues → listed; exit code `3`. Missing `--yes` → `2`. Write failure → `1`. Success, declined and dry run → `0`. Cancelled by `Ctrl+C` → `130`.
- Repeatable identities (`--package`, `--lib`, `--entry`, `--target`, …) are comma-separated lists registered in order; reference flags are resolved after all identities exist, exactly as the interactive phases do.

## 16. Example sessions

### A. VL-only application, no pipeline (5 prompts + review)

```text
[1/9] Basics
? What are you creating?            Application
? Repository name:                  Gallery
? Destination [D:\Work\Gallery]:    ⏎
? vvvv installation:                7.1-0123 — C:\Program Files\vvvv\vvvv_gamma_7.1
? Pipeline files:                   None
✓ Application "Gallery" → D:\Work\Gallery · vvvv 7.1 · pipeline: None
[2/9] Entry
? Application name [Gallery]:       ⏎
? Entry patch [Gallery.vl]:         ⏎
✓ Entry: Gallery.vl
[3/9] Packages   ? Does this application require an additional VL package (e.g. VL.Shapes)? (y/N) ⏎
[4/9] Libraries  ? Does this application require a C# library (e.g. Gallery.Core)? (y/N) ⏎
[5/9]–[6/9]      skipped (no C# projects)
[7/9] Content    ? Repository files: [x] .gitignore [x] .gitattributes ⏎
                 ? Optional folders: [x] assets\ [ ] rnd\ [ ] vl\ [ ] resources\ [ ] sandbox\ [ ] docs\ ⏎
[8/9] Pipeline   skipped (None)
[9/9] Review
D:\Work\Gallery\
├── assets\
│   └── .gitkeep
├── .gitattributes
├── .gitignore
├── Gallery.vl
└── README.md
5 new
? Generate? (Y/n) ⏎
✓ 5 files written to D:\Work\Gallery
```

### B. Application with a library and a package, CMD pipeline

```text
Basics:    Application "Gallery", vvvv 7.1, pipeline CMD
Entry:     Gallery.vl
Packages:  VL.Shapes (root VL.Shapes, C# wrapper: y)
Libraries: Gallery.Core (role derived: application library) · `Use this layout?` y (VL.Shapes\src, VL.Shapes\lib ; src\, lib\)
C# card:   SDK 10.0.100 x64 (dotnet on PATH) · net8.0 (vvvv host) · VL 2025.7.4 (VL.Core.nuspec) · Debug · Gallery.slnx · shared · Use these settings? y
Projects:  table (Gallery.Core direct · VL.Shapes assembly) · Use these project settings? n → Gallery.Core → Local references → [x] VL.Shapes · y
Pipeline:  shared nuget folder (..\__nugets)? y · export sources (..\__sources)? ⏎ (n) ·
           settings table (nuget ..\__nugets · sources none · repos (repository root) · editable VL.Shapes) y ·
           targets table: Gallery (dev opens Gallery.vl, export, prereqs VL.Shapes, Gallery.Core, bin\Gallery + assets) · Shapes (libraries, VL.Shapes) y · default all
Review:    tree (18 new) · Generate? y · commands: _dev.cmd, _build_Gallery.cmd, _build_Shapes.cmd, _build_all.cmd
```

### C. Multi-package repository, Just pipeline

```text
Basics:    Package repository "Shapes", vvvv 7.1, pipeline Just (just 1.x found)
Packages:  VL.Shapes (C# wrapper) · VL.Patterns (VL-only)
Libraries: Shapes.Core (helper, no direct VL binding)
Projects:  table (VL.Shapes assembly · Shapes.Core none) · Use these project settings? n → VL.Shapes → Local references → [x] Shapes.Core · y
Pipeline:  shared nuget folder? ⏎ (n; no entry document, so export sources is not asked) · settings table y · targets table: Shapes (libraries, prereqs Shapes.Core, VL.Shapes); VL.Patterns: no target · y · default Shapes
Review:    tree (… new, incl. VL.Shapes\VL.Shapes.vl and VL.Patterns\VL.Patterns.vl) · Generate? y · `just --list` passes; commands: just dev vvvv, just build Shapes
```

## 17. Mapping from the previous flow

| Previous IDs | Replaced by |
|---|---|
| R1–R5, W1 | Phase 1 slots (`repo.*`, `vvvv.*`, `pipeline.backend`) |
| D1–D2 | removed: every document is generated as vvvv's new-document skeleton into an empty destination; nothing is copied or asked |
| A1–A4 | Phase 2 single entry; additional launch patches are Phase 7 targets |
| P1–P7 | Phase 3 package loop (`Add another?` instead of counts; no layout question, roots default from the previous package) |
| C1–C5 | Phase 4 library loop (role derived from the repository kind) + layout table with `Use this layout?` |
| C6–C12 | Phase 5 C# settings card |
| C13–C18 | Phase 6 project table + per-project edit groups |
| F1–F4 | Phase 7: `Repository files:` MultiSelect (.gitignore, .gitattributes, NuGet.config, CSharpier, LICENSE) + `Optional folders:` MultiSelect; copying and known-document questions removed |
| W2–W5 | Phase 8 settings table (nuget path, export sources, package repositories, editable); launch profiles removed |
| T1–T9, E1–E6 | Phase 8 derived targets table + per-target edit groups (dev, build, prerequisites, export root, staging); verification patches, extra entries, overrides, preflight and export options removed (fixed at generation) |
| K1–K5 | removed: packaging is the export target itself (export root → `bin\`) |
| Blocking-issue table, V1–V2 | removed: blocking issues stop the owning phase (exit `3`); Phase 9 is a tree plus `Generate? (Y/n)`, `--dry-run` replaces `Preview only`, no build-after-generate question (the generated `just build` / `_build.cmd` is the build) |

