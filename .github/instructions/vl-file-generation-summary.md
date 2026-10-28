# VL file generation summary

Reference for emitting **new, explicitly requested `.vl` scaffold files** alongside C# projects and solutions. Existing user-authored documents remain unchanged; this is not permission to reconstruct application graphs or rewrite patches.

Source: [vvvv-fileformat skill](../skills/vvvv-fileformat/SKILL.md), attributed to Kopffarben, with declared license `CC-BY-SA-4.0`. This summary condenses its format guidance and distinguishes documented patterns from implementation gaps.

## Generation parameters

| Parameter | Meaning |
|---|---|
| Document role and path | Application entry, help/test application, or package/library facade; destination relative to the repository. |
| Language version | `Document.LanguageVersion`, resolved for the selected host, not copied from an example. |
| Dependencies | Explicit kind, location, optional version, and forwarding intent for each dependency. |
| Core library version | Compatible `VL.CoreLib` NuGet version; resolve separately from the XML format version. |
| Category | Root canvas `DefaultCategory`; the application helper uses `Main`. |
| Known assembly bindings | Exact project/library output paths and required configuration/framework. Keep these consistent with generated project and pipeline settings. |

IDs and lifecycle references are generated internally, not questionnaire fields. Do not infer dependency graphs from filenames, project proximity, or all installed packages.

## Common XML contract

- Write UTF-8 without BOM, with the XML declaration and one `Document` root.
- Declare `xmlns:p="property"`; `xmlns:r="reflection"` is needed when reflection-qualified values are used.
- The documented format uses `Version="0.128"`. This is not the application, NuGet, or vvvv installation version.
- Include exactly one top-level `Patch`. Its category canvas uses `CanvasType="FullCategory"`; an application body's canvas uses `CanvasType="Group"`.
- Dependencies belong directly under `Document`, never inside a `Patch`.
- Include a compatible `VL.CoreLib` dependency for the initial supported scaffold templates.
- XML names and attribute casing matter. Preserve the canonical property/reference representation; not every XML element has an entity ID.

### IDs

Use fresh, unique VL IDs for model entities such as documents, dependencies, patches, canvases, nodes, and fragments. Never reuse illustrative IDs from documentation.

VL IDs are 22-character encoded GUIDs, but **not ordinary base62 encoding of a 128-bit integer**. The bundled [ID implementation](../skills/vvvv-fileformat/scripts/vl-patch-manipulator/src/vl_patch_manipulator/ids.py) uses:

- Alphabet `ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789`.
- The byte order of .NET `Guid.ToByteArray()`.
- Two 8-byte chunks, each interpreted as a big-endian unsigned integer and encoded independently into 11 characters.

Use the matching VL encoder or the existing helper. Any .NET equivalent must match the supplied [encoding test vectors](../skills/vvvv-fileformat/scripts/vl-patch-manipulator/tests/test_framework.py); a length/character check alone is insufficient. Allocate IDs once, then reuse those exact values in references.

## Minimal application or help/test scaffold

The bundled [PatchSession.new implementation](../skills/vvvv-fileformat/scripts/vl-patch-manipulator/src/vl_patch_manipulator/session.py) creates:

```text
Document
|-- NugetDependency: VL.CoreLib
`-- Patch
    |-- Canvas: FullCategory
    `-- Node: Application
        |-- p:NodeReference: ContainerDefinition / Process, Primitive category
        `-- Patch
            |-- Canvas: Group
            |-- Patch: Create
            |-- Patch: Update
            `-- ProcessDefinition
                |-- Fragment: enabled, references Create patch ID
                `-- Fragment: enabled, references Update patch ID
