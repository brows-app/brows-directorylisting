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

internal sealed class FtpListingInfoBuilder {
    internal bool IsSkipped { get; set; }

    public string Name { get; set; }
    public long? Size { get; set; }
    public string Permissions { get; set; }
    public string OwnerGroup { get; set; }
    public string Target { get; set; }
    public DateTime? Time { get; set; }
    public DirectoryListItemFlags Flags { get; set; }

    public DirectoryListItem Build(string parser) {
        return new DirectoryListItem(Name, Size, Permissions, OwnerGroup, Target, Time, Flags, parser);
    }
}
