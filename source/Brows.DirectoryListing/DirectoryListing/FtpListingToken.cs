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
using System.Linq;

namespace Brows.DirectoryListing;

internal sealed class FtpListingToken {
    private Dictionary<string, FtpListingToken> Chunks;

    private long? GetBase16() {
        const long max = long.MaxValue;
        const long max16 = max / 16;
        long num = 0;
        for (var i = 0; i < View.Length; ++i) {
            var c = View[i];
            long digit;
            if (c >= '0' && c <= '9') {
                digit = c - '0';
            }
            else if (c >= 'a' && c <= 'f') {
                digit = c - 'a' + 10;
            }
            else if (c >= 'A' && c <= 'F') {
                digit = c - 'A' + 10;
            }
            else {
                return null;
            }

            if (num > max16) {
                return null;
            }
            num *= 16;
            if (max - digit < num) {
                return null;
            }
            num += digit;
        }
        return num;
    }

    private long? GetBase10OnLeft() {
        const long max = long.MaxValue;
        const long max10 = max / 10;
        long num = 0;
        for (var i = 0; i < View.Length; ++i) {
            if (View[i] < '0' || View[i] > '9') {
                break;
            }

            var digit = View[i] - '0';
            if (num > max10) {
                return null;
            }
            num *= 10;
            if (max - digit < num) {
                return null;
            }
            num += digit;
        }
        return num;
    }

    private long? GetBase10OnRight() {
        const long max = long.MaxValue;
        const long max10 = max / 10;
        long num = 0;
        var start = View.Length - 1;
        while (View[start - 1] >= '0' && View[start - 1] <= '9') {
            --start;
        }
        for (var i = start; i < View.Length; ++i) {
            var digit = View[i] - '0';
            if (num > max10) {
                return null;
            }
            num *= 10;
            if (max - digit < num) {
                return null;
            }
            num += digit;
        }
        return num;
    }

    internal long? GetBase10(int start, int length) {
        var startIsInvalid = start < 0 || start >= View.Length;
        if (startIsInvalid)
            return null;

        if (length == -1)
            length = View.Length - start;

        var lengthIsInvalid = length < 1 || length > View.Length - start;
        if (lengthIsInvalid)
            return null;

        var firstCharacterIsNotNumeric = View[start] < '0' || View[start] > '9';
        if (firstCharacterIsNotNumeric)
            return null;

        const long max = long.MaxValue;
        const long max10 = max / 10;
        long number = 0;
        var end = start + length;
        for (var i = start; i < end; ++i) {
            var character = View[i];
            if (character < '0' || character > '9')
                break;

            var digit = character - '0';
            if (number > max10)
                return null;

            number *= 10;
            if (max - digit < number)
                return null;

            number += digit;
        }

        return number;
    }

    public char this[int n] => View[n];

    public bool IsBase10 => _IsBase10 ??= View.All(c => '0' <= c && c <= '9');
    private bool? _IsBase10;

    public bool IsBase10OnLeft => _IsBase10OnLeft ??= !(View.Length < 2 || View[0] < '0' || View[0] > '9');
    private bool? _IsBase10OnLeft;

    public bool IsBase10OnRight => _IsBase10OnRight ??= !(View.Length < 2 || View[^1] < '0' || View[^1] > '9');
    private bool? _IsBase10OnRight;

    public bool IsBase16 => _IsBase16 ??= View.All(c => '0' <= c && c <= '9' || 'A' <= c && c <= 'F' || 'a' <= c && c <= 'f');
    private bool? _IsBase16;

    public long? Base10 => _Base10 ??=
        IsBase10 || IsBase10OnLeft ? GetBase10OnLeft() :
        IsBase10OnRight ? GetBase10OnRight() :
        null;
    private long? _Base10;

    public long? Base16 => _Base16 ??= IsBase16 ? GetBase16() : null;
    private long? _Base16;

    public string Data { get; }
    public string View { get; }
    public int Start { get; }
    public int Length { get; }

    public FtpListingToken(string data, int start, int length) {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        View = data.Substring(start, length);
        Start = start;
        Length = length;
    }

    public FtpListingToken(string data) {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        View = Data;
        Start = 0;
        Length = View.Length;
    }

    public FtpListingToken Chunk(int start, int length) {
        if (length < 0) {
            length = View.Length - start;
        }
        var key = $"{start}..{length}";
        var chunks = Chunks ??= [];
        if (chunks.TryGetValue(key, out var chunk) == false) {
            chunks[key] = chunk = new FtpListingToken(View, start, length);
        }
        return chunk;
    }

    public int Find(string any, int start = 0) {
        if (null == any) {
            return -1;
        }
        for (var i = start; i < View.Length; ++i) {
            for (var c = 0; c < any.Length; ++c) {
                if (View[i] == any[c]) {
                    return i;
                }
            }
        }
        return -1;
    }

    public int Find(char c, int start = 0) {
        for (var i = start; i < View.Length; ++i) {
            if (View[i] == c) {
                return i;
            }
        }
        return -1;
    }

    public sealed override string ToString() {
        return View;
    }
}