```

Keep the entry node named `Application`; the filename can use the chosen application name. Both lifecycle patches and enabled fragments are mandatory even when empty. A Group canvas without this lifecycle can display in vvvv while providing no execution entry point.

Pass language and dependency versions explicitly: the helper currently defaults both to `2025.7.3`, while documentation examples use other versions. Neither is a universal scaffold default. Use the canonical implementation as the authoring reference; it currently uses `FullNameCategoryReference ID="Primitive"` for the Process category.

An empty scaffold does not need renderers, operation nodes, pins, links, or example behavior. If these are later added, their contracts must come from verified templates/catalogs rather than invented node surfaces.

## Package documents and dependency bindings

A package document is a library/aggregator: consumers use its definitions, and its `Application` node (present because vvvv saves the File → New skeleton) is not an entry point for them. Forwarding is explicit through `IsForward="true"` only where consumers should receive the dependency's public surface.

| Reference | Documented representation |
|---|---|
| NuGet package | `NugetDependency` with package ID in `Location` and a resolved version where specified. |
| Another VL document | `DocumentDependency` with its location relative to the owning document. |
| Compiled .NET assembly | `PlatformDependency`; use a verified assembly location matching the intended build output. |
| Registered node factory | `NodeFactoryDependency` with the exact registration identifier; only for a feature that actually uses it. |
| Direct `.csproj` binding | `ProjectDependency Location="./src/X.csproj"` (optionally `IsForward="true"`), seen in vvvv-saved application documents (e.g. LASERCUBE.vl); `PlatformDependency Location="./lib/Debug/net8.0/X.dll" IsForward="true"` is the binary form used by package documents. Verified by fixtures but not emitted by the tool (see below). |

The package name may match its wrapper project when the document imports the assembly through `lib`. Application libraries must still avoid the known same-name direct-project/document collision.

**Package documents in practice:** every vvvv-saved local package document inspected (VL.AppTodo, Hronomapper, VL.NURBS) is the same File → New skeleton — including the `Application` process node — plus its dependencies. There is no separate facade template; vvvv simply saves the new document under the package name. Consumers see the package's definitions, not its `Application` node.

## Generation and validation boundary

1. Resolve the document role, host-compatible versions, dependency kinds/paths, and supported template before writing. Fail explicitly for an unsupported binding/template.
2. Use the existing skill-owned authoring implementation where applicable. It is Python, not a .NET API; runtime integration or a verified .NET equivalent is a separate implementation decision.
3. Generate only at a new or empty destination; existing documents are never regenerated with new IDs.
4. Validate XML structure, namespaces, unique/valid IDs, dependency placement, and reference integrity. Application validation must require the lifecycle above; do not require an Application node in every library facade.
5. If links are present, require valid endpoint references; routed links can contain more than two IDs. For an empty scaffold, do not invent links to satisfy a validator.
6. Validate actual dependency resolution and application/package behavior in the selected vvvv host when execution is explicitly requested. File-only validation and C# build success are not live VL compilation.

## Implementation in the tool

`DotnetCreateVL/Generate/VlTemplates.cs` emits every application entry and package document; `VlId` is a .NET port of the ID encoder (verified against the test vectors above).

- **Template:** the File → New skeleton exactly as vvvv gamma 7.x saves it — `Document` (`xmlns:p="property"`, `xmlns:r="reflection"`, `Version="0.128"`), one `NugetDependency Location="VL.CoreLib"`, a `FullCategory` canvas and the `Application` process (`Choice Kind="ContainerDefinition" Name="Process"`, `CategoryReference Kind="Category" Name="Primitive"`, empty `Create`/`Update` patches, both fragments enabled). Fresh ids on every element, written as UTF-8 without BOM.
- **Versions:** `LanguageVersion` and the `VL.CoreLib` version are the VL version of the selected installation (`packs\VL.Core\VL.Core.nuspec`, e.g. `2025.7.4`), never the `vvvv.exe` product version; resolution fails as a blocking issue when no version can be determined.
- **No further dependencies:** local packages, `.csproj` projects and `lib\` assemblies are not declared in the generated documents. The user adds them in vvvv (Dependencies → …); the verified forms above document what vvvv will write.
- **Destination must be new or empty:** keep/copy/author-later document sources were removed together with the "pending" review marker; nothing on disk is read or merged.
- **Validation:** XML structure, namespaces, id uniqueness/format and fragment → patch references are covered by unit tests; opening the document in the selected vvvv host is still a manual check.
