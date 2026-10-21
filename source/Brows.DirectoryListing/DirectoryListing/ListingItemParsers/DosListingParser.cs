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

using System.Collections.Generic;

namespace Brows.DirectoryListing.ListingItemParsers;

internal sealed class DosListingParser : FtpListingItemParser {
    private static FtpListingInfoBuilder Build(FtpListingItem line, FtpListingContext context) {
        if (line is null) {
            throw new ArgumentNullException(nameof(line));
        }
        if (context is null) {
            throw new ArgumentNullException(nameof(context));
        }

        int index = 0;
        var info = new FtpListingInfoBuilder();

        // Get first token, has to be a valid date
        var token = line.GetToken(index);
        if (token == null) {
            return null;
        }

        info.Flags = 0;
        if (null == (info.Time = ParseShortDate(token))) {
            return null;
        }

        // Extract time
        if (null == (token = line.GetToken(++index))) {
            return null;
        }

        if (!ParseTime(token, info)) {
            return null;
        }

        // If next token is <DIR>, entry is a directory
        // else, it should be the filesize.
        if (null == (token = line.GetToken(++index)))
            return null;

        if (token.ToString() == "<DIR>") {
            info.Flags |= DirectoryListItemFlags.Directory;
            info.Size = -1;
        }

        else if (token.IsBase10 || token.IsBase10OnLeft) {
            // Convert size, filter out separators
            long size = 0;
            const long max = long.MaxValue;
            const long max10 = max / 10;
            int len = token.Length;
            for (int i = 0; i < len; ++i) {
                var chr = token[i];
                if (chr == ',' || chr == '.') {
                    continue;
                }
                if (chr < '0' || chr > '9') {
                    return null;
                }

                var digit = chr - '0';
                if (size > max10) {
                    return null;
                }
                size *= 10;
                if (max - digit < size) {
                    return null;
                }
                size += digit;
            }
            info.Size = size;
        }
        else {
            return null;
        }

        // Extract filename
        if (null == (token = line.GetToken(++index, true))) {
            return null;
        }
        info.Name = token.ToString();

        info.Target = "";
        info.OwnerGroup = "";
        info.Permissions = info.OwnerGroup;
        info.Time += context.TimeZoneOffset;

        return info;
    }

    protected sealed override FtpListingInfoBuilder Build(IReadOnlyList<FtpListingItem> items, FtpListingContext context) {
        return items?.Count == 1 ? Build(items[0], context) : null;
    }
}
