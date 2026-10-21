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
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Brows.DirectoryListing;

internal abstract class DirectoryListParser<T> {
    private const int MaximumLineLengthInUtf8Bytes = 10_000;

    private static int GetUtf8ByteCount(char character, ref bool hasPendingHighSurrogate) {
        // Count a pending high surrogate as a three-byte replacement character.
        // A following low surrogate adds one more byte to complete the pair.
        if (hasPendingHighSurrogate) {
            hasPendingHighSurrogate = false;
            if (char.IsLowSurrogate(character)) {
                return 1;
            }
        }

        if (char.IsHighSurrogate(character)) {
            hasPendingHighSurrogate = true;
            return 3;
        }
        if (char.IsLowSurrogate(character)) {
            return 3;
        }
        if (character <= 0x7F) {
            return 1;
        }
        if (character <= 0x7FF) {
            return 2;
        }
        return 3;
    }

    private static void EnsureLineLengthIsWithinLimit(int byteCount) {
        if (byteCount > MaximumLineLengthInUtf8Bytes) {
            throw new InvalidDataException(
                $"A listing line exceeds {MaximumLineLengthInUtf8Bytes} UTF-8 bytes.");
        }
    }

    protected abstract IAsyncEnumerable<T> ParseLines(IAsyncEnumerable<string> lines, CancellationToken token);

    protected virtual void OnParseError(ErrorEventArgs e) {
        ParseError?.Invoke(this, e);
    }

    public event ErrorEventHandler ParseError;

    public IAsyncEnumerable<T> ParseContents(IAsyncEnumerable<string> contents, CancellationToken token) {
        if (contents is null) {
            throw new ArgumentNullException(nameof(contents));
        }
        async IAsyncEnumerable<string> lines([EnumeratorCancellation] CancellationToken enumerationToken = default) {
            static string StripLeadingBom(string line) {
                var lineHasLeadingBom = line.Length > 0 && line[0] == '\uFEFF';
                if (lineHasLeadingBom != true) {
                    return line;
                }
                return line[1..];
            }

            var sb = new StringBuilder();
            var lineLengthInUtf8Bytes = 0;
            var hasPendingHighSurrogate = false;
            var skipLineFeed = false;
            await foreach (var content in contents.WithCancellation(enumerationToken)) {
                if (content is null) {
                    continue;
                }

                foreach (var c in content) {
                    var characterCompletesCrLf = skipLineFeed && c == '\n';
                    skipLineFeed = false;
                    if (characterCompletesCrLf) {
                        continue;
                    }

                    var characterIsLineTerminator = c == '\r' || c == '\n' || c == '\0';
                    if (characterIsLineTerminator) {
                        hasPendingHighSurrogate = false;
                        var line = sb.ToString();
                        sb.Clear();
                        lineLengthInUtf8Bytes = 0;
                        skipLineFeed = c == '\r';
                        yield return StripLeadingBom(line);
                        continue;
                    }

                    lineLengthInUtf8Bytes += GetUtf8ByteCount(c, ref hasPendingHighSurrogate);
                    EnsureLineLengthIsWithinLimit(lineLengthInUtf8Bytes);
                    sb.Append(c);
                }
            }
            if (sb.Length > 0) {
                yield return StripLeadingBom(sb.ToString());
            }
        }
        return ParseLines(lines(token), token);
    }
}
