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
using System.Linq;

namespace Brows.DirectoryListing.ListingItemParsers;

internal sealed class OtherListingParser : FtpListingItemParser {
    private static FtpListingInfoBuilder Build(FtpListingItem line, FtpListingContext context) {
        int index = 0;
        FtpListingToken firstToken;

        if (null == (firstToken = line.GetToken(index))) {
            return null;
        }

        if (!firstToken.IsBase10) {
            return null;
        }

        // Possible formats: Numerical unix, VShell or OS/2

        FtpListingToken token;
        if (null == (token = line.GetToken(++index))) {
            return null;
        }

        var info = new FtpListingInfoBuilder();
        info.Flags = 0;

        // If token is a number, than it's the numerical Unix style format,
        // else it's the VShell, OS/2 or nortel.VxWorks format
        if (token.IsBase10) {
            if (firstToken.Length >= 2 && firstToken[1] == '4') {
                info.Flags |= DirectoryListItemFlags.Directory;
            }

            var ownerGroup = token.ToString();

            if (null == (token = line.GetToken(++index))) {
                return null;
            }

            ownerGroup += " " + token.ToString();

            // Get size
            if (null == (token = line.GetToken(++index))) {
                return null;
            }

            if (!token.IsBase10) {
                return null;
            }

            info.Size = token.Base10 ?? -1;

            // Get date/time
            if (null == (token = line.GetToken(++index))) {
                return null;
            }

            long number = token.Base10 ?? -1;
            if (number < 0) {
                return null;
            }
            info.Time = Time(number);

            // Get filename
            if (null == (token = line.GetToken(++index, true))) {
                return null;
            }

            info.Name = token.ToString();
            info.Target = "";

            info.Permissions = firstToken.ToString();
            info.OwnerGroup = ownerGroup;
        }
        else {
            // Possible conflict with multiline VMS listings
            if (context.MaybeMultilineVms) {
                return null;
            }

            // VShell, OS/2 or nortel.VxWorks style format
            info.Size = firstToken.Base10 ?? -1;

            // Get date
            var dateMonth = token.ToString();
            int? month = 0;
            if (null == (month = GetMonthFromName(dateMonth))) {
                // OS/2 or nortel.VxWorks
                int skippedCount = 0;
                do {
                    if (token.ToString() == "DIR") {
                        info.Flags |= DirectoryListItemFlags.Directory;
                    }

                    else if (token.Find("-/.") != -1) {
                        break;
                    }

                    ++skippedCount;

                    if (null == (token = line.GetToken(++index))) {
                        return null;
                    }
                } while (true);

                if (null == (info.Time = ParseShortDate(token))) {
                    return null;
                }

                // Get time
                if (null == (token = line.GetToken(++index))) {
                    return null;
                }

                if (!ParseTime(token, info)) {
                    return null;
                }

                // Get filename
                if (null == (token = line.GetToken(++index, true))) {
                    return null;
                }

                info.Name = token.ToString();
                if (info.Name.Length >= 5) {
                    var type = info.Name.Substring(info.Name.Length - 5).ToLowerInvariant();
                    if (0 == skippedCount && type == "<dir>") {
                        info.Flags |= DirectoryListItemFlags.Directory;
                        info.Name = info.Name.Substring(0, info.Name.Length - 5);
                        while (!string.IsNullOrEmpty(info.Name) && info.Name.Last() == ' ') {
                            info.Name = info.Name.Substring(0, info.Name.Length - 1);
                        }
                    }
                }
            }
            else {
                // Get day
                if (null == (token = line.GetToken(++index))) {
                    return null;
                }

                if (!token.IsBase10 && !token.IsBase10OnLeft) {
                    return null;
                }

                long day = token.Base10 ?? -1;
                if (day < 0 || day > 31) {
                    return null;
                }

                // Get Year
                if (null == (token = line.GetToken(++index))) {
                    return null;
                }

                if (!token.IsBase10) {
                    return null;
                }

                long year = token.Base10 ?? -1;
                if (year < 0 || year > 9999) {
                    return null;
                }
                if (year < 50) {
                    year += 2000;
                }
                else if (year < 1000) {
                    year += 1900;
                }

                if (!(year > 0 && month > 0 && day > 0)) {
                    return null;
                }
                info.Time = new DateTime((int)year, month.Value, (int)day, 0, 0, 0, DateTimeKind.Utc);

                // Get time
                if (null == (token = line.GetToken(++index))) {
                    return null;
                }

                if (!ParseTime(token, info)) {
                    return null;
                }

                // Get filename
                if (null == (token = line.GetToken(++index, true))) {
                    return null;
                }

                info.Name = token.ToString();
                var chr = token[token.Length - 1];
                if (chr == '/' || chr == '\\') {
                    info.Flags |= DirectoryListItemFlags.Directory;
                    info.Name = info.Name.Substring(0, info.Name.Length - 1);
                }
            }
            info.Target = "";
            info.OwnerGroup = "";
            info.Permissions = info.OwnerGroup;
            info.Time += context.TimeZoneOffset;
        }

        return info;
    }

    protected sealed override FtpListingInfoBuilder Build(IReadOnlyList<FtpListingItem> items, FtpListingContext context) {
        return items?.Count == 1 ? Build(items[0], context) : null;
    }
}
