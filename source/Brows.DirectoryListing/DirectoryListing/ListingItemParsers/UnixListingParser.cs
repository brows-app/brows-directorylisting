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

internal sealed class UnixListingParser : FtpListingItemParser {
    private readonly Func<DateTime> GetUtcNow;

    private FtpListingInfoBuilder Build(FtpListingItem line, FtpListingContext context) {
        var info = new FtpListingInfoBuilder();
        int index = 0;
        var permissionToken = line[index];
        if (permissionToken == null) {
            return null;
        }

        var chr = permissionToken[0];
        if (chr != 'b' &&
                chr != 'c' &&
                chr != 'd' &&
                chr != 'l' &&
                chr != 'p' &&
                chr != 's' &&
                chr != '-') {
            return null;
        }


        info.Permissions = permissionToken.ToString();

        info.Flags = DirectoryListItemFlags.File;

        if (chr == 'd' || chr == 'l') {
            info.Flags |= DirectoryListItemFlags.Directory;
        }

        if (chr == 'l') {
            info.Flags |= DirectoryListItemFlags.Link;
        }

        // Check for netware servers, which split the permissions into two parts
        bool netware = false;
        if (permissionToken.Length == 1) {
            var cont_perm = line.GetToken(++index);
            if (null == cont_perm) {
                return null;
            }
            info.Permissions += " " + cont_perm.ToString();
            netware = true;
        }

        int numOwnerGroup = 3;
        if (!netware) {
            // Filter out link count, we don't need it
            var linkCount = line.GetToken(++index);
            if (null == linkCount) {
                return null;
            }

            if (!linkCount.IsBase10) {
                --index;
            }
        }

        // Repeat until numOwnerGroup is 0 since not all servers send every possible field
        int startindex = index;
        do {
            // Reset index
            index = startindex;

            var ownerGroup = "";
            for (int i = 0; i < numOwnerGroup; ++i) {
                var ownerGroupToken = line.GetToken(++index);
                if (null == ownerGroupToken) {
                    return null;
                }
                if (0 != i) {
                    ownerGroup += " ";
                }
                ownerGroup += ownerGroupToken.ToString();
            }


            var sizeToken = line.GetToken(++index);
            if (null == sizeToken) {
                return null;
            }

            // Check for concatenated groupname and size fields
            var size = ParseComplexFileSize(sizeToken);
            if (size == null) {
                if (!sizeToken.IsBase10OnRight) {
                    continue;
                }
                var isInvalidNumericSize = sizeToken.IsBase10OnLeft && !sizeToken.IsBase10;
                if (isInvalidNumericSize) {
                    continue;
                }
                size = sizeToken.Base10 ?? -1;

                // Append missing group to ownerGroup
                if (!string.IsNullOrEmpty(ownerGroup)) {
                    ownerGroup += " ";
                }

                var group = sizeToken.ToString();
                int i;
                for (i = group.Length - 1;
                         i >= 0 && group[i] >= '0' && group[i] <= '9';
                         --i) {
                }

                ownerGroup += group.Substring(0, i + 1);
            }


            if (ExpectDate) {
                var time = info.Time = ParseUnixDateTime(line, ref index);
                if (time == null) {
                    continue;
                }
            }

            // Get the filename
            var nameToken = line.GetEndToken(++index);
            if (null == nameToken) {
                continue;
            }

            info.Name = nameToken.ToString();

            if (info.Flags.HasFlag(DirectoryListItemFlags.Link)) {
                var pos = info.Name.IndexOf(" -> ");
                if (pos > 0) {
                    info.Target = info.Name.Substring(pos + 4);
                    info.Name = info.Name.Substring(0, pos);
                }
            }

            // Classification markers belong to the entry name, not the link target.
            chr = info.Name[info.Name.Length - 1];
            if (chr == '/' || chr == '|' || chr == '*') {
                info.Name = info.Name.Substring(0, info.Name.Length - 1);
            }

            info.Size = size;
            info.Permissions = info.Permissions;
            info.OwnerGroup = ownerGroup;
            info.Time += context.TimeZoneOffset;

            return info;
        }
        while (0 != numOwnerGroup--);

        return null;
    }

