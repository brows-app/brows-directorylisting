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

internal sealed class FtpListingItem {
    private IReadOnlyList<FtpListingToken> Tokens => _Tokens ??= Split().ToList();
    private IReadOnlyList<FtpListingToken> _Tokens;

    private IEnumerable<FtpListingToken> Split() {
        var start = default(int?);
        for (var i = 0; i < Data.Length; i++) {
            if (Data[i] == ' ' || Data[i] == '\t') {
                if (start.HasValue) {
                    yield return new FtpListingToken(Data, start.Value, i - start.Value);
                }
                start = null;
            }
            else {
                if (start == null) {
                    start = i;
                }
            }
        }
        if (start.HasValue) {
            yield return new FtpListingToken(Data, start.Value, Data.Length - start.Value);
        }
    }

    public FtpListingToken this[int n] => GetToken(n);

    public string Data { get; }

    public FtpListingItem(string data) {
        Data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public FtpListingToken GetToken(int n) {
        return Tokens.Count > n
            ? Tokens[n]
            : null;
    }

    public FtpListingToken GetToken(int n, bool to_end = false, bool include_whitespace = false) {
        if (to_end) {
            return GetEndToken(n, include_whitespace);
        }
        else {
            return GetToken(n);
        }
    }

    public FtpListingToken GetEndToken(int n, bool includeWhitespace = false) {
        if (includeWhitespace) {
            var previousToken = GetToken(n == 0 ? 0 : n - 1);
            if (previousToken is null) {
                return null;
            }

            var start = n == 0
                ? previousToken.Start
                : previousToken.Start + previousToken.Length + 1;
            if (start >= Data.Length) {
                return null;
            }

            return new FtpListingToken(Data, start, Data.Length - start);
        }

        var token = GetToken(n);
        if (token is null) {
            return null;
        }

        var length = Data.TrimEnd(' ', '\t').Length - token.Start;

        return new FtpListingToken(Data, token.Start, length);
    }

    public sealed override string ToString() {
        return Data;
    }
}
