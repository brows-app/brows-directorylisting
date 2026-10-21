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

internal sealed class MlsdListingParser : FtpListingItemParser {
    private static bool TryParseDigits(string value, ref int position, int count, out int number) {
        while (position < value.Length && (value[position] < '0' || value[position] > '9')) {
            ++position;
        }

        if (value.Length - position < count) {
            number = 0;
            return false;
        }

        number = 0;
        var end = position + count;
        while (position < end) {
            var digit = value[position];
            if (digit < '0' || digit > '9') {
                number = 0;
                return false;
            }

            number = number * 10 + digit - '0';
            ++position;
        }

        return true;
    }

    private static DateTime? Time(string s) {
        try {
            var position = 0;
            if (!TryParseDigits(s, ref position, 4, out var year)) {
                return null;
            }
            if (!TryParseDigits(s, ref position, 2, out var month)) {
                return null;
            }
            if (!TryParseDigits(s, ref position, 2, out var day)) {
                return null;
            }

            var hour = 0;
            var minute = 0;
            var second = 0;
            var millisecond = 0;
            var hasHour = TryParseDigits(s, ref position, 2, out hour);
            if (hasHour) {
                var hasMinute = TryParseDigits(s, ref position, 2, out minute);
                if (hasMinute) {
                    var hasSecond = TryParseDigits(s, ref position, 2, out second);
                    if (hasSecond) {
                        // FileZilla only reads milliseconds when all three digits are present.
                        TryParseDigits(s, ref position, 3, out millisecond);
                    }
                }
            }

            return new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Utc);
        }
        catch {
            return null;
        }
    }

    private static FtpListingInfoBuilder Build(FtpListingItem line, FtpListingContext context) {
        FtpListingToken token = line.GetToken(0);
        if (null == token) {
            return null;
        }

        var facts = token.View;
        if (string.IsNullOrEmpty(facts)) {
            return null;
        }

        var info = new FtpListingInfoBuilder();
        info.Flags = 0;
        info.Size = -1;
        info.Time = null;
        info.Target = null;

        string owner = "", ownername = "", group = "", groupname = "", user = "", uid = "", gid = "";
        string ownerGroup = "";
        string permissions = "";

        var start = 0;
        while (start < facts.Length) {
            var delim = facts.IndexOf(';', start);
            if (delim == -1) {
                delim = facts.Length;
            }
            else if (delim < start + 3) {
                return null;
            }

            var pos = facts.IndexOf('=', start);
            if (pos == -1 || pos < start + 1 || pos > delim) {
                return null;
            }

            var factname = facts.Substring(start, pos - start).ToLowerInvariant();
            var value = facts.Substring(pos + 1, delim - pos - 1);
            if (factname == "type") {
                var colonPos = value.IndexOf(':');
                string valuePrefix;
                if (colonPos == -1) {
                    valuePrefix = value.ToLowerInvariant();
                }
                else {
                    valuePrefix = value.Substring(0, colonPos).ToLowerInvariant();
                }

                if (valuePrefix == "dir" && colonPos == -1) {
                    info.Flags |= DirectoryListItemFlags.Directory;
                }

                else if (valuePrefix == "os.unix=slink" || valuePrefix == "os.unix=symlink") {
                    info.Flags |= DirectoryListItemFlags.Directory | DirectoryListItemFlags.Link;
                    if (colonPos != -1) {
                        var target = value.Substring(colonPos + 1);
                        info.Target = target;
                    }
                }

                else if ((valuePrefix == "cdir" || valuePrefix == "pdir") && colonPos == -1) {
                    // Current and parent directory, don't parse it
                    return new FtpListingInfoBuilder { IsSkipped = true };
                }
            }

            else if (factname == "size") {
                long size = 0;
                const long max = long.MaxValue;
                const long max10 = max / 10;

                for (var i = 0; i < value.Length; ++i) {
                    var character = value[i];
                    if (character < '0' || character > '9') {
                        return null;
                    }

                    var digit = character - '0';
                    if (size > max10) {
                        return null;
                    }
                    size *= 10;
                    if (max - digit < size) {
                        return null;
                    }
                    size += digit;
                }

                info.Size = size;
            }

            else if (factname == "modify" ||
                    info.Time == null && factname == "create") {
                info.Time = Time(value);
                if (info.Time == null) {
                    return null;
                }
            }

            else if (factname == "perm") {
                if (!string.IsNullOrEmpty(value)) {
                    if (!string.IsNullOrEmpty(permissions)) {
                        string tmp;
                        tmp = value;
                        tmp += " (";
                        tmp += permissions;
                        tmp += ")";
                        permissions = tmp;
                    }
                    else {
                        permissions = value;
                    }
                }
            }

            else if (factname == "unix.mode") {
                if (!string.IsNullOrEmpty(permissions)) {
                    permissions += " (";
                    permissions += value;
                    permissions += ")";
                }
                else {
                    permissions = value;
                }
            }

            else if (factname == "unix.owner") {
                owner = value;
            }

            else if (factname == "unix.ownername") {
                ownername = value;
            }

            else if (factname == "unix.group") {
                group = value;
            }

            else if (factname == "unix.groupname") {
                groupname = value;
            }

            else if (factname == "unix.user") {
                user = value;
            }

            else if (factname == "unix.uid") {
                uid = value;
            }

            else if (factname == "unix.gid") {
                gid = value;
            }

            start = delim + 1;
        }

        // The order of the facts is undefined, so assemble ownerGroup in correct
        // order
        if (!string.IsNullOrEmpty(ownername)) {
            ownerGroup = ownername;
        }
        else if (!string.IsNullOrEmpty(owner)) {
            ownerGroup = owner;
        }
        else if (!string.IsNullOrEmpty(user)) {
            ownerGroup = user;
        }
        else if (!string.IsNullOrEmpty(uid)) {
            ownerGroup = uid;
        }

        if (!string.IsNullOrEmpty(groupname)) {
            ownerGroup += ' ';
            ownerGroup += groupname;
        }
        else if (!string.IsNullOrEmpty(group)) {
            ownerGroup += ' ';
            ownerGroup += group;
        }
        else if (!string.IsNullOrEmpty(gid)) {
            ownerGroup += ' ';
            ownerGroup += gid;
        }

        var nameToken = line.GetEndToken(1, true);
        if (null == nameToken) {
            return null;
        }

        info.Name = nameToken.ToString();
        info.OwnerGroup = ownerGroup;
        info.Permissions = permissions;

        return info;
    }

    protected sealed override FtpListingInfoBuilder Build(IReadOnlyList<FtpListingItem> items, FtpListingContext context) {
        return items?.Count == 1 ? Build(items[0], context) : null;
    }
}
