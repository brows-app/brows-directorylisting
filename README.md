# Brows.DirectoryListing

A .NET library for parsing FTP server directory listings into structured
entries.

FTP servers do not agree on a single listing format. A server can change
formats depending on the requested path or the client's `OPTS` settings, and
vendors add their own dialects. This library recognizes supported formats and
returns parsed `DirectoryListItem` entries.

| Listing format | Example |
| --- | --- |
| MLSD (RFC 3659) | `Type=file;mOdIfY=20081105165215;size=1234;` |
| Unix `ls -l` | `-rw-r--r--   1 root     other        531 3 29 03:26 02-unix-std file` |
| DOS / IIS | `04-27-00  12:09PM       <DIR>          16-dos-dateambiguous dir` |
| EPLF | `+i8388621.48594,m825718503,r,s280,up755` |
| VMS / OpenVMS | `36-vms-dir.DIR;1  1 19-NOV-2001 21:41 [root,root] (RWE,RWE,RE,RE)` |
| IBM iSeries (AS/400) | `QSYS            77824 02/23/00 15:09:55 *DIR 44-ibm-as400 dir/` |
| IBM MVS, including PDS members and migrated data sets | `TSO005 3390   2005/06/06 213000 U 0 27998 PO 63-MVS.DATASET` |
| IBM OS/9 | `20.20 07/03/29 1026 d-ewrewr 2650 85920 51-OS-9 dir` |
| HP NonStop | `57-HP_NonStop 101 528 6-Apr-07 14:21:18 255, 0 "oooo"` |
| z/VM | `70-ZVMFILE  TRACE   V        65      107        2 2005-10-04 15:28:42 060191` |
| Numerical Unix, VShell, Nortel wfFtp, NetWare, NetPresenz | `0100644   500  101   12345    123456789       20-unix-numerical file` |

## Status

The library exposes `DirectoryList.Parse` as its public parser entry point;
format-specific parser implementations are internal. Publishing is configured
to run from a published GitHub release: the workflow packs and pushes the
`Brows.DirectoryListing` NuGet package.

## Target frameworks

- .NET Framework 4.6.2
- .NET Framework 4.8
- .NET 8
- .NET 10

The .NET Framework targets require Windows. The .NET 8 and .NET 10 targets use
no platform-specific APIs.

## Installation

To consume the current source checkout, reference the project directly:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/brows-directorylisting/source/Brows.DirectoryListing/Brows.DirectoryListing.csproj" />
</ItemGroup>
```

After a package release has been published, the package can also be installed
with `dotnet add package Brows.DirectoryListing`.

## Usage

`DirectoryList.Parse` consumes an asynchronous sequence of decoded text chunks
and yields parsed `DirectoryListItem` entries. Call
`DirectoryList.Parse(contents, cancellationToken)` for general listings, or
`DirectoryList.Parse(contents, options, cancellationToken)` to configure parsing
with a `DirectoryListOptions` instance. Passing `null` for `options` uses the
same defaults as a new options instance:

| Option | Default | Purpose |
| --- | --- | --- |
| `ServerType` | `DirectoryListServerType.Default` | Selects server-specific parsing for MVS migrated/PDS2/tape, HP NonStop, or z/VM listings. |
| `ListItemTimeOffset` | `TimeSpan.Zero` | Adjusts timestamps in formats treated as server-local. |
| `ServerTimeOffset` | `TimeSpan.Zero` | Applies an additional server-clock correction to every timestamp. |

Physical lines are separated by CR, LF, or NUL; a CRLF pair is treated as one
terminator, including when split between input chunks. A leading BOM is
removed from each line. A final unterminated line is parsed if it is nonempty.
Each line is limited to 10,000 UTF-8 bytes of the supplied text. Because the
parser receives decoded strings, this limit is based on their UTF-8 encoding,
not the original server's wire encoding. Enumeration throws
`InvalidDataException` if a line exceeds the limit.

For formats that interpret timestamps as server-local, `ListItemTimeOffset` is
added to the parsed time. For example, convert a server's UTC+02:00 local time
to UTC by setting this option to `TimeSpan.FromHours(-2)`. UTC-valued formats
such as MLSD are not changed by this offset. `ServerTimeOffset` is added to every parsed
timestamp, including UTC-valued timestamps. The parser does not infer a
server's timezone; parsed timestamps are represented as UTC-kind
`DateTime` values, but local-time listings require the appropriate offset to
represent the actual UTC time.

The listing offset applies to Unix, DOS, VMS (including date-only entries),
IBM iSeries, wfFtp, MVS PDS members, z/VM, and calendar-based OTHER listings.
MLSD, EPLF, numeric Unix epoch timestamps, MVS data-set dates, HP NonStop,
and OS/9 ignore that offset; the server-clock offset still applies to them.

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Brows.DirectoryListing;

static async IAsyncEnumerable<string> GetListingChunks() {
    yield return "Migrated\t69-SOME.FILE";
    await Task.CompletedTask;
}

await foreach (var entry in DirectoryList.Parse(
    GetListingChunks(),
    new DirectoryListOptions {
        ServerType = DirectoryListServerType.Mvs
    },
    CancellationToken.None)) {
    Console.WriteLine($"{entry.Name}\t{entry.Size}\t{entry.Time}");
}
```

