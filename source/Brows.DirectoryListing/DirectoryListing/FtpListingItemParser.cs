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

namespace Brows.DirectoryListing;

internal abstract class FtpListingItemParser {
    private static FtpListingMonthMap MonthLookup => _MonthLookup ??= new();
    private static FtpListingMonthMap _MonthLookup;
    private string _Name;

    internal enum ParseStatus {
        NoMatch,
        Parsed,
        Skipped
    }

    protected static int? GetMonthFromName(string name) {
        return MonthLookup.Find(name);
    }

    protected static long? ParseComplexFileSize(FtpListingToken token, int blocksize = -1) {
        long size;
        if (token.IsBase10) {
            var parsedSize = token.Base10;
            if (null == parsedSize) {
                return null;
            }
            size = parsedSize.Value;
            if (blocksize > 0) {
                if (size > long.MaxValue / blocksize) {
                    return null;
                }
                size *= blocksize;
            }

            return size;
        }

        int len = token.Length;

        var last = token[len - 1];
        if (last == 'B' || last == 'b') {
            if (len == 1) {
                return null;
            }

            var c = token[--len - 1];
            if (c < '0' || c > '9') {
                --len;
                last = c;
            }
            else {
                last = '\0';
            }
        }
        else if (last >= '0' && last <= '9') {
            last = '\0';
        }
        else {
            if (--len == 0) {
                return null;
            }
        }

        size = 0;

        int dot = -1;
        const long max = long.MaxValue;
        const long max10 = max / 10;
        for (int i = 0; i < len; ++i) {
            var c = token[i];
            if (c >= '0' && c <= '9') {
                if (dot != -1) {
                    continue;
                }
                var digit = c - '0';
                if (size > max10) {
                    return null;
                }
                size *= 10;
                if (max - digit < size) {
                    return null;
                }
                size += digit;
            }
            else if (c == '.') {
                if (dot != -1) {
                    return null;
                }
                dot = i;
            }
            else {
                return null;
            }
        }
        long multiplier = 1;
        switch (last) {
            case 'k':
            case 'K':
                multiplier = 1024;
                break;
            case 'm':
            case 'M':
                multiplier = 1024 * 1024;
                break;
            case 'g':
            case 'G':
                multiplier = 1024L * 1024 * 1024;
                break;
            case 't':
            case 'T':
                multiplier = 1024L * 1024 * 1024 * 1024;
                break;
            case 'b':
            case 'B':
                break;
            case '\0':
                if (blocksize > 0)
                    multiplier = blocksize;
                break;
            default:
                return null;
        }
        if (size > long.MaxValue / multiplier) {
            return null;
        }
        size *= multiplier;

        // Work backwards through the fraction so neither a long mantissa nor
        // its scaled intermediate value has to fit in Int64. Each step retains
        // the exact truncated carry and is bounded by ten times the multiplier.
        long fraction = 0;
        if (dot != -1) {
            for (var i = len - 1; i > dot; --i) {
                fraction = ((token[i] - '0') * multiplier + fraction) / 10;
            }
        }
        if (fraction > long.MaxValue - size) {
            return null;
        }
        return size + fraction;
    }

