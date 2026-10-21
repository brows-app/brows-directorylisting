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

namespace Brows.DirectoryListing;

/// <summary>
/// Specifies the server type and timestamp adjustments used to parse directory listings.
/// </summary>
/// <remarks>
/// The default options use general listing formats and apply no timestamp adjustments.
/// Pass an instance to <see cref="DirectoryList.Parse(System.Collections.Generic.IAsyncEnumerable{string},DirectoryListOptions,CancellationToken)"/>
/// to configure parsing.
/// </remarks>
public sealed record DirectoryListOptions {
    /// <summary>
    /// Gets the server type used to select server-specific listing parsers.
    /// </summary>
    /// <value>
    /// The default is <see cref="DirectoryListServerType.Default"/>.
    /// </value>
    public DirectoryListServerType ServerType { get; init; } = DirectoryListServerType.Default;

    /// <summary>
    /// Gets the offset added to listing timestamps in formats treated as server-local.
    /// </summary>
    /// <remarks>
    /// For a server-local time that is UTC+02:00, use a negative two-hour offset to convert it
    /// to UTC. UTC-valued formats such as MLSD are not changed by this setting. The parser
    /// does not infer the server's timezone.
    /// </remarks>
    /// <value>
    /// The default is <see cref="TimeSpan.Zero"/>.
    /// </value>
    public TimeSpan ListItemTimeOffset { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Gets the additional offset added to every reported timestamp to correct the server clock.
    /// </summary>
    /// <remarks>
    /// This offset applies after <see cref="ListItemTimeOffset"/>, including to UTC-valued
    /// timestamps such as MLSD timestamps. Entries without a timestamp are unaffected.
    /// </remarks>
    /// <value>
    /// The default is <see cref="TimeSpan.Zero"/>.
    /// </value>
    public TimeSpan ServerTimeOffset { get; init; } = TimeSpan.Zero;
}