A raw filename list is returned only when the complete listing has no
recognized structured entries and each candidate line contains no ASCII spaces.
Entries named `.` and `..` are excluded from every output
path. Unicode whitespace is preserved as filename text.

Each `DirectoryListItem` exposes these values:

| Property | Description |
| --- | --- |
| `Name` | The entry name after format-specific parsing and normalization. |
| `Size` | The reported size; units depend on the listing format. Most formats report bytes, while some MVS formats report record counts. `-1` is the usual unknown-size value; some formats use a synthetic value, such as 100 for an MVS sequential entry. Fractional K/M/G/T sizes use binary multipliers and truncate toward zero. |
| `Time` | The parsed timestamp, or `null` when the listing has no date. Parser-created values have `DateTimeKind.Utc`; use the parse offsets when a format reports server-local time. |
| `Permissions` | The permissions text recognized by the parser, when available. |
| `OwnerGroup` | The owner/group text recognized by the parser, when available. |
| `Target` | The link target when available. Unix targets preserve literal trailing `/`, `*`, and pipe characters. |
| `Flags` | `File`, `Directory`, and/or `Link`; `File` is the zero value. |
| `Parser` | The parser name, such as `MLSD` or `UNIX`; `null` for a raw filename-list entry. |
| `Extension` | The last dot and all following characters in `Name`, or an empty string when there is no dot. A dotfile such as `.gitignore` returns its full name. |

Missing permissions and owner/group metadata may be `null` or an empty string.

## Repository layout

- `source/Brows.DirectoryListing/` — the library.
  - `DirectoryListing/` — the public API and parsing pipeline.
  - `DirectoryListing/ListingItemParsers/` — the format-specific parsers.
- `tests/Brows.DirectoryListing.Tests/DirectoryListing/DirectoryListTest.cs` —
  the test fixture.

The public types are `DirectoryList`, `DirectoryListOptions`,
`DirectoryListItem`, `DirectoryListItemFlags`, and `DirectoryListServerType`.
The format-specific parser implementations are internal.

## Build and test

Use the .NET SDK selected by `global.json` (10.0.100, with feature roll-forward).
From the repository root:

```sh
dotnet restore
dotnet build --no-restore --configuration Release
dotnet test --no-restore --configuration Release --no-build
```

To run the current listing parser test fixture on .NET 10:

```sh
dotnet test tests/Brows.DirectoryListing.Tests/Brows.DirectoryListing.Tests.csproj \
  --no-restore --configuration Release --framework net10.0 \
  --filter "FullyQualifiedName~DirectoryListTest"
```

Build output is written to `out/`.

## Source code

Source code and build files are available on GitHub in the repository linked
from the package's NuGet metadata. For the source associated with a release,
use the repository revision recorded in that package's metadata.

## Origin of the code

This C# parser is adapted from FileZilla Client's directory-listing parser. The
upstream source baseline recorded for this repository is FileZilla Client
3.71.1, specifically `src/engine/directorylistingparser.cpp` and its related
parser tests. The source archive is available
[here](https://fossies.org/linux/misc/FileZilla_3.71.1_src.tar.xz); its SHA-256
is `3df9b9b3e5b2c37dc6d9ecb883554aba140a02ec47c3d6b52333bf1733af4da4`.

Some test listings are sample data credited by FileZilla's older
`CFtpListResult` implementation to
[D. J. Bernstein's `ftpparse`](https://cr.yp.to/ftpparse/ftpparse.c). That
credit applies to the Unix, NetWare, NetPresenz, EPLF, and DOS samples in this
repository; it does not describe the parser's code lineage.

FileZilla Client 3.71.1 is distributed under the GNU General Public License,
version 3 or later. This repository is also distributed under GPL version 3 or
later, as stated in `LICENSE` and the `GPL-3.0-or-later` package metadata.
Derived source files retain the original copyright notices and identify their
modifications in accordance with GPL sections 4 and 5(a).

Contributions to this repository are licensed under the same terms.

## License

[GNU General Public License, version 3 or later](LICENSE). The full text is
also available at <https://www.gnu.org/licenses/gpl-3.0.txt>.

## References

- [FileZilla Client](https://filezilla-project.org/) — the upstream project;
  see the [3.71.1 source archive](https://fossies.org/linux/misc/FileZilla_3.71.1_src.tar.xz)
  and [version history](https://filezilla-project.org/versions.php?type=client).
- [D. J. Bernstein's `ftpparse`](https://cr.yp.to/ftpparse/ftpparse.c) — source
  of several credited test listings.
- [RFC 3659](https://www.rfc-editor.org/rfc/rfc3659) — the MLSD and MLST
  machine-list format.
