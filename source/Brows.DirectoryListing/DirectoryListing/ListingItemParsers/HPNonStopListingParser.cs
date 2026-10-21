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

internal sealed class HPNonStopListingParser : FtpListingItemParser {
    private static FtpListingInfoBuilder Build(FtpListingItem line, FtpListingContext context) {
        if (line is null) {
            throw new ArgumentNullException(nameof(line));
        }
        if (context is null) {
            throw new ArgumentNullException(nameof(context));
        }

        int index = 0;
        FtpListingToken token;

        // Get name
        if (null == (token = line.GetToken(index)))
            return null;

        var info = new FtpListingInfoBuilder();
        info.Name = token.ToString();

        // File code, numeric, unsuded
        if (null == (token = line.GetToken(++index)))
            return null;
        if (!token.IsBase10)
            return null;

        // Size
        if (null == (token = line.GetToken(++index)))
            return null;
        if (!token.IsBase10)
            return null;

        info.Size = token.Base10 ?? -1;

        info.Flags = 0;

        // Date
        if (null == (token = line.GetToken(++index)))
            return null;
        if (null == (info.Time = ParseShortDate(token, false)))
            return null;

        // Time
        if (null == (token = line.GetToken(++index)))
            return null;
        if (!ParseTime(token, info))
            return null;

        // Owner
        if (null == (token = line.GetToken(++index)))
            return null;
        var ownerGroup = token.ToString();

        if (token[token.Length - 1] == ',') {
            // Owner, part 2
            if (null == (token = line.GetToken(++index)))
                return null;
            ownerGroup += " " + token.ToString();
        }

        // Permissions
        FtpListingToken permToken;
        if (null == (permToken = line.GetToken(++index)))
            return null;

        // Nothing
        if (null != (token = line.GetToken(++index)))
            return null;

        info.Permissions = permToken.ToString();
        info.OwnerGroup = ownerGroup;

        return info;
    }

    protected sealed override FtpListingInfoBuilder Build(IReadOnlyList<FtpListingItem> items, FtpListingContext context) {
        return items?.Count == 1 ? Build(items[0], context) : null;
    }
}
