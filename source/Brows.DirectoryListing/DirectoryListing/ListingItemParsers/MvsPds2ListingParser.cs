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

internal sealed class MvsPds2ListingParser : FtpListingItemParser {
    private static FtpListingInfoBuilder Build(FtpListingItem line, FtpListingContext context) {
        int index = 0;
        FtpListingToken token;
        if (null == (token = line.GetToken(index))) {
            return null;
        }

        var info = new FtpListingInfoBuilder();
        info.Name = token.ToString();

        info.Flags = 0;
        info.OwnerGroup = "";
        info.Permissions = info.OwnerGroup;
        info.Size = -1;

        if (null == (token = line.GetToken(++index))) {
            return info;
        }

        var size = token.Base16;
        var sizeIsInvalid = null == size || size == -1;
        if (sizeIsInvalid) {
            return null;
        }
        info.Size = size;

        // Unused hexadecimal token
        if (null == (token = line.GetToken(++index))) {
            return null;
        }
        if (!token.IsBase16) {
            return null;
        }

        // Unused numeric token
        if (null == (token = line.GetToken(++index))) {
            return null;
        }
        if (!token.IsBase10) {
            return null;
        }

        int start = ++index;
        while (null != (token = line.GetToken(index))) {
            ++index;
        }
        if (index - start < 2) {
            return null;
        }
        --index;

        if (null == (token = line.GetToken(index))) {
            return null;
        }
        if (!token.IsBase10 && token.ToString() != "ANY") {
            return null;
        }

        if (null == (token = line.GetToken(index - 1))) {
            return null;
        }
        if (!token.IsBase10 && token.ToString() != "ANY") {
            return null;
        }

        for (int i = start; i < index - 1; ++i) {
            if (null == (token = line.GetToken(i))) {
                return null;
            }
            int len = token.Length;
            for (int j = 0; j < len; ++j) {
                if (token[j] < 'A' || token[j] > 'Z') {
                    return null;
                }
            }
        }

        return info;
    }

    protected sealed override FtpListingInfoBuilder Build(IReadOnlyList<FtpListingItem> items, FtpListingContext context) {
        return items?.Count == 1 ? Build(items[0], context) : null;
    }
}
