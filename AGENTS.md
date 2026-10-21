# Repository guidance

This repository contains Brows.DirectoryListing, a .NET library that parses
FTP directory listings into structured entries. Read the relevant source and
tests before changing behavior.

## Layout

- `brows-directorylisting.slnx` contains one library project and one test
  project.
- `source/Brows.DirectoryListing/` contains the packable library. Core types
  and the listing pipeline are under `DirectoryListing/`; the 16
  format-specific parsers are under `DirectoryListing/ListingItemParsers/`.
- `tests/Brows.DirectoryListing.Tests/` contains the NUnit test project. Its
  fixture is `DirectoryListing/DirectoryListTest.cs`.
- The root `README.md` is the repository guide. The source project's
  `README.md` is packed into the NuGet package; keep their shared product,
  API, requirements, and licensing information in sync.
- `Directory.Build.props` and `Directory.Packages.props` contain shared
  build settings and central package versions. `source/Directory.Build.props`
  and `tests/Directory.Build.props` add group-specific settings.
- `Properties/AssemblyInfo.cs.cs` is the existing assembly-info filename in
  both projects. Preserve the doubled extension.
- `samples/` contains shared build properties only; it has no sample project.

## Preserve existing work

- Before editing, inspect `git status` and the existing diff. Preserve
  unrelated changes and keep edits scoped to the request.
- Do not commit generated output or IDE state such as `out/`, `bin/`,
  `obj/`, or `.vs/`.

## Build and test

The projects target `net462`, `net48`, `net8.0`, and `net10.0`, use C#
14, and select SDK `10.0.100` through `global.json` with feature roll-forward.
The .NET Framework targets require Windows; the .NET 8 and .NET 10 targets
contain no platform-specific APIs. Build artifacts go to `out/`.

Run restore, build, and tests from the repository root:

```sh
dotnet restore
dotnet build --no-restore --configuration Release
dotnet test --no-restore --configuration Release --no-build
```

For the current test fixture on .NET 10:

```sh
dotnet test tests/Brows.DirectoryListing.Tests/Brows.DirectoryListing.Tests.csproj --no-restore --configuration Release --framework net10.0 --filter "FullyQualifiedName~DirectoryListTest"
```

The tests use NUnit 5, `Assert.That`, and shared parser cases. Package versions
are managed in `Directory.Packages.props`; omit versions from project package
references. Keep the source README in place because package settings include it
at the package root.

## Public API and documentation

The public API consists of `DirectoryList`, `DirectoryListItem`,
`DirectoryListItemFlags`, and `DirectoryListServerType`. The parser and
format-specific implementation types are internal; tests access them through
`InternalsVisibleTo`. Do not widen the public API unless explicitly requested.

Write XML documentation for public types and their public members. Do not add
XML documentation to internal types or to members declared on internal types.
Update both READMEs when a documented public contract changes. The package
README omits the repository installation and build instructions.

## Parser behavior

- `DirectoryList.Parse` consumes an asynchronous sequence of decoded text
  chunks. CR, LF, and NUL terminate physical lines; CRLF is treated as one
  terminator even across chunks. A leading BOM is removed from each line. A
  final unterminated line is emitted when nonempty. Lines over 10,000 UTF-8
  bytes throw `InvalidDataException`.
- `FtpListingParser` tries format parsers in order. A parsed result or an
  explicit skip stops dispatch; changing parser order can change results.
  MVS migrated, PDS2, and tape formats are enabled only for
  `DirectoryListServerType.Mvs`. HP NonStop and z/VM parsers are prioritized
  when their respective server types are selected.
- Single-line entries are yielded when recognized. Unrecognized lines may be
  held to try a two-line VMS entry or to decide whether the complete listing
  qualifies for raw filename fallback. The fallback is disabled when a
  structured entry is recognized or skipped; candidate lines containing a
  space do not qualify. Entries named `.` or `..` are filtered.
- `FtpListingItem` splits on spaces and tabs and retains a final one-character
  token. Unknown sizes usually use `-1`, but some formats provide synthetic
  values (MVS sequential entries use 100). The unit of a reported size depends
  on the listing format; some MVS formats use record counts.
- Listing timestamps are represented as UTC-kind `DateTime` values. The
  parser does not infer the server's timezone: `listingTimeOffset` is added
  for formats treated as server-local, while `serverTimeOffset` is added to
  every timestamp. MLSD timestamps are UTC-valued and are not changed by the
  listing-time offset.
- `DirectoryListItem.Target` preserves a nonempty constructor value; check
  each link parser's extraction separately. `File` is the zero value of
  `DirectoryListItemFlags`. `Extension` returns the last dot and following
  text, or an empty string when there is no dot; a dotfile returns its full
  name.

## Code conventions

- Follow `.editorconfig` and nearby code: four spaces for C#, two for project
  XML and Markdown, file-scoped namespaces, and same-line opening braces.
- Use the configured language version and APIs available on every target
  framework. Check shared build settings and global usings before adding
  imports or project-specific settings.
- Keep public API changes explicit and task-scoped. Preserve compatibility
  shims, project import structure, and centrally managed package versions.
- Put each top-level type in a separate file, match namespaces to folder
  structure, and follow nearby naming and member-order conventions. Prefer
  `var` for locals and block-bodied methods; follow the existing C# style.
- Put `CancellationToken` parameters last. Preserve target-specific
  conditions and compatibility shims.
- Preserve FileZilla copyright and license notices in derived source files.
  The READMEs record FileZilla Client 3.71.1 as the source baseline and retain
  the archive checksum. Keep the FileZilla code attribution distinct from the
  Bernstein credit for the Unix, NetWare, NetPresenz, EPLF, and DOS test
  samples. FileZilla is the source provenance for this library. Document the
  library's supported API behavior in both READMEs and cover it with tests.

## Tests

- Follow the existing NUnit style and keep tests compatible with all target
  frameworks.
- For reported defects, add regression coverage where useful and confirm the
  test fails because of the defect before applying the fix.
- Date inference uses UTC in production. Use the fixture's fixed-clock helper
  for date-boundary regressions rather than relying on the current date.

## Code reviews

- For a requested code review, inspect the full requested scope and write
  numbered findings to `_agents/Review.md`. Read existing findings first,
  retain relevant findings, keep issue numbers stable, and include locations
  and suggested resolutions. If the file does not exist, start at issue 1; do
  not search Git history for it.
- When fixing review findings, work only on the requested issue numbers. Keep
  resolved findings and mark them resolved.
- Create separate worktrees only when explicitly requested. For multiple
  requested issues, use sibling directories named
  `_agent-{repository}-{issue}` and branches named
  `agent/{issue}/{short-description}`. Commit each issue separately only
  when requested.

## Plans and completion

- When asked to plan code changes, write
  `_agents/Plan-{PascalCaseDescription}.md`.
- Before reporting completion, inspect the final diff, including untracked
  files, and run `git diff --check`. Report changes, checks and results, and
  any checks that could not run.
