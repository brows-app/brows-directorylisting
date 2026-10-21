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
/// Represents one entry parsed from a directory listing.
/// </summary>
public sealed class DirectoryListItem {
    /// <summary>
    /// Gets the final dot and all following characters in the entry name, or an empty string
    /// when the name contains no dot. A dotfile such as ".gitignore" returns its full name.
    /// </summary>
    public string Extension => _Extension ??= new Func<string>(() => {
        var i = Name.LastIndexOf('.');
        if (i >= 0) {
            return Name.Substring(i);
        }
        return "";
    })();
    private string _Extension;

    /// <summary>
    /// Gets the size reported for the entry, when available.
    /// </summary>
    /// <remarks>
    /// Units depend on the listing format. Most formats report bytes, but some MVS formats report
    /// record counts. Parser results usually use -1 when the listing does not report a size, but
    /// some formats use synthetic values; for example, MVS sequential entries use 100. Manually
    /// constructed entries may also have a null value.
    /// </remarks>
    public long? Size { get; }

    /// <summary>
    /// Gets the entry name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the permissions text reported for the entry, when available.
    /// </summary>
    public string Permissions { get; }

    /// <summary>
    /// Gets the owner and group text reported for the entry, when available.
    /// </summary>
    public string OwnerGroup { get; }

    /// <summary>
    /// Gets the target path of a symbolic link, when available.
    /// </summary>
    /// <remarks>
    /// A null or empty constructor value is exposed as <see langword="null"/>.
    /// Unix link targets preserve literal trailing slash, asterisk, and pipe characters.
    /// </remarks>
    public string Target { get; }

    /// <summary>
    /// Gets the parsed timestamp for the entry, when available.
    /// </summary>
    /// <remarks>
    /// Parser-created timestamps have <see cref="DateTimeKind.Utc"/>. For formats that report
    /// server-local time, configure <see cref="DirectoryListOptions.ListItemTimeOffset"/>
    /// when calling <see cref="DirectoryList.Parse(System.Collections.Generic.IAsyncEnumerable{string},DirectoryListOptions,CancellationToken)"/>.
    /// Use <see cref="DirectoryListOptions.ServerTimeOffset"/> to apply an additional
    /// server-clock correction to all timestamps.
    /// </remarks>
    public DateTime? Time { get; }

    /// <summary>
    /// Gets the flags that describe the entry type.
    /// </summary>
    public DirectoryListItemFlags Flags { get; }

    /// <summary>
    /// Gets the name of the parser that recognized the entry, when available.
    /// </summary>
    public string Parser { get; }

    /// <summary>
    /// Initializes a new directory entry with the supplied parsed values.
    /// </summary>
    /// <param name="name">
    /// The entry name. This value cannot be <see langword="null"/>.
    /// </param>
    /// <param name="size">
    /// The size reported by the listing, in units used by that format. Parser results usually use
    /// -1 when the listing does not report a size, but some formats use synthetic values. Null is
    /// also allowed for manually constructed entries.
    /// </param>
    /// <param name="permissions">
    /// The permissions text, when available.
    /// </param>
    /// <param name="ownerGroup">
    /// The owner and group text, when available.
    /// </param>
    /// <param name="target">
    /// The target path for a symbolic link, when available. A null or empty value indicates
    /// that no target is available.
    /// </param>
    /// <param name="time">
    /// The timestamp for the entry, when available. Parser-created timestamps have
    /// <see cref="DateTimeKind.Utc"/>.
    /// </param>
    /// <param name="flags">
    /// The flags that describe the entry type.
    /// </param>
    /// <param name="parser">
    /// The name of the parser that recognized the entry, when available.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    public DirectoryListItem(string name,
                             long? size,
                             string permissions,
                             string ownerGroup,
                             string target,
                             DateTime? time,
                             DirectoryListItemFlags flags,
                             string parser = null) {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Size = size;
        Permissions = permissions;
        OwnerGroup = ownerGroup;
        Target = string.IsNullOrEmpty(target) ? null : target;
        Time = time;
        Flags = flags;
        Parser = parser;
    }

    /// <summary>
    /// Returns the entry name.
    /// </summary>
    /// <returns>
    /// The value of <see cref="Name"/>.
    /// </returns>
    public sealed override string ToString() {
        return Name;
    }
}