    protected static DateTime Time(long seconds) {
        return new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) + TimeSpan.FromSeconds(seconds);
    }

    protected static bool ParseTime(FtpListingToken token, FtpListingInfoBuilder info) {
        if (info.Time == null) {
            return false;
        }
        int pos = token.Find(':');
        if (pos < 1 || pos >= token.Length - 1)
            return false;

        long hour = token.GetBase10(0, pos) ?? -1;
        if (hour < 0 || hour > 24)
            return false;

        // See if we got seconds
        int pos2 = token.Find(':', pos + 1);
        int len;
        if (pos2 == -1)
            len = -1;
        else
            len = pos2 - pos - 1;

        if (0 == len)
            return false;

        long minute = token.GetBase10(pos + 1, len) ?? -1;
        if (minute < 0 || minute > 59)
            return false;

        long seconds = -1;
        if (pos2 != -1) {
            // Parse seconds
            seconds = token.GetBase10(pos2 + 1, -1) ?? -1;
            if (seconds < 0 || seconds > 60)
                return false;
        }

        var isAlternateMidnight = hour == 24;
        var alternateMidnightHasNonzeroTime = minute != 0 || seconds > 0;
        var alternateMidnightHasSuffix = !token.IsBase10OnRight;
        var alternateMidnightIsInvalid = isAlternateMidnight &&
                                         (alternateMidnightHasNonzeroTime || alternateMidnightHasSuffix);
        if (alternateMidnightIsInvalid)
            return false;

        // Convert to 24h format
        if (!token.IsBase10OnRight) {
            var suffix = token[token.Length - 2];
            var suffixIsPm = suffix == 'P' || suffix == 'p';
            var suffixIsAm = suffix == 'A' || suffix == 'a';
            if (suffixIsPm) {
                if (hour < 12)
                    hour += 12;
            }
            else if (suffixIsAm) {
                if (hour == 12)
                    hour = 0;
            }
            else {
                return false;
            }
        }

        if (isAlternateMidnight)
            hour = 0;

        var time = new DateTime(info.Time.Value.Year, info.Time.Value.Month, info.Time.Value.Day,
            hour >= 0 ? (int)hour : 0, (int)minute, seconds >= 0 ? (int)seconds : 0, DateTimeKind.Utc);
        info.Time = isAlternateMidnight ? time.AddDays(1) : time;
        return true;
    }

    protected static DateTime? ParseShortDate(FtpListingToken token, bool saneFieldOrder = false) {
        if (token.Length < 1) {
            return null;
        }

        bool gotYear = false;
        bool gotMonth = false;
        bool gotDay = false;
        bool gotMonthName = false;

        long year = 0;
        long month = 0;
        long day = 0;
        long value;

        int pos = token.Find("-./");
        if (pos < 1) {
            return null;
        }

        if (!token.Chunk(0, pos).IsBase10) {
            // Seems to be monthname-dd-yy

            // Check month name
            var dateMonth = token.ToString().Substring(0, pos);
            var m = GetMonthFromName(dateMonth);
            if (m == null) {
                return null;
            }
            month = m.Value;
            gotMonth = true;
            gotMonthName = true;
        }
        else if (pos == 4) {
            // Seems to be yyyy-mm-dd
            year = token.GetBase10(0, pos) ?? -1;
            if (year < 1900 || year > 3000) {
                return null;
            }
            gotYear = true;
        }
        else if (pos <= 2) {
            value = token.GetBase10(0, pos) ?? -1;
            if (token[pos] == '.') {
                // Maybe dd.mm.yyyy
                if (value < 1 || value > 31) {
                    return null;
                }
                day = value;
                gotDay = true;
            }
            else {
                if (saneFieldOrder) {
                    year = value;
                    if (year < 50) {
                        year += 2000;
                    }
                    else {
                        year += 1900;
                    }
                    gotYear = true;
                }
                else {
                    // Detect mm-dd-yyyy or mm/dd/yyyy and
                    // dd-mm-yyyy or dd/mm/yyyy
                    if (value < 1) {
                        return null;
                    }
                    if (value > 12) {
                        if (value > 31) {
                            return null;
                        }

                        day = value;
                        gotDay = true;
                    }
                    else {
                        month = value;
                        gotMonth = true;
                    }
                }
            }
        }
        else {
            return null;
        }

        int pos2 = token.Find("-./", pos + 1);
        if (pos2 == -1 || pos2 - pos == 1) {
            return null;
        }
        if (pos2 == token.Length - 1) {
            return null;
        }

        // If we already got the month and the second field is not numeric,
        // change old month into day and use new token as month
        if (!token.Chunk(pos + 1, pos2 - pos - 1).IsBase10 && gotMonth) {
            if (gotMonthName) {
                return null;
            }

            if (gotDay) {
                return null;
            }

            gotDay = true;
            gotMonth = false;
            day = month;
        }

        if (gotYear || gotDay) {
            // Month field in yyyy-mm-dd or dd-mm-yyyy
            // Check month name
            var dateMonth = token.ToString().Substring(pos + 1, pos2 - pos - 1);
            var m = GetMonthFromName(dateMonth);
            if (m == null) {
                return null;
            }
            month = m.Value;
            gotMonth = true;
        }
        else {
            value = token.GetBase10(pos + 1, pos2 - pos - 1) ?? -1;
            // Day field in mm-dd-yyyy
            if (value < 1 || value > 31) {
                return null;
            }
            day = value;
            gotDay = true;
        }

        value = token.GetBase10(pos2 + 1, token.Length - pos2 - 1) ?? -1;
        if (gotYear) {
            // Day field in yyy-mm-dd
            if (value <= 0 || value > 31) {
                return null;
            }
            day = value;
            gotDay = true;
        }
        else {
            if (value < 0 || value > 9999) {
                return null;
            }

            if (value < 50) {
                value += 2000;
            }
            else if (value < 1000) {
                value += 1900;
            }
            year = value;

            gotYear = true;
        }

        if (!gotYear || !gotMonth || !gotDay) {
            return null;
        }

        return new DateTime((int)year, (int)month, (int)day, 0, 0, 0, DateTimeKind.Utc);
    }

    internal ParseStatus Parse(IReadOnlyList<FtpListingItem> items,
                               FtpListingContext context,
                               out DirectoryListItem item) {
        item = null;
        var info = Build(items, context);
        if (info is null) {
            return ParseStatus.NoMatch;
        }
        if (info.IsSkipped) {
            return ParseStatus.Skipped;
        }
        item = info.Build(Name);
        return ParseStatus.Parsed;
    }

    protected abstract FtpListingInfoBuilder Build(IReadOnlyList<FtpListingItem> items, FtpListingContext context);

    public string Name => _Name ??= GetType().Name[..^"ListingParser".Length].ToUpperInvariant();

    public sealed override string ToString() {
        return Name;
    }
}
