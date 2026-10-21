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

namespace Brows.DirectoryListing;

/// <summary>
/// Provides methods for parsing FTP directory listings.
/// </summary>
/// <remarks>
/// Input is already-decoded text. CR, LF, and NUL delimit physical lines, with
/// CRLF treated as one delimiter. A leading BOM is removed from each line.
/// Lines longer than 10,000 UTF-8 bytes cause enumeration to fail.
/// Entries named "." or ".." are excluded, including from raw filename lists.
/// Unicode whitespace is retained in filenames; only ASCII spaces and tabs are separators.
/// </remarks>
public static class DirectoryList {
    /// <summary>
    /// Parses chunks of FTP directory listing text into directory entries.
    /// </summary>
    /// <param name="contents">
    /// An asynchronous sequence of text chunks containing the listing.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel enumeration of the listing.
    /// </param>
    /// <returns>
    /// An asynchronous sequence of parsed directory entries.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="contents"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// A physical listing line exceeds 10,000 UTF-8 bytes.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Enumeration is canceled through <paramref name="cancellationToken"/> or the input sequence.
    /// </exception>
    public static IAsyncEnumerable<DirectoryListItem> Parse(IAsyncEnumerable<string> contents,
                                                               CancellationToken cancellationToken) {
        return Parse(contents, DirectoryListServerType.Default, cancellationToken);
    }

    /// <summary>
    /// Parses chunks of FTP directory listing text using the selected server-specific formats.
    /// </summary>
    /// <param name="contents">
    /// An asynchronous sequence of text chunks containing the listing.
    /// </param>
    /// <param name="serverType">
    /// The server type used to select server-specific listing parsers.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel enumeration of the listing.
    /// </param>
    /// <returns>
    /// An asynchronous sequence of parsed directory entries.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="contents"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="serverType"/> is not a defined value.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// A physical listing line exceeds 10,000 UTF-8 bytes.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Enumeration is canceled through <paramref name="cancellationToken"/> or the input sequence.
    /// </exception>
    public static IAsyncEnumerable<DirectoryListItem> Parse(IAsyncEnumerable<string> contents,
                                                               DirectoryListServerType serverType,
                                                               CancellationToken cancellationToken) {
        return Parse(contents, serverType, TimeSpan.Zero, TimeSpan.Zero, cancellationToken);
    }

    /// <summary>
    /// Parses listing text and adjusts timestamps with the supplied offsets.
    /// </summary>
    /// <param name="contents">
    /// An asynchronous sequence of text chunks containing the listing.
    /// </param>
    /// <param name="serverType">
    /// The server type used to select server-specific listing parsers.
    /// </param>
    /// <param name="listingTimeOffset">
    /// An offset added by parsers to formats whose timestamps are treated as server-local. For a
    /// server-local time that is UTC+02:00, pass a negative two-hour offset to convert it to UTC.
    /// UTC-valued formats are not changed by this setting.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel enumeration of the listing.
    /// </param>
    /// <returns>
    /// An asynchronous sequence of parsed directory entries.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="contents"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="serverType"/> is not a defined value.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// A physical listing line exceeds 10,000 UTF-8 bytes.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Enumeration is canceled through <paramref name="cancellationToken"/> or the input sequence.
    /// </exception>
    public static IAsyncEnumerable<DirectoryListItem> Parse(IAsyncEnumerable<string> contents,
                                                               DirectoryListServerType serverType,
                                                               TimeSpan listingTimeOffset,
                                                               CancellationToken cancellationToken) {
        return Parse(contents, serverType, listingTimeOffset, TimeSpan.Zero, cancellationToken);
    }

    /// <summary>
    /// Parses listing text and applies listing-time and server-clock adjustments to timestamps.
    /// </summary>
    /// <param name="contents">
    /// An asynchronous sequence of text chunks containing the listing.
    /// </param>
    /// <param name="serverType">
    /// The server type used to select server-specific listing parsers.
    /// </param>
    /// <param name="listingTimeOffset">
    /// An offset added by parsers to formats whose timestamps are treated as server-local. For a
    /// server-local time that is UTC+02:00, pass a negative two-hour offset to convert it to UTC.
    /// UTC-valued formats are not changed by this setting.
    /// </param>
    /// <param name="serverTimeOffset">
    /// An additional offset added to every reported timestamp, including UTC-valued timestamps, to
    /// correct the server clock.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel enumeration of the listing.
    /// </param>
    /// <returns>
    /// An asynchronous sequence of parsed directory entries.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="contents"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="serverType"/> is not a defined value.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// A physical listing line exceeds 10,000 UTF-8 bytes.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Enumeration is canceled through <paramref name="cancellationToken"/> or the input sequence.
    /// </exception>
    public static IAsyncEnumerable<DirectoryListItem> Parse(IAsyncEnumerable<string> contents,
                                                               DirectoryListServerType serverType,
                                                               TimeSpan listingTimeOffset,
                                                               TimeSpan serverTimeOffset,
                                                               CancellationToken cancellationToken) {
        var parser = new FtpListingParser(
            serverType,
            listingTimeOffset: listingTimeOffset,
            serverTimeOffset: serverTimeOffset);
        var result = parser.ParseContents(contents, cancellationToken);
        return result;
    }
}
