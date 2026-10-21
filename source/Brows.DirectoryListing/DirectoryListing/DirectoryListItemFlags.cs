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

namespace Brows.DirectoryListing;

/// <summary>
/// Identifies the type or link status of a directory listing entry.
/// </summary>
[Flags]
public enum DirectoryListItemFlags {
    /// <summary>
    /// Indicates a regular file. This is the zero value and also represents no flags set.
    /// </summary>
    File = 0,

    /// <summary>
    /// Indicates a directory.
    /// </summary>
    Directory = 1,

    /// <summary>
    /// Indicates a symbolic link. Link entries may also include the
    /// <see cref="Directory"/> flag.
    /// </summary>
    Link = 2
}
