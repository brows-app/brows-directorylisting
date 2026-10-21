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

internal sealed class VmsListingParser : FtpListingItemParser {
    private static string Unescape(string str, char escape) {
        string res = "";
        for (var i = 0; i < str.Length; ++i) {
            var c = str[i];
            if (c == escape) {
                ++i;
                if (i == str.Length || 0 == str[i]) {
                    break;
                }
                c = str[i];
            }
            res += c;
        }

        return res;
    }

    private static FtpListingInfoBuilder Build(FtpListingItem line1, FtpListingItem line2, FtpListingContext context) {
        var line1s = line1?.ToString();
        if (line1s == null) {
            return null;
        }
        var i = line1s.IndexOf(';');
        if (i < 0) {
            return null;
        }
        var j = i + 1;
        if (j >= line1s.Length) {
            return null;
        }
        var nextIsNumeric = int.TryParse(
#if NET
            line1s.AsSpan(j)
#else
            line1s.Substring(j)
#endif
            , out _);
        if (nextIsNumeric == false) {
            return null;
        }
        if (line2 == null) {
            return null;
        }
        var line = line1.ToString() + ' ' + line2.ToString();
        var item = new FtpListingItem(line);
        return Build(item, context);
    }

    private static FtpListingInfoBuilder Build(FtpListingItem line, FtpListingContext context) {
        FtpListingToken token;
        int index = 0;

        if (null == (token = line.GetToken(index)))
            return null;

        int pos = token.Find(';');
        if (pos == -1)
            return null;

        var info = new FtpListingInfoBuilder();
        info.Flags = 0;

        if (pos > 4 && token.ToString().Substring(pos - 4, 4) == ".DIR") {
            info.Flags |= DirectoryListItemFlags.Directory;
            if (token.ToString().Substring(pos) == ";1")
                info.Name = token.ToString().Substring(0, pos - 4);

            else
                info.Name = token.ToString().Substring(0, pos - 4) + token.ToString().Substring(pos);
        }

        else
            info.Name = token.ToString();

        // Some VMS servers escape special characters like additional dots with ^
        info.Name = Unescape(info.Name, '^');

        if (null == (token = line.GetToken(++index)))
            return null;

        string ownerGroup = "";
        string permissions = "";

        // This field can either be the filesize, a username (at least that's what I think) enclosed in [] or a date.
        if (!token.IsBase10 && !token.IsBase10OnLeft) {
            // Must be username
            var len = token.Length;
            if (len < 3 || token[0] != '[' || token[len - 1] != ']')
                return null;
            ownerGroup = token.ToString().Substring(1, len - 2);

            if (null == (token = line.GetToken(++index)))
                return null;
            if (!token.IsBase10 && !token.IsBase10OnLeft)
                return null;
        }

        // Current token is either size or date
        bool gotSize = false;
        pos = token.Find('/');

        if (0 == pos)
            return null;

        if (token.IsBase10 || pos != -1 && token.Find('/', pos + 1) == -1) {
            // Definitely size
            FtpListingToken sizeToken;
            if (pos == -1)
                sizeToken = token;
            else
                sizeToken = new(token.View.Substring(0, pos));
            if (null == (info.Size = ParseComplexFileSize(sizeToken, 512)))
                return null;
            gotSize = true;

            if (null == (token = line.GetToken(++index)))
                return null;
        }
        else if (pos == -1 && token.IsBase10OnLeft) {
            // Perhaps size
            if (null != (info.Size = ParseComplexFileSize(token, 512))) {
                gotSize = true;

                if (null == (token = line.GetToken(++index)))
                    return null;
            }
        }

        // Get date
        if (null == (info.Time = ParseShortDate(token)))
            return null;

        // Get time
        if (null == (token = line.GetToken(++index))) {
            info.Time += context.TimeZoneOffset;
            return info;
        }

        if (!ParseTime(token, info)) {
            int len = token.Length;
            if (token[0] == '[' && token[len - 1] != ']')
                return null;
            if (token[0] == '(' && token[len - 1] != ')')
                return null;
            if (token[0] != '[' && token[len - 1] == ']')
                return null;
            if (token[0] != '(' && token[len - 1] == ')')
                return null;
            --index;
        }

        if (!gotSize) {
            // Get size
            if (null == (token = line.GetToken(++index)))
                return null;

            if (!token.IsBase10 && !token.IsBase10OnLeft)
                return null;

            pos = token.Find('/');
            if (0 == pos)
                return null;

            FtpListingToken sizeToken;
            if (pos == -1)
                sizeToken = token;
            else
                sizeToken = new(token.View.Substring(0, pos));
            if (null == (info.Size = ParseComplexFileSize(sizeToken, 512)))
                return null;
        }

        // Owner / group and permissions
        while (null != (token = line.GetToken(++index))) {
            var len = token.Length;
            if (len > 2 && token[0] == '(' && token[len - 1] == ')') {
                if (!string.IsNullOrEmpty(permissions))
                    permissions += " ";
                permissions += token.ToString().Substring(1, len - 2);
            }
            else if (len > 2 && token[0] == '[' && token[len - 1] == ']') {
                if (!string.IsNullOrEmpty(ownerGroup))
                    ownerGroup += " ";
                ownerGroup += token.ToString().Substring(1, len - 2);
            }
            else {
                if (!string.IsNullOrEmpty(ownerGroup))
                    ownerGroup += " ";
                ownerGroup += token.ToString();
            }
        }
        info.Permissions = permissions;
        info.OwnerGroup = ownerGroup;

        info.Time += context.TimeZoneOffset;

        return info;
    }

    protected sealed override FtpListingInfoBuilder Build(IReadOnlyList<FtpListingItem> items, FtpListingContext context) {
        return
            items?.Count == 1 ? Build(items[0], context) :
            items?.Count == 2 ? Build(items[0], items[1], context) :
            null;
    }
}
