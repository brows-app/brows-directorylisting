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

using System;
using System.Collections.Generic;

namespace Brows.DirectoryListing.ListingItemParsers;

internal sealed class EplfListingParser : FtpListingItemParser {
    private static FtpListingInfoBuilder Build(FtpListingItem line, FtpListingContext context) {
        if (line is null) {
            throw new ArgumentNullException(nameof(line));
        }
        if (context is null) {
            throw new ArgumentNullException(nameof(context));
        }

        var token = line.GetToken(0, true);
        if (token == null) {
            return null;
        }
        if (token[0] != '+')
            return null;

        int pos = token.Find('\t');
        if (pos == -1 || pos == token.Length - 1)
            return null;

        var info = new FtpListingInfoBuilder();
        info.Name = token.ToString().Substring(pos + 1);

        info.Flags = 0;
        info.Size = -1;

        var permissions = "";

        int fact = 1;
        while (fact < pos) {
            int separator = token.Find(',', fact);
            int len;
            if (separator == -1) {
                len = pos - fact;
            }
            else {
                len = separator - fact;
            }

            if (0 == len) {
                ++fact;
                continue;
            }

            var type = token[fact];

            if (type == '/') {
                info.Flags |= DirectoryListItemFlags.Directory;
            }
            else if (type == 's') {
                info.Size = token.GetBase10(fact + 1, len - 1) ?? -1;
            }
            else if (type == 'm') {
                long number = token.GetBase10(fact + 1, len - 1) ?? -1;
                if (number < 0) {
                    return null;
                }
                // 1970-01-01 00:00:00.000 UTC
                info.Time = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc) + TimeSpan.FromSeconds(number);
            }
            else if (type == 'u' && len > 2 && token[fact + 1] == 'p') {
                permissions = token.ToString().Substring(fact + 2, len - 2);
            }

            fact += len + 1;
        }

        info.Permissions = permissions;
        info.OwnerGroup = "";
        return info;
    }

    protected sealed override FtpListingInfoBuilder Build(IReadOnlyList<FtpListingItem> items, FtpListingContext context) {
        return items?.Count == 1 ? Build(items[0], context) : null;
    }
}
