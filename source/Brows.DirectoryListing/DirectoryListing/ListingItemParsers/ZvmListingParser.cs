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

internal sealed class ZvmListingParser : FtpListingItemParser {
    private static FtpListingInfoBuilder Build(FtpListingItem line, FtpListingContext context) {

        int index = 0;
        FtpListingToken token;

        // Get name
        if (null == (token = line.GetToken(index)))
            return null;

        var info = new FtpListingInfoBuilder();
        info.Name = token.ToString();

        // Get filename extension
        if (null == (token = line.GetToken(++index)))
            return null;
        info.Name += "." + token.ToString();

        // File format. Unused
        if (null == (token = line.GetToken(++index)))
            return null;
        var format = token.ToString();
        if (format != "V" && format != "F")
            return null;

        // Record length
        if (null == (token = line.GetToken(++index)))
            return null;

        if (!token.IsBase10)
            return null;

        long size = token.Base10 ?? -1;

        // Number of records
        if (null == (token = line.GetToken(++index)))
            return null;

        if (!token.IsBase10)
            return null;

        long records = token.Base10 ?? -1;
        var recordCountIsInvalid = size > 0 &&
                                   (records < 0 || long.MaxValue / size < records);
        if (recordCountIsInvalid)
            return null;

        if (size > 0)
            size *= records;
        info.Size = size;

        // Unused (Block size?)
        if (null == (token = line.GetToken(++index)))
            return null;

        if (!token.IsBase10)
            return null;

        info.Flags = 0;

        // Date
        if (null == (token = line.GetToken(++index)))
            return null;

        if (null == (info.Time = ParseShortDate(token, true)))
            return null;

        // Time
        if (null == (token = line.GetToken(++index)))
            return null;

        if (!ParseTime(token, info))
            return null;

        // Owner
        FtpListingToken ownerGroupToken;
        if (null == (ownerGroupToken = line.GetToken(++index)))
            return null;

        // No further token!
        if (null != (token = line.GetToken(++index)))
            return null;

        info.OwnerGroup = ownerGroupToken.ToString();
        info.Permissions = "";
        info.Target = null;
        info.Time += context.TimeZoneOffset;

        return info;
    }

    protected sealed override FtpListingInfoBuilder Build(IReadOnlyList<FtpListingItem> items, FtpListingContext context) {
        return items?.Count == 1 ? Build(items[0], context) : null;
    }
}
