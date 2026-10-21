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

internal sealed class OS9ListingParser : FtpListingItemParser {
    private static FtpListingInfoBuilder Build(FtpListingItem line, FtpListingContext context) {
        int index = 0;

        // Get owner
        var ownerGroupToken = line.GetToken(index++);
        if (ownerGroupToken == null) {
            return null;
        }

        // Make sure it's number.number
        int pos = ownerGroupToken.Find('.');
        if (pos == -1 || 0 == pos || pos == ownerGroupToken.Length - 1)
            return null;

        if (!ownerGroupToken.Chunk(0, pos).IsBase10)
            return null;

        if (!ownerGroupToken.Chunk(pos + 1, ownerGroupToken.Length - pos - 1).IsBase10)
            return null;

        var info = new FtpListingInfoBuilder();
        info.Flags = 0;

        // Get date
        FtpListingToken token;
        if (null == (token = line.GetToken(index++)))
            return null;

        if (null == (info.Time = ParseShortDate(token, true)))
            return null;

        // Unused token
        if (null == (token = line.GetToken(index++)))
            return null;

        // Get perms
        var permToken = line.GetToken(index++);
        if (permToken == null)
            return null;

        if (permToken[0] == 'd')
            info.Flags |= DirectoryListItemFlags.Directory;

        // Unused token
        if (null == (token = line.GetToken(index++)))
            return null;

        // Get Size
        if (null == (token = line.GetToken(index++)))
            return null;

        if (!token.IsBase10)
            return null;

        info.Size = token.Base10 ?? -1;

        // Filename
        if (null == (token = line.GetToken(index++, true)))
            return null;

        info.Name = token.ToString();
        info.OwnerGroup = ownerGroupToken.ToString();
        info.Permissions = permToken.ToString();

        return info;
    }

    protected sealed override FtpListingInfoBuilder Build(IReadOnlyList<FtpListingItem> items, FtpListingContext context) {
        return items?.Count == 1 ? Build(items[0], context) : null;
    }
}
