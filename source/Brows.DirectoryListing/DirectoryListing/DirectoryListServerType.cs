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
/// Specifies the server-specific listing formats to use when parsing a directory listing.
/// </summary>
public enum DirectoryListServerType {
    /// <summary>
    /// Uses the general listing formats without server-specific parser overrides.
    /// </summary>
    Default,

    /// <summary>
    /// Enables IBM MVS migrated, PDS2, and tape listing formats.
    /// </summary>
    Mvs,

    /// <summary>
    /// Tries the HP NonStop listing parser before general formats.
    /// </summary>
    HpNonStop,

    /// <summary>
    /// Tries the z/VM listing parser before general formats.
    /// </summary>
    Zvm
}
