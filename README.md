# dotnet create-vl

Creates a ready-to-use **vvvv gamma** project folder for you — the patch, the folder layout, Git housekeeping files, optional C# libraries and one-command scripts to open the patch in vvvv or export it as an application.

You answer a few questions in the terminal, review what will be written, and get a folder like this:

```text
MyApp\
├── MyApp.vl          your patch (opens in vvvv)
├── assets\           images, fonts, data… copied next to the exported app
├── .justfile         `just dev` opens the patch · `just build` exports it
├── .gitignore, .gitattributes, NuGet.config, LICENSE, README.md
└── src\, lib\        only when you add C# libraries
```

Nothing is installed, downloaded or compiled while the folder is created. The tool never starts vvvv for you.

## What you need

- Windows 10/11
- [vvvv gamma](https://visualprogramming.net) installed (the tool finds installations under `C:\Program Files\vvvv` and in the Windows app list)
- the [.NET SDK](https://dotnet.microsoft.com/download) 10 or newer — it provides the `dotnet` command
- optional: [just](https://just.systems) if you pick the `just` scripts (plain `.cmd` scripts are the alternative and need nothing extra)

## Install

```powershell
dotnet tool install -g DotnetCreateVl
```

Update later with `dotnet tool update -g DotnetCreateVl`. If the package is not on nuget.org yet, see [Build from source](#build-from-source) below.

## Use

Open a terminal in the folder where the new project folder should be created and run:

```powershell
dotnet create-vl
```

Then answer the questions. Pressing **Enter** accepts the suggested value shown in parentheses.

| Question | Meaning |
|---|---|
| What are you creating? | **Application** — a patch you run and export. **Package repository** — reusable VL packages for other projects. |
| Repository name / Destination | Folder name and where it goes. The folder must be new or empty. |
| Which vvvv installation…? | The vvvv version the project is made for. Pick one from the list or choose *Enter installation path…*. |
| Pipeline files | `just`, `cmd` or none — the scripts that open and export the patch. |
| Application name / Entry patch | Name of the app and of its `.vl` file (default `<Name>.vl`). |
| Does this application require an additional VL package? | Default **n**. Say **y** only if you want to develop a reusable `VL.Something` package alongside the app. |
| Add another library? | Default **n**. C# projects that vvvv can use directly; only for people writing C#. |
| Repository files / Optional folders / License | Which housekeeping files and folders to create. `assets` is on by default. |
| Use a shared nuget folder outside the repository (..\\__nugets)? | Default **n**. Keeps vvvv's downloaded packages in a folder next to the repository so several projects share one download. |
| Use these pipeline settings? / Use these targets? | Summary tables; **Enter** accepts, **n** lets you change single rows. |
| Generate? | Shows every file that will be written. **Enter** writes them. |

### Afterwards

Inside the new folder:

```powershell
just dev            # open the patch in vvvv (or: _dev.cmd)
just build          # export the application to bin\<App>\ (or: _build.cmd)
just --list dev     # all available commands
```

The export lands in `bin\<App>\<App>.exe` together with the `assets` folder. `bin\src\` next to it is vvvv's temporary build folder and can be deleted at any time. Both `bin\` and `src\`/`lib\` build output are ignored by Git.

### Without questions

Every answer has a command-line flag, so the whole thing can be scripted:

```powershell
dotnet create-vl --help
dotnet create-vl --no-input --yes --kind app --name MyApp --vvvv-dir "C:\Program Files\vvvv\vvvv_gamma_7.4-win-x64" --pipeline just
dotnet create-vl --no-input --dry-run ...     # only show what would be written
```

With `--no-input`, anything the tool cannot work out is listed as missing — it never guesses.

## Troubleshooting

- **"No vvvv installation found"** — install vvvv gamma, or pass `--vvvv-dir "C:\path\to\vvvv_gamma_…"`.
- **vvvv opens but without the patch** — a vvvv instance was already running; close it or run the command again.
- **"The destination already contains files"** — the tool only writes into new or empty folders. Pick another name or `--output` path.
- **`just` is not recognised** — install [just](https://just.systems) or create the project with `--pipeline cmd`.

## Build from source

For contributors, or if the package is not published yet:

```powershell
dotnet test
dotnet pack DotnetCreateVL\DotnetCreateVL.csproj -o nupkg
dotnet tool uninstall -g DotnetCreateVl 2>$null
dotnet tool install -g DotnetCreateVl --add-source .\nupkg
```

`dotnet tool update` does not pick up a rebuilt package with an unchanged version; uninstall and install again, or bump `Version` in the project file.

Design notes live in [.github/instructions](.github/instructions): [requirements](.github/instructions/techincal-requirements.md) and the [console Q&A flow](.github/instructions/techincal-flow-optimized.md), which documents every phase, prompt, flag and generated file.

## License

[MIT](LICENSE) — free to use, copy, change and redistribute; provided as is, without warranty.