    internal UnixListingParser(bool expectDate, Func<DateTime> getUtcNow) {
        ExpectDate = expectDate;
        GetUtcNow = getUtcNow ?? (() => DateTime.UtcNow);
    }

    public DateTime? ParseUnixDateTime(FtpListingItem line, ref int index) {
        bool mayHaveTime = true;
        bool bHasYearAndTime = false;

        // Get the month date field
        var token = line.GetToken(++index);
        if (token == null) {
            return null;
        }

        long year = -1;
        int month = -1;
        long day = -1;
        long hour = -1;
        long minute = -1;

        FtpListingToken dateMonth = default;

        // Some servers use the following date formats:
        // 26-05 2002, 2002-10-14, 01-jun-99 or 2004.07.15
        // slashes instead of dashes are also possible
        int pos = token.Find("-/.");
        if (pos != -1) {
            int pos2 = token.Find("-/.", pos + 1);
            if (pos2 == -1) {
                if (token[pos] != '.') {
                    // something like 26-05 2002
                    day = token.GetBase10(pos + 1, token.Length - pos - 1) ?? -1;
                    if (day < 1 || day > 31) {
                        return null;
                    }
                    dateMonth = new(token.ToString().Substring(0, pos));
                }
                else {
                    dateMonth = token;
                }
            }
            else if (token[pos] != token[pos2]) {
                return null;
            }
            else {
                var shortDate = ParseShortDate(token);
                if (shortDate == null) {
                    return null;
                }

                if (token[pos] == '.') {
                    return shortDate.Value;
                }

                var t = shortDate.Value;
                year = t.Year;
                month = t.Month;
                day = t.Day;
            }
        }
        else if (token.IsBase10) {
            if (token.Base10 > 1000 && token.Base10 < 10000) {
                // Two possible variants:
                // 1) 2005 3 13
                // 2) 2005 13 3
                // assume first one.
                year = token.Base10 ?? -1;
                dateMonth = line.GetToken(++index);
                if (null == dateMonth) {
                    return null;
                }
                mayHaveTime = false;
            }
            else {
                dateMonth = token;
            }
        }
        else {
            if (token.IsBase10OnLeft && token[token.Length - 1] > 127 && token.Base10 > 1000) {
                if (token.Base10 > 10000) {
                    return null;
                }

                // Asian date format: 2005xxx 5xx 20xxx with some non-ascii characters following
                year = token.Base10 ?? -1;

                dateMonth = line.GetToken(++index);
                if (null == dateMonth) {
                    return null;
                }
                mayHaveTime = false;
            }

            else {
                dateMonth = token;
            }
        }

        if (day < 1) {
            // Get day field
            var dayToken = line.GetToken(++index);
            if (null == dayToken) {
                return null;
            }

            long dateDay;

            // Check for non-numeric day
            if (!dayToken.IsBase10 && !dayToken.IsBase10OnLeft) {
                int offset = 0;
                if (dateMonth.ToString().Last() == '.') {
                    ++offset;
                }
                if (!dateMonth.Chunk(0, dateMonth.Length - offset).IsBase10) {
                    return null;
                }
                dateDay = dateMonth.GetBase10(0, dateMonth.Length - offset) ?? -1;
                dateMonth = dayToken;
            }
            else if (dayToken.Length == 5 && dayToken[2] == ':' && dayToken.IsBase10OnRight) {
                // This is a time. We consumed too much already.
                return null;
            }
            else {
                dateDay = dayToken.Base10 ?? -1;
                if (dayToken[dayToken.Length - 1] == ',') {
                    bHasYearAndTime = true;
                }
            }

            if (dateDay < 1 || dateDay > 31) {
                return null;
            }
            day = dateDay;
        }

        if (month < 1) {
            var strMonth = dateMonth.ToString();
            if (dateMonth.IsBase10OnLeft && strMonth[strMonth.Length - 1] > 127) {
                // Most likely an Asian server sending some unknown language specific
                // suffix at the end of the monthname. Filter it out.
                int i;
                for (i = strMonth.Length - 1; i > 0; --i) {
                    if (strMonth[i] >= '0' && strMonth[i] <= '9') {
                        break;
                    }
                }
                strMonth = strMonth.Substring(0, i + 1);
            }
            // Check month name
            while (!string.IsNullOrEmpty(strMonth) && (strMonth.Last() == ',' || strMonth.Last() == '.')) {
                strMonth = strMonth.Remove(strMonth.Length - 1, 1);
            }

            var m = GetMonthFromName(strMonth);
            if (m == null) {
                return null;
            }
            month = m.Value;
        }

        // Get time/year field
        var timeOrYearToken = line.GetToken(++index);
        if (null == timeOrYearToken) {
            return null;
        }

        pos = timeOrYearToken.Find(":.-");
        if (pos != -1 && mayHaveTime) {
            // token is a time
            if (0 == pos || pos == timeOrYearToken.Length - 1) {
                return null;
            }

            var str = timeOrYearToken.ToString();
            hour = int.TryParse(str.Substring(0, pos), out var h) ? h : -1;
            minute = int.TryParse(str.Substring(pos + 1), out var m) ? m : -1;

            if (hour < 0 || hour > 23) {
                // Allow alternate midnight representation
                if (hour != 24 || minute != 0) {
                    return null;
                }
            }
            else if (minute < 0 || minute > 59) {
                return null;
            }

            // Some servers use times only for files newer than 6 months
            if (year <= 0) {
                if (month == -1 || day == -1) {
                    return null;
                }
                var t = GetUtcNow();
                year = t.Year;
                var currentDayOfYear = t.Day + 31 * (t.Month - 1);
                var fileDayOfYear = day + 31 * (month - 1);

                // We have to compare with an offset of one. In the worst case,
                // the server's timezone might be up to 24 hours ahead of the
                // client.
                // Problem: Servers which do send the time but not the year even
                // one day away from getting 1 year old. This is far more uncommon
                // however.
                // This month-weighted comparison matches FileZilla and selects
                // the year before the date is validated (for example, February 29).
                if (currentDayOfYear + 1 < fileDayOfYear) {
                    year -= 1;
                }
            }
        }
        else if (year <= 0) {
            // token is a year
            if (!timeOrYearToken.IsBase10 && !timeOrYearToken.IsBase10OnLeft) {
                return null;
            }

            year = timeOrYearToken.Base10 ?? -1;

            if (year < 0 || year > 3000) {
                return null;
            }
            if (year < 1000) {
                year += 1900;
            }

            if (bHasYearAndTime) {
                var timeToken = line.GetToken(++index);
                if (null == timeToken) {
                    return null;
                }

                if (timeToken.Find(':') == 2 && timeToken.Length == 5 && timeToken.IsBase10OnLeft && timeToken.IsBase10OnRight) {
                    pos = timeToken.Find(':');
                    // token is a time
                    if (0 == pos || pos == timeToken.Length - 1) {
                        return null;
                    }

                    var str = timeToken.ToString();

                    hour = int.TryParse(str.Substring(0, pos), out var h) ? h : -1;
                    minute = int.TryParse(str.Substring(pos + 1), out var m) ? m : -1;

                    if (hour < 0 || hour > 23) {
                        // Allow alternate midnight representation
                        if (hour != 24 || minute != 0) {
                            return null;
                        }
                    }
                    else if (minute < 0 || minute > 59) {
                        return null;
                    }
                }
                else {
                    --index;
                }
            }
        }
        else {
            --index;
        }
        var isAlternateMidnight = hour == 24;
        if (isAlternateMidnight)
            hour = 0;

        var time = new DateTime((int)year, month, (int)day,
            hour >= 0 ? (int)hour : 0, minute >= 0 ? (int)minute : 0, 0, DateTimeKind.Utc);
        if (isAlternateMidnight)
            return time.AddDays(1);

        return time;
    }

    protected sealed override FtpListingInfoBuilder Build(IReadOnlyList<FtpListingItem> items, FtpListingContext context) {
        return items?.Count == 1 ? Build(items[0], context) : null;
    }

    public bool ExpectDate { get; }

    public UnixListingParser(bool expectDate) : this(expectDate, null) {
    }
}
