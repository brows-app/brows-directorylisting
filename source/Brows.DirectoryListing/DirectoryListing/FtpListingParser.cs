// This file is part of Brows.DirectoryListing.
//
// It is a C# port of the FTP directory listing parser from the FileZilla
// client, and is derived from that project's source code.
//
// This file was modified from the original. Changes include the translation
// to C#, the renaming of types and members, and the restructuring of the
// parsing pipeline.
//
// Copyright (C) 2004-2024 Tim Kosse <tim.kosse@filezilla-project.org>
// Copyright (C) 2026 Ken Yourek
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT
// ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or
// FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for
// more details.
//
// You should have received a copy of the GNU General Public License along
// with this program. If not, see <https://www.gnu.org/licenses/>.

using Brows.DirectoryListing.ListingItemParsers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Brows.DirectoryListing;

internal sealed class FtpListingParser : DirectoryListParser<DirectoryListItem> {
    private readonly IReadOnlyList<FtpListingItemParser> Agents;
    private readonly TimeSpan ListingTimeOffset;
    private readonly TimeSpan ServerTimeOffset;

    private static void RecordUnparsedLine(string line, List<string> fileList, ref bool fileListOnly,
                                           FtpListingContext context) {
        var token = new FtpListingItem(line).GetEndToken(0);
        var lineCanBeFilename = null != token && token.Find(' ') == -1;
        if (lineCanBeFilename != true) {
            fileList.Clear();
            fileListOnly = false;
            context.MaybeMultilineVms = false;
            return;
        }

        context.MaybeMultilineVms = token.Find(';') != -1;
        if (fileListOnly) {
            fileList.Add(token.ToString());
        }
    }

    private static bool IsDotEntry(string name) {
        return name == "." || name == "..";
    }

    private static bool IsBlankLine(string line) {
        return line is null || line.All(character => character == ' ' || character == '\t');
    }

    private static DirectoryListItem ApplyServerTimeOffset(DirectoryListItem item, TimeSpan serverTimeOffset) {
        var itemHasTime = null != item && item.Time.HasValue;
        var serverTimeOffsetIsZero = serverTimeOffset == TimeSpan.Zero;
        if (!itemHasTime || serverTimeOffsetIsZero) {
            return item;
        }

        return new DirectoryListItem(
            item.Name,
            item.Size,
            item.Permissions,
            item.OwnerGroup,
            item.Target,
            item.Time.Value.Add(serverTimeOffset),
            item.Flags,
            item.Parser);
    }

    internal FtpListingParser(DirectoryListServerType serverType = DirectoryListServerType.Default,
                              Func<DateTime> getUtcNow = null,
                              TimeSpan listingTimeOffset = default,
                              TimeSpan serverTimeOffset = default) {
        ListingTimeOffset = listingTimeOffset;
        ServerTimeOffset = serverTimeOffset;

        switch (serverType) {
            case DirectoryListServerType.Default:
            case DirectoryListServerType.Mvs:
            case DirectoryListServerType.HpNonStop:
            case DirectoryListServerType.Zvm:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(serverType), serverType, "Unsupported server type.");
        }

        var agents = new List<FtpListingItemParser>();
        if (serverType == DirectoryListServerType.Zvm) {
            agents.Add(new ZvmListingParser());
        }
        else if (serverType == DirectoryListServerType.HpNonStop) {
            agents.Add(new HPNonStopListingParser());
        }

        agents.AddRange([
            new MlsdListingParser(),
            new UnixListingParser(expectDate: true, getUtcNow: getUtcNow),
            new DosListingParser(),
            new EplfListingParser(),
            new VmsListingParser(),
            new OtherListingParser(),
            new IbmListingParser(),
            new WfFtpListingParser(),
            new MvsListingParser(),
            new MvsPdsListingParser(),
            new OS9ListingParser()
        ]);

        if (serverType == DirectoryListServerType.Mvs) {
            agents.AddRange([
                new MvsMigratedListingParser(),
                new MvsPds2ListingParser(),
                new MvsTapeListingParser()
            ]);
        }

        agents.Add(new UnixListingParser(expectDate: false, getUtcNow: getUtcNow));
        Agents = agents;
    }

    internal (FtpListingItemParser.ParseStatus Status, DirectoryListItem Item) Parse(IReadOnlyList<string> lines,
                                                                                     FtpListingContext context) {
        if (lines is null) {
            throw new ArgumentNullException(nameof(lines));
        }
        var items = lines.Where(line => !IsBlankLine(line))
                         .Select(line => new FtpListingItem(line))
                         .ToList();
        if (items.Count == 0) {
            return (FtpListingItemParser.ParseStatus.NoMatch, null);
        }
        foreach (var agent in Agents) {
            try {
                var status = agent.Parse(items, context, out var info);
                if (status != FtpListingItemParser.ParseStatus.NoMatch) {
                    if (status == FtpListingItemParser.ParseStatus.Parsed) {
                        info = ApplyServerTimeOffset(info, context.ServerTimeOffset);
                    }
                    return (status, info);
                }
            }
            catch (Exception ex) {
                OnParseError(new(ex));
            }
        }
        return (FtpListingItemParser.ParseStatus.NoMatch, null);
    }

    protected sealed override async IAsyncEnumerable<DirectoryListItem>
    ParseLines(IAsyncEnumerable<string> lines, [EnumeratorCancellation] CancellationToken token) {
        if (lines is null) {
            throw new ArgumentNullException(nameof(lines));
        }

        var fileList = new List<string>();
        var fileListOnly = true;
        var prev = default(string);
        var context = new FtpListingContext {
            TimeZoneOffset = ListingTimeOffset,
            ServerTimeOffset = ServerTimeOffset
        };
        await foreach (var line in lines.WithCancellation(token)) {
            if (IsBlankLine(line)) {
                continue;
            }

            var result = Parse([line], context);
            if (result.Status != FtpListingItemParser.ParseStatus.NoMatch) {
                prev = null;
                context.MaybeMultilineVms = false;
                fileList.Clear();
                fileListOnly = false;
                var resultIsVisibleEntry = result.Status == FtpListingItemParser.ParseStatus.Parsed &&
                                           !IsDotEntry(result.Item.Name);
                if (resultIsVisibleEntry) {
                    yield return result.Item;
                }
                continue;
            }

            RecordUnparsedLine(line, fileList, ref fileListOnly, context);
            if (prev == null) {
                prev = line;
                continue;
            }

            var previous = prev;
            result = Parse([previous, line], context);
            context.MaybeMultilineVms = false;
            if (result.Status != FtpListingItemParser.ParseStatus.NoMatch) {
                prev = null;
                fileList.Clear();
                fileListOnly = false;
                var resultIsVisibleEntry = result.Status == FtpListingItemParser.ParseStatus.Parsed &&
                                           !IsDotEntry(result.Item.Name);
                if (resultIsVisibleEntry) {
                    yield return result.Item;
                }
            }
            else {
                prev = line;
            }
        }

        if (fileListOnly) {
            foreach (var name in fileList) {
                if (IsDotEntry(name)) {
                    continue;
                }
                yield return new DirectoryListItem(name, -1, "", "", null, null, DirectoryListItemFlags.File);
            }
        }
    }
}
