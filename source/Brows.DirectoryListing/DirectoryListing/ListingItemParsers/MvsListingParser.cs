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

internal sealed class MvsListingParser : FtpListingItemParser {
    private static FtpListingInfoBuilder Build(FtpListingItem line, FtpListingContext context) {
        int index = 0;
        FtpListingToken token;

        // volume
        if (null == (token = line.GetToken(index++)))
            return null;

        // unit
        if (null == (token = line.GetToken(index++)))
            return null;

        // Referred date
        if (null == (token = line.GetToken(index++)))
            return null;

        var info = new FtpListingInfoBuilder();
        info.Flags = 0;
        if (token.ToString() != "**NONE**" && null == (info.Time = ParseShortDate(token))) {
            // Perhaps of the following type:
            // TSO004 3390 VSAM FOO.BAR
            if (token.ToString() != "VSAM")
                return null;

            if (null == (token = line.GetToken(index++)))
                return null;

            info.Name = token.ToString();
            if (info.Name.IndexOf(' ') > -1)
                return null;

            info.Size = -1;
            info.OwnerGroup = "";
            info.Permissions = info.OwnerGroup;

            return info;
        }

        // ext
        if (null == (token = line.GetToken(index++)))
            return null;
        if (!token.IsBase10)
            return null;

        int prevLen = token.Length;

        // used
        if (null == (token = line.GetToken(index++)))
            return null;
        if (token.IsBase10 || token.ToString() == "????" || token.ToString() == "++++") {
            // recfm
            if (null == (token = line.GetToken(index++)))
                return null;
            if (token.IsBase10)
                return null;
        }

        else {
            if (prevLen < 6)
                return null;
        }

        // lrecl
        if (null == (token = line.GetToken(index++)))
            return null;
        if (!token.IsBase10)
            return null;

        // blksize
        if (null == (token = line.GetToken(index++)))
            return null;
        if (!token.IsBase10)
            return null;

        // dsorg
        if (null == (token = line.GetToken(index++)))
            return null;

        if (token.ToString() == "PO" || token.ToString() == "PO-E") {
            info.Flags |= DirectoryListItemFlags.Directory;
            info.Size = -1;
        }

        else
            info.Size = 100;

        // name of dataset or sequential file
        if (null == (token = line.GetToken(index++, true)))
            return null;

        info.Name = token.ToString();

        info.OwnerGroup = "";
        info.Permissions = info.OwnerGroup;

        return info;
    }

    protected sealed override FtpListingInfoBuilder Build(IReadOnlyList<FtpListingItem> items, FtpListingContext context) {
        return items?.Count == 1 ? Build(items[0], context) : null;
    }
}
