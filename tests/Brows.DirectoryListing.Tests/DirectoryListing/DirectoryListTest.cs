// This file is part of Brows.DirectoryListing.
//
// The test listings are taken from the FileZilla client's directory listing
// parser tests. Some of those samples are themselves taken from D. J.
// Bernstein's ftpparse, as FileZilla's sources state: "Some samples are from
// https://cr.yp.to/ftpparse/ftpparse.c".
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

using Brows.DirectoryListing.ListingItemParsers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Brows.DirectoryListing;

[TestFixture]
public sealed class DirectoryListTest {
    private static readonly DateTime TestUtcNow = new(2026, 1, 12, 0, 30, 0, DateTimeKind.Utc);

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source) {
        var items = new List<T>();
        await foreach (var item in source) {
            items.Add(item);
        }
        return items;
    }

    private static async Task<List<DirectoryListItem>> ParseListingAsync(params string[] lines) {
        return await ParseListingAsync(DirectoryListServerType.Default, lines);
    }

    private static async Task<List<DirectoryListItem>> ParseListingAsync(DirectoryListServerType serverType,
                                                                           params string[] lines) {
        return await ParseListingAsync(serverType, lines, null);
    }

    private static async Task<List<DirectoryListItem>> ParseListingWithFixedUtcClockAsync(
        DirectoryListServerType serverType, params string[] lines) {
        return await ParseListingAsync(serverType, lines, TestUtcNow);
    }

    private static async Task<List<DirectoryListItem>> ParseListingAsync(DirectoryListServerType serverType,
                                                                           string[] lines,
                                                                           DateTime? utcNow,
                                                                           TimeSpan listingTimeOffset = default,
                                                                           TimeSpan serverTimeOffset = default) {
        async IAsyncEnumerable<string> contents() {
            foreach (var line in lines) {
                yield return line;
                yield return "\r\n";
                await Task.CompletedTask;
            }
        }

        var parsedContents = contents();
        if (utcNow.HasValue) {
            var parser = new FtpListingParser(
                serverType,
                () => utcNow.Value,
                listingTimeOffset,
                serverTimeOffset);
            return await ToListAsync(parser.ParseContents(parsedContents, default));
        }
        return await ToListAsync(DirectoryList.Parse(
            parsedContents,
            serverType,
            listingTimeOffset,
            serverTimeOffset,
            default));
    }

    private static async Task<List<DirectoryListItem>> ParseChunksAsync(params string[] chunks) {
        async IAsyncEnumerable<string> contents() {
            foreach (var chunk in chunks) {
                yield return chunk;
                await Task.CompletedTask;
            }
        }

        return await ToListAsync(DirectoryList.Parse(contents(), default));
    }

    private static DirectoryListItem ParseUnixListing(string line, DateTime utcNow) {
        var parser = new UnixListingParser(expectDate: true, getUtcNow: () => utcNow);
        var status = parser.Parse(new[] { new FtpListingItem(line) }, new FtpListingContext(), out var item);
        Assert.That(status, Is.EqualTo(FtpListingItemParser.ParseStatus.Parsed));
        return item;
    }

    private static DirectoryListServerType ServerTypeForLine(string line) {
        var isMvsListing = line.StartsWith("Migrated", StringComparison.Ordinal) ||
                           line.StartsWith("67-MVSPDSMEMBER2", StringComparison.Ordinal) ||
                           line.StartsWith("68-MVSPDSMEMBER3", StringComparison.Ordinal) ||
                           line.StartsWith("V43525 Tape", StringComparison.Ordinal);
        if (isMvsListing) {
            return DirectoryListServerType.Mvs;
        }

        var isHpNonStopListing = line.StartsWith("57-HP_NonStop", StringComparison.Ordinal) ||
                                 line.StartsWith("58-HP_NonStop", StringComparison.Ordinal);
        if (isHpNonStopListing) {
            return DirectoryListServerType.HpNonStop;
        }

        if (line.StartsWith("70-ZVMFILE", StringComparison.Ordinal)) {
            return DirectoryListServerType.Zvm;
        }

        return DirectoryListServerType.Default;
    }

    private static async Task<List<DirectoryListItem>> ParseTestCasesAsync() {
        var actualItems = new DirectoryListItem[TestCases.Length];
        var testCases = TestCases.Select((testCase, index) => {
            var line = (string)((object[])testCase)[0];
            var serverType = ServerTypeForLine(line);
            var isUnstructuredSingleLine = line.IndexOf(' ') == -1 && line.IndexOf('\t') == -1;
            var groupName = isUnstructuredSingleLine ? $"{serverType}-{index}" : serverType.ToString();
            return new { Index = index, Line = line, ServerType = serverType, GroupName = groupName };
        });

        foreach (var group in testCases.GroupBy(testCase => testCase.GroupName)) {
            var groupItems = group.ToList();
            var lines = groupItems.Select(testCase => testCase.Line).ToArray();
            var parsedItems = await ParseListingWithFixedUtcClockAsync(groupItems[0].ServerType, lines);
            Assert.That(parsedItems.Count, Is.EqualTo(groupItems.Count),
                $"The {groupItems[0].ServerType} test listing produced an unexpected number of entries.");

            for (var i = 0; i < groupItems.Count; ++i) {
                actualItems[groupItems[i].Index] = parsedItems[i];
            }
        }

        return actualItems.ToList();
    }

    private async Task CompareAsync(string line, DirectoryListItem expectedInfo, Func<DirectoryListItem, object> callback) {
        var actualInfo = (await ParseListingWithFixedUtcClockAsync(ServerTypeForLine(line), line)).Single();
        var actualValue = callback(actualInfo);
        var expectedValue = callback(expectedInfo);
        Assert.That(actualValue, Is.EqualTo(expectedValue));
    }

    private async Task CompareAsync(int i, DirectoryListItem expectedInfo, Func<DirectoryListItem, object> callback) {
        var infos = await ParseTestCasesAsync();
        var actualInfo = infos[i];
        var actualValue = callback(actualInfo);
        var expectedValue = callback(expectedInfo);
        Assert.That(actualValue, Is.EqualTo(expectedValue));
    }

    private static int Year(int month, int day) {
        var currentDayOfYear = TestUtcNow.Day + 31 * (TestUtcNow.Month - 1);
        var fileDayOfYear = day + 31 * (month - 1);
        if (currentDayOfYear + 1 < fileDayOfYear) {
            return TestUtcNow.Year - 1;
        }
        return TestUtcNow.Year;
    }

    public static object[] TestCases = [
        new object[] {
            "dr-xr-xr-x   2 root     other        512 Apr  8  1994 01-unix-std dir", new DirectoryListItem(
                "01-unix-std dir",
                512,
                "dr-xr-xr-x",
                "root other",
                null,
                new DateTime(1994, 4, 8, 0, 0, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "-rw-r--r--   1 root     other        531 3 29 03:26 02-unix-std file", new DirectoryListItem(
                "02-unix-std file",
                531,
                "-rw-r--r--",
                "root other",
                null,
                new DateTime(Year(3, 29), 3, 29, 3, 26, 0, DateTimeKind.Utc),
                0),
        },
        new object[] {
            "dr-xr-xr-x   2 root                  512 Apr  8  1994 03-unix-nogroup dir", new DirectoryListItem(
                "03-unix-nogroup dir",
                512,
                "dr-xr-xr-x",
                "root",
                null,
                new DateTime(1994, 4, 8, 0, 0, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "lrwxrwxrwx   1 root     other          7 Jan 25 00:17 04-unix-std link -> usr/bin", new DirectoryListItem(
                "04-unix-std link",
                7,
                "lrwxrwxrwx",
                "root other",
                "usr/bin",
                new DateTime(Year(1, 25), 1, 25, 0, 17, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory | DirectoryListItemFlags.Link)
        },
        new object[] {
            "-rw-r--r--   1 root     other        531 09-26 2000 05-unix-date file", new DirectoryListItem(
                "05-unix-date file",
                531,
                "-rw-r--r--",
                "root other",
                null,
                new DateTime(2000, 9, 26, 0, 0, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "-rw-r--r--   1 root     other        531 09-26 13:45 06-unix-date file", new DirectoryListItem(
                "06-unix-date file",
                531,
                "-rw-r--r--",
                "root other",
                null,
                new DateTime(Year(9, 26), 9, 26, 13, 45, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "-rw-r--r--   1 root     other        531 2005-06-07 21:22 07-unix-date file", new DirectoryListItem(
                "07-unix-date file",
                531,
                "-rw-r--r--",
                "root other",
                null,
                new DateTime(2005, 6, 7, 21, 22, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "-rw-r--r--   1 root     other  33.5k Oct 5 21:22 08-unix-namedsize file", new DirectoryListItem(
                "08-unix-namedsize file",
                335 * 1024 / 10,
                "-rw-r--r--",
                "root other",
                null,
                new DateTime(Year(10, 5), 10, 5, 21, 22, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "d [R----F--] supervisor            512       Jan 16 18:53    09-netware dir", new DirectoryListItem(
                "09-netware dir",
                512,
                "d [R----F--]",
                "supervisor",
                null,
                new DateTime(Year(1, 16), 1, 16, 18, 53, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "- [R----F--] rhesus             214059       Oct 20 15:27    10-netware file", new DirectoryListItem(
                "10-netware file",
                214059,
                "- [R----F--]",
                "rhesus",
                null,
                new DateTime(Year(10, 20), 10, 20, 15, 27, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "-------r--         326  1391972  1392298 Nov 22  1995 11-netpresenz file", new DirectoryListItem(
                "11-netpresenz file",
                1392298,
                "-------r--",
                "1391972",
                null,
                new DateTime(1995, 11, 22, 0, 0, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "drwxrwxr-x               folder        2 May 10  1996 12-netpresenz dir", new DirectoryListItem(
                "12-netpresenz dir",
                2,
                "drwxrwxr-x",
                "folder",
                null,
                new DateTime(1996, 5, 10, 0, 0, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "-rw-r--r--   1 group domain user 531 Jan 29 03:26 13-unix-domain file", new DirectoryListItem(
                "13-unix-domain file",
                531,
                "-rw-r--r--",
                "group domain user",
                null,
                new DateTime(Year(1, 29), 1, 29, 3, 26, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "+i8388621.48594,m825718503,r,s280,up755\t14-eplf file", new DirectoryListItem(
                "14-eplf file",
                280,
                "755",
                "",
                null,
                new DateTime(1996, 3, 1, 22, 15, 3, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "+i8388621.50690,m824255907,/,\t15-eplf dir", new DirectoryListItem(
                "15-eplf dir",
                -1,
                "",
                "",
                null,
                new DateTime(1996, 2, 13, 23, 58, 27, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "04-27-00  12:09PM       <DIR>          16-dos-dateambiguous dir", new DirectoryListItem(
                "16-dos-dateambiguous dir",
                -1,
                "",
                "",
                null,
                new DateTime(2000, 4, 27, 12, 9, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "04-06-00  03:47PM                  589 17-dos-dateambiguous file", new DirectoryListItem(
                "17-dos-dateambiguous file",
                589,
                "",
                "",
                null,
                new DateTime(2000, 4, 6, 15, 47, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "2002-09-02  18:48       <DIR>          18-dos-longyear dir", new DirectoryListItem(
                "18-dos-longyear dir",
                -1,
                "",
                "",
                null,
                new DateTime(2002, 9, 2, 18, 48, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "2002-09-02  19:06                9,730 19-dos-longyear file", new DirectoryListItem(
                "19-dos-longyear file",
                9730,
                "",
                "",
                null,
                new DateTime(2002, 9, 2, 19, 6, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "0100644   500  101   12345    123456789       20-unix-numerical file", new DirectoryListItem(
                "20-unix-numerical file",
                12345,
                "0100644",
                "500 101",
                null,
                new DateTime(1973, 11, 29, 21, 33, 9, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "206876  Apr 04, 2000 21:06 21-vshell-old file", new DirectoryListItem(
                "21-vshell-old file",
                206876,
                "",
                "",
                null,
                new DateTime(2000, 4, 4, 21, 6, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "0  Dec 12, 2002 02:13 22-vshell-old dir/", new DirectoryListItem(
                "22-vshell-old dir",
                0,
                "",
                "",
                null,
                new DateTime(2002, 12, 12, 2, 13, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "-rwxr-xr-x    1 user group        9 Oct 08, 2002 09:47 23-vshell-new file", new DirectoryListItem(
                "23-vshell-new file",
                9,
                "-rwxr-xr-x",
                "user group",
                null,
                new DateTime(2002, 10, 8, 9, 47, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "36611      A    04-23-103  10:57  24-os2 file", new DirectoryListItem(
                "24-os2 file",
                36611,
                "",
                "",
                null,
                new DateTime(2003, 4, 23, 10, 57, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            " 1123      A    07-14-99   12:37  25-os2 file", new DirectoryListItem(
                "25-os2 file",
                1123,
                "",
                "",
                null,
                new DateTime(1999, 7, 14, 12, 37, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "    0 DIR       02-11-103  16:15  26-os2 dir", new DirectoryListItem(
                "26-os2 dir",
                0,
                "",
                "",
                null,
                new DateTime(2003, 2, 11, 16, 15, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            " 1123 DIR  A    10-05-100  23:38  27-os2 dir", new DirectoryListItem(
                "27-os2 dir",
                1123,
                "",
                "",
                null,
                new DateTime(2000, 10, 5, 23, 38, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "dr-xr-xr-x   2 root     other      2235 26. Juli, 20:10 28-datetest-ger dir", new DirectoryListItem(
                "28-datetest-ger dir",
                2235,
                "dr-xr-xr-x",
                "root other",
                null,
                new DateTime(Year(7, 26), 7, 26, 20, 10, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "dr-xr-xr-x   2 root     other      2235 szept 26 20:10 28b-datetest-hungarian dir", new DirectoryListItem(
                "28b-datetest-hungarian dir",
                2235,
                "dr-xr-xr-x",
                "root other",
                null,
                new DateTime(Year(9, 26), 9, 26, 20, 10, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "-r-xr-xr-x   2 root     other      2235 2.   Okt.  2003 29-datetest-ger file", new DirectoryListItem(
                "29-datetest-ger file",
                2235,
                "-r-xr-xr-x",
                "root other",
                null,
                new DateTime(2003, 10, 2, 0, 0, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "-r-xr-xr-x   2 root     other      2235 1999/10/12 17:12 30-datetest file", new DirectoryListItem(
                "30-datetest file",
                2235,
                "-r-xr-xr-x",
                "root other",
                null,
                new DateTime(1999, 10, 12, 17, 12, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "-r-xr-xr-x   2 root     other      2235 24-04-2003 17:12 31-datetest file", new DirectoryListItem(
                "31-datetest file",
                2235,
                "-r-xr-xr-x",
                "root other",
                null,
                new DateTime(2003, 4, 24, 17, 12, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "-rw-r--r--   1 root       sys           8473  4\x8c\x8e 18\x93\xfa 2003\x94\x4e 32-datatest-japanese file", new DirectoryListItem(
                "32-datatest-japanese file",
                8473,
                "-rw-r--r--",
                "root sys",
                null,
                new DateTime(2003, 4, 18, 0, 0, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "-rwxrwxrwx   1 root     staff          0 2003   3\xed\xef 20 33-asian date file", new DirectoryListItem(
                "33-asian date file",
                0,
                "-rwxrwxrwx",
                "root staff",
                null,
                new DateTime(2003, 3, 20, 0, 0, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "-r--r--r-- 1 root root 2096 8\xed 17 08:52 34-asian date file", new DirectoryListItem(
                "34-asian date file",
                2096,
                "-r--r--r--",
                "root root",
                null,
                new DateTime(Year(8, 17), 8, 17, 8, 52, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "-r-xr-xr-x   2 root  root  96 2004.07.15   35-dotted-date file", new DirectoryListItem(
                "35-dotted-date file",
                96,
                "-r-xr-xr-x",
                "root root",
                null,
                new DateTime(2004, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "36-vms-dir.DIR;1  1 19-NOV-2001 21:41 [root,root] (RWE,RWE,RE,RE)", new DirectoryListItem(
                "36-vms-dir",
                512,
                "RWE,RWE,RE,RE",
                "root,root",
                null,
                new DateTime(2001, 11, 19, 21, 41, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "37-vms-file;1       155   2-JUL-2003 10:30:13.64", new DirectoryListItem(
                "37-vms-file;1",
                79360,
                "",
                "",
                null,
                new DateTime(2003, 7, 2, 10, 30, 13, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "38-vms-notime-file;1    2/8    7-JAN-2000    [IV2_XXX]   (RWED,RWED,RE,)", new DirectoryListItem(
                "38-vms-notime-file;1",
                1024,
                "RWED,RWED,RE,",
                "IV2_XXX",
                null,
                new DateTime(2000, 1, 7, 0, 0, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "39-vms-notime-file;1    6/8    15-JUI-2002    PRONAS   (RWED,RWED,RE,)", new DirectoryListItem(
                "39-vms-notime-file;1",
                3072,
                "RWED,RWED,RE,",
                "PRONAS",
                null,
                new DateTime(2002, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "40-vms-multiline-file;1\r\n170774/170775     24-APR-2003 08:16:15  [FTP_CLIENT,SCOT]      (RWED,RWED,RE,)", new DirectoryListItem(
                "40-vms-multiline-file;1",
                87436288,
                "RWED,RWED,RE,",
                "FTP_CLIENT,SCOT",
                null,
                new DateTime(2003, 4, 24, 8, 16, 15, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "41-vms-multiline-file;1\r\n10     2-JUL-2003 10:30:08.59  [FTP_CLIENT,SCOT]      (RWED,RWED,RE,)", new DirectoryListItem(
                "41-vms-multiline-file;1",
                5120,
                "RWED,RWED,RE,",
                "FTP_CLIENT,SCOT",
                null,
                new DateTime(2003, 7, 2, 10, 30, 8, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "42-vms-alternate-field-order-file;1   [SUMMARY]    1/3     2-AUG-2006 13:05  (RWE,RWE,RE,)", new DirectoryListItem(
                "42-vms-alternate-field-order-file;1",
                512,
                "RWE,RWE,RE,",
                "SUMMARY",
                null,
                new DateTime(2006, 8, 2, 13, 5, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "43-vms-alternate-field-order-file;1       17-JUN-1994 17:25:37     6308/13     (RWED,RWED,R,)", new DirectoryListItem(
                "43-vms-alternate-field-order-file;1",
                3229696,
                "RWED,RWED,R,",
                "",
                null,
                new DateTime(1994, 6, 17, 17, 25, 37, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "QSYS            77824 02/23/00 15:09:55 *DIR 44-ibm-as400 dir/", new DirectoryListItem(
                "44-ibm-as400 dir",
                77824,
                "",
                "QSYS",
                null,
                new DateTime(2000, 2, 23, 15, 9, 55, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "QSYS            77824 23/02/00 15:09:55 *FILE 45-ibm-as400-date file", new DirectoryListItem(
                "45-ibm-as400-date file",
                77824,
                "",
                "QSYS",
                null,
                new DateTime(2000, 2, 23, 15, 9, 55, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "-r-xr-xr-x longowner longgroup123456 Feb 12 17:20 46-unix-concatsize file", new DirectoryListItem(
                "46-unix-concatsize file",
                123456,
                "-r-xr-xr-x",
                "longowner longgroup",
                null,
                new DateTime(Year(2, 12), 2, 12, 17, 20, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "-r-xr-xr-x 2 owner group 4512 01-jun-99 47_unix_shortdatemonth file", new DirectoryListItem(
                "47_unix_shortdatemonth file",
                4512,
                "-r-xr-xr-x",
                "owner group",
                null,
                new DateTime(1999, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "48-nortel-wfftp-file       1014196  06/03/04  Thur.   10:20:03", new DirectoryListItem(
                "48-nortel-wfftp-file",
                1014196,
                "",
                "",
                null,
                new DateTime(2004, 6, 3, 10, 20, 3, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "2048    Feb-28-1998  05:23:30   49-nortel-vxworks dir <DIR>", new DirectoryListItem(
                "49-nortel-vxworks dir",
                2048,
                "",
                "",
                null,
                new DateTime(1998, 2, 28, 5, 23, 30, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "-C--E-----FTP B BCC3I1       7670  1294495 Jan 13 07:42 50-conent file", new DirectoryListItem(
                "50-conent file",
                1294495,
                "-C--E-----FTP",
                "B BCC3I1 7670",
                null,
                new DateTime(Year(1, 13), 1, 13, 7, 42, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "20.20 07/03/29 1026 d-ewrewr 2650 85920 51-OS-9 dir", new DirectoryListItem(
                "51-OS-9 dir",
                85920,
                "d-ewrewr",
                "20.20",
                null,
                new DateTime(2007, 3, 29, 0, 0, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "drwxr-xr-x 3 user group 512 01 oct 2004 52-swapped-daymonth dir", new DirectoryListItem(
                "52-swapped-daymonth dir",
                512,
                "drwxr-xr-x",
                "user group",
                null,
                new DateTime(2004, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "-r--r--r-- 0125039 12 Nov 11 2005 53-noownergroup file", new DirectoryListItem(
                "53-noownergroup file",
                12,
                "-r--r--r--",
                "",
                null,
                new DateTime(2005, 11, 11, 0, 0, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "drwxr-xr-x   5 root     sys          512 2005\xEB\x85\x84  1\xEC\x9B\x94  6\xEC\x9D\xBC 54-asian date year first dir", new DirectoryListItem(
                "54-asian date year first dir",
                512,
                "drwxr-xr-x",
                "root sys",
                null,
                new DateTime(2005, 1, 6, 0, 0, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "QPGMR           36864 18.09.06 14:21:26 *FILE      55-AS400.FILE", new DirectoryListItem(
                "55-AS400.FILE",
                36864,
                "",
                "QPGMR",
                null,
                new DateTime(2006, 9, 18, 14, 21, 26, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "56-VMS-complex-size;1 2KB 23-SEP-2005 14:57:07.27", new DirectoryListItem(
                "56-VMS-complex-size;1",
                2048,
                "",
                "",
                null,
                new DateTime(2005, 9, 23, 14, 57, 7, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "57-HP_NonStop 101 528 6-Apr-07 14:21:18 255, 0 \"oooo\"", new DirectoryListItem(
                "57-HP_NonStop",
                528,
                "\"oooo\"",
                "255, 0",
                null,
                new DateTime(2007, 4, 6, 14, 21, 18, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "58-HP_NonStop 101 528 6-Apr-07 14:21:18 255,255 \"oooo\"", new DirectoryListItem(
                "58-HP_NonStop",
                528,
                "\"oooo\"",
                "255,255",
                null,
                new DateTime(2007, 4, 6, 14, 21, 18, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "drwxr-xr-x 6 user sys 1024 30. Jan., 12:40 59-localized-date-dir", new DirectoryListItem(
                "59-localized-date-dir",
                1024,
                "drwxr-xr-x",
                "user sys",
                null,
                new DateTime(Year(1, 30), 1, 30, 12, 40, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "WYOSPT 3420   2003/05/21  1  200  FB      80  8053  PS  60-MVS.FILE", new DirectoryListItem(
                "60-MVS.FILE",
                100,
                "",
                "",
                null,
                new DateTime(2003, 5, 21, 0, 0, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "WPTA01 3290   2004/03/04  1    3  FB      80  3125  PO  61-MVS.DATASET", new DirectoryListItem(
                "61-MVS.DATASET",
                -1,
                "",
                "",
                null,
                new DateTime(2004, 3, 04, 0, 0, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "NRP004 3390   **NONE**    1   15  NONE     0     0  PO  62-MVS-NONEDATE.DATASET", new DirectoryListItem(
                "62-MVS-NONEDATE.DATASET",
                -1,
                "",
                "",
                null,
                null,
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "TSO005 3390   2005/06/06 213000 U 0 27998 PO 63-MVS.DATASET", new DirectoryListItem(
                "63-MVS.DATASET",
                -1,
                "",
                "",
                null,
                new DateTime(2005, 6, 6, 0, 0, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "TSO004 3390   VSAM 64-mvs-file", new DirectoryListItem(
                "64-mvs-file",
                -1,
                "",
                "",
                null,
                null,
                0)
        },
        new object[] {
            "65-MVS-PDS-MEMBER", new DirectoryListItem(
                "65-MVS-PDS-MEMBER",
                -1,
                "",
                "",
                null,
                null,
                0)
        },
        new object[] {
            "66-MVSPDSMEMBER 01.01 2004/06/22 2004/06/22 16:32   128   128    0 BOBY12", new DirectoryListItem(
                "66-MVSPDSMEMBER",
                128,
                "",
                "",
                null,
                new DateTime(2004, 6, 22, 16, 32, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "67-MVSPDSMEMBER2 00B308 000411  00 FO                31    ANY", new DirectoryListItem(
                "67-MVSPDSMEMBER2",
                45832,
                "",
                "",
                null,
                null,
                0)
        },
        new object[] {
            "68-MVSPDSMEMBER3 00B308 000411  00 FO        RU      ANY    24", new DirectoryListItem(
                "68-MVSPDSMEMBER3",
                45832,
                "",
                "",
                null,
                null,
                0)
        },
        new object[] {
            "Migrated				69-SOME.FILE", new DirectoryListItem(
                "69-SOME.FILE",
                -1,
                "",
                "",
                null,
                null,
                0)
        },
        new object[] {
            "70-ZVMFILE  TRACE   V        65      107        2 2005-10-04 15:28:42 060191", new DirectoryListItem(
                "70-ZVMFILE.TRACE",
                6955,
                "",
                "060191",
                null,
                new DateTime(2005, 10, 4, 15, 28, 42, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "drwxr-xr-x 3 slopri devlab 512 71-unix-dateless", new DirectoryListItem(
                "71-unix-dateless",
                512,
                "drwxr-xr-x",
                "slopri devlab",
                null,
                null,
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "Type=file;mOdIfY=20081105165215;size=1234; 72-MLSD-file", new DirectoryListItem(
                "72-MLSD-file",
                1234,
                "",
                "",
                null,
                new DateTime(2008, 11, 5, 16, 52, 15, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "V43525 Tape                                             73-MSV-TAPE.FILE", new DirectoryListItem(
                "73-MSV-TAPE.FILE",
                -1,
                "",
                "",
                null,
                null,
                0)
        },
        new object[] {
            "Type=file; 74-MLSD-whitespace trailing\t ", new DirectoryListItem(
                "74-MLSD-whitespace trailing\t ",
                -1,
                "",
                "",
                null,
                null,
                0)
        },
        new object[] {
            "Type=file; \t 75-MLSD-whitespace leading", new DirectoryListItem(
                "\t 75-MLSD-whitespace leading",
                -1,
                "",
                "",
                null,
                null,
                0)
        },
        new object[] {
            "modify=20080426135501;perm=;size=65718921;type=file;unique=802U1066013B;UNIX.group=1179;UNIX.mode=00;UNIX.owner=1179; 75 MLSD file with empty permissions", new DirectoryListItem(
                "75 MLSD file with empty permissions",
                65718921,
                "00",
                "1179 1179",
                null,
                new DateTime(2008, 4, 26, 13, 55, 1, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "type=OS.unix=slink:/foo; 76 MLSD symlink", new DirectoryListItem(
                "76 MLSD symlink",
                -1,
                "",
                "",
                "/foo",
                null,
                DirectoryListItemFlags.Directory | DirectoryListItemFlags.Link)
        },
        new object[] {
            "type=OS.UNIX=symlink; 76b MLSD symlink", new DirectoryListItem(
                "76b MLSD symlink",
                -1,
                "",
                "",
                null,
                null,
                DirectoryListItemFlags.Directory | DirectoryListItemFlags.Link)
        },
        new object[] {
            "type=file 77 MLSD file no trailing semicolon after facts < mlst-07", new DirectoryListItem(
                "77 MLSD file no trailing semicolon after facts < mlst-07",
                -1,
                "",
                "",
                null,
                null,
                0)
        },
        new object[] {
            "type=OS.unix=slink; 77 MLSD symlink notarget", new DirectoryListItem(
                "77 MLSD symlink notarget",
                -1,
                "",
                "",
                null,
                null,
                DirectoryListItemFlags.Directory | DirectoryListItemFlags.Link)
        },
        new object[] {
            "size=1365694195;type=file;modify=20090722092510;\tadsl TV 2009-07-22 08-25-10 78 mlsd file that can get parsed as unix.file", new DirectoryListItem(
                "adsl TV 2009-07-22 08-25-10 78 mlsd file that can get parsed as unix.file",
                1365694195,
                "",
                "",
                null,
                new DateTime(2009, 7, 22, 9, 25, 10, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "WYOSPT 3420   2003/05/21  1 ????  FB      80  8053  PS  79-MVS.FILE", new DirectoryListItem(
                "79-MVS.FILE",
                100,
                "",
                "",
                null,
                new DateTime(2003, 5, 21, 0, 0, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "GISBWI 3390   2011/08/25  2 ++++  FB     904 18080  PS  80-MVS.FILE", new DirectoryListItem(
                "80-MVS.FILE",
                100,
                "",
                "",
                null,
                new DateTime(2011, 8, 25, 0, 0, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "WYOSPT 3420   2003/05/21  1 3 U 6447    6447  PO-E 81-MVS.DIR", new DirectoryListItem(
                "81-MVS.DIR",
                -1,
                "",
                "",
                null,
                new DateTime(2003, 5, 21, 0, 0, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "drwxrwxrwx   1 0        0               0 29 Jul 02:27 82 2014 Invoices", new DirectoryListItem(
                "82 2014 Invoices",
                0,
                "drwxrwxrwx",
                "0 0",
                null,
                new DateTime(Year(7, 29), 7, 29, 2, 27, 0, DateTimeKind.Utc),
                DirectoryListItemFlags.Directory)
        },
        new object[] {
            "Type=file;mOdIfY=19681105165215;size=1234; 83 MLSD pre-epoch", new DirectoryListItem(
                "83 MLSD pre-epoch",
                1234,
                "",
                "",
                null,
                new DateTime(1968, 11, 5, 16, 52, 15, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "-rw-------      1  99999999 0              3 Apr   4 24:00 84 alternate_midnight", new DirectoryListItem(
                "84 alternate_midnight",
                3,
                "-rw-------",
                "99999999 0",
                null,
                new DateTime(Year(4, 4), 4, 5, 0, 0, 0, DateTimeKind.Utc),
                0)
        },
        new object[] {
            "04-27-00 12:09PM 5 x", new DirectoryListItem(
                "x",
                5,
                "",
                "",
                null,
                new DateTime(2000, 4, 27, 12, 9, 0, DateTimeKind.Utc),
                0)
        }
    ];

    [TestCaseSource(nameof(TestCases))]
    public async Task NameIsParsed(string line, DirectoryListItem entry) {
        await CompareAsync(line, entry, e => e.Name);
    }

    [TestCaseSource(nameof(TestCases))]
    public async Task PermissionsAreParsed(string line, DirectoryListItem entry) {
        await CompareAsync(line, entry, e => e.Permissions);
    }

    [TestCaseSource(nameof(TestCases))]
    public async Task SizeIsParsed(string line, DirectoryListItem entry) {
        await CompareAsync(line, entry, e => e.Size);
    }

    [TestCaseSource(nameof(TestCases))]
    public async Task OwnerGroupIsParsed(string line, DirectoryListItem entry) {
        await CompareAsync(line, entry, e => e.OwnerGroup);
    }

    [TestCaseSource(nameof(TestCases))]
    public async Task TimeIsParsed(string line, DirectoryListItem entry) {
        await CompareAsync(line, entry, e => e.Time);
    }

    [TestCaseSource(nameof(TestCases))]
    public async Task FlagsAreParsed(string line, DirectoryListItem entry) {
        await CompareAsync(line, entry, e => e.Flags);
    }

    [TestCaseSource(nameof(TestCases))]
    public async Task TargetIsParsed(string line, DirectoryListItem entry) {
        await CompareAsync(line, entry, e => e.Target);
    }

    [Test]
    public async Task CorrectNumberOfEntriesAreParsedFromLines() {
        var infos = await ParseTestCasesAsync();
        var actual = infos.Count;
        var expected = TestCases.Length;
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void FinalMetadataTokenIsRetainedWhenItIsOneCharacter() {
        var item = new FtpListingItem("permissions owner x");
        var actual = item.GetToken(2)?.ToString();
        Assert.That(actual, Is.EqualTo("x"));
    }

    [Test]
    public void ConstructorRetainsTarget() {
        var item = new DirectoryListItem("link", 0, "", "", "target", null, DirectoryListItemFlags.Link);
        Assert.That(item.Target, Is.EqualTo("target"));
    }

    [Test]
    public async Task UnixLinkTargetIsRetained() {
        const string line = "lrwxrwxrwx   1 root     other          7 Jan 25 00:17 link -> usr/bin";
        async IAsyncEnumerable<string> contents() {
            yield return line;
            await Task.CompletedTask;
        }

        var items = await ToListAsync(DirectoryList.Parse(contents(), default));
        Assert.That(items.Single().Target, Is.EqualTo("usr/bin"));
    }

    [Test]
    public async Task UnixLinkWithSingleCharacterTargetIsSeparatedFromName() {
        const string line = "lrwxrwxrwx   1 root     other          1 Jan 25 2024 link -> x";
        async IAsyncEnumerable<string> contents() {
            yield return line;
            await Task.CompletedTask;
        }

        var item = (await ToListAsync(DirectoryList.Parse(contents(), default))).Single();
        Assert.That(new[] { item.Name, item.Target }, Is.EqualTo(new[] { "link", "x" }));
    }

    [Test]
    public async Task FilenameListCandidatesAreDiscardedWhenStructuredEntryAppears() {
        var items = await ParseListingAsync("README", "type=file; realfile");
        Assert.That(items.Select(item => item.Name), Is.EqualTo(new[] { "realfile" }));
    }

    [Test]
    public async Task FilenameListDoesNotTreatMigratedRowsAsMvsByDefault() {
        var items = await ParseListingAsync("Migrated\tSOME.FILE", "type=file; realfile");
        Assert.That(items.Select(item => item.Name), Is.EqualTo(new[] { "realfile" }));
    }

    [Test]
    public async Task FilenameListFallbackEmitsUnparsedNamesWithoutParser() {
        var items = await ParseListingAsync("README", "LICENSE");
        Assert.That(items.Select(item => item.Name), Is.EqualTo(new[] { "README", "LICENSE" }));
        Assert.That(items.Select(item => item.Parser), Is.All.Null);
    }

    [Test]
    public async Task CurrentAndParentDirectoryEntriesAreFilteredFromUnixListings() {
        var items = await ParseListingAsync(
            "drwxr-xr-x 2 root root 512 Jan 1 2024 .",
            "drwxr-xr-x 2 root root 512 Jan 1 2024 ..");
        Assert.That(items, Is.Empty);
    }

    [Test]
    public async Task CurrentAndParentDirectoryEntriesAreFilteredFromDosListings() {
        var items = await ParseListingAsync(
            "04-27-00  12:09PM       <DIR>          .",
            "04-27-00  12:09PM       <DIR>          ..");
        Assert.That(items, Is.Empty);
    }

    [Test]
    public async Task CurrentAndParentDirectoryEntriesAreFilteredFromMlsdListings() {
        var items = await ParseListingAsync("type=dir; .", "type=dir; ..");
        Assert.That(items, Is.Empty);
    }

    [TestCase("01:30am", 1, 30)]
    [TestCase("01:30pm", 13, 30)]
    [TestCase("12:00am", 0, 0)]
    [TestCase("12:00pm", 12, 0)]
    public async Task DosListingParsesLowercaseAmPmSuffix(string time, int hour, int minute) {
        var items = await ParseListingAsync($"04-27-00 {time} 5 filename");

        Assert.That(items.Count, Is.EqualTo(1));
        Assert.That(items[0].Parser, Is.EqualTo("DOS"));
        Assert.That(items[0].Time, Is.EqualTo(new DateTime(2000, 4, 27, hour, minute, 0, DateTimeKind.Utc)));
    }

    [TestCase("01:30xm")]
    [TestCase("12:30xm")]
    public async Task DosListingRejectsUnknownTimeSuffix(string time) {
        var items = await ParseListingAsync($"04-27-00 {time} 5 filename");

        Assert.That(items, Is.Empty);
    }

    [TestCase("12-31-24 24:00 5 filename", 2025, 1, 1)]
    [TestCase("12-31-24 24:00:00 5 filename", 2025, 1, 1)]
    [TestCase("-rw-r--r-- 1 user group 5 Dec 31, 2024 24:00 filename", 2025, 1, 1)]
    [TestCase("-rw-r--r-- 1 user group 5 Apr 30, 2024 24:00 filename", 2024, 5, 1)]
    [TestCase("-rw-r--r-- 1 user group 5 Feb 29, 2024 24:00 filename", 2024, 3, 1)]
    public async Task AlternateMidnightRollsOverAtCalendarBoundaries(string line, int year, int month, int day) {
        var items = await ParseListingAsync(line);

        Assert.That(items.Count, Is.EqualTo(1));
        Assert.That(items[0].Name, Is.EqualTo("filename"));
        Assert.That(items[0].Time, Is.EqualTo(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc)));
    }

    [TestCase("12-31-24 24:01 5 filename")]
    [TestCase("12-31-24 24:00:01 5 filename")]
    [TestCase("12-31-24 24:00PM 5 filename")]
    public async Task AlternateMidnightRejectsNonMidnightTimes(string line) {
        var items = await ParseListingAsync(line);

        Assert.That(items.Any(item => item.Name == "filename" && item.Time != null), Is.False);
    }

    [TestCase("04-27-00 a1:30 5 filename")]
    [TestCase("04-27-00 12:30: 5 filename")]
    public async Task DosListingRejectsInvalidNumericTimeSlices(string line) {
        var items = await ParseListingAsync(line);

        Assert.That(items, Is.Empty);
    }

    [Test]
    public async Task UnixListingRejectsInvalidNumericDaySlice() {
        var items = await ParseListingAsync("-rw-r--r-- 1 user group 5 Apr-a5 2024 filename");

        Assert.That(items.Any(item => item.Name == "filename" && item.Parser == "UNIX"), Is.False);
    }

    [Test]
    public async Task UnixListingMapsOverflowingDecimalSizeToUnknownSize() {
        var items = await ParseListingAsync(
            "-rw-r--r-- 1 user group 9223372036854775808 Jan 1 2024 filename");

        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0].Parser, Is.EqualTo("UNIX"));
        Assert.That(items[0].Size, Is.EqualTo(-1));
    }

    [Test]
    public void FtpListingTokenParsesMaximumDecimalValuesAcrossViews() {
        const string maximum = "9223372036854775807";
        var maximumValue = long.MaxValue;

        Assert.That(new FtpListingToken(maximum).Base10, Is.EqualTo(maximumValue));
        Assert.That(new FtpListingToken(maximum + "suffix").Base10, Is.EqualTo(maximumValue));
        Assert.That(new FtpListingToken("prefix" + maximum).Base10, Is.EqualTo(maximumValue));
        Assert.That(new FtpListingToken("x" + maximum + "y").GetBase10(1, maximum.Length),
            Is.EqualTo(maximumValue));
        Assert.That(new FtpListingToken("9223372036854775808").Base10, Is.Null);
    }

    [Test]
    public void FtpListingTokenChecksHexadecimalBoundaries() {
        Assert.That(new FtpListingToken("7FFFFFFFFFFFFFFF").Base16, Is.EqualTo(long.MaxValue));
        Assert.That(new FtpListingToken("8000000000000000").Base16, Is.Null);
    }

    [Test]
    public async Task UnixListingAcceptsMaximumDecimalSize() {
        var items = await ParseListingAsync(
            "-rw-r--r-- 1 user group 9223372036854775807 Jan 1 2024 filename");

        Assert.That(items.Single().Size, Is.EqualTo(long.MaxValue));
    }

    [Test]
    public async Task UnixListingRejectsComplexSizeThatOverflowsLong() {
        var items = await ParseListingAsync(
            "-rw-r--r-- 1 user group 9500000000000000K Jan 1 2024 filename");

        Assert.That(items.Any(item => item.Name == "filename" && item.Parser == "UNIX"), Is.False);
    }

    [Test]
    public async Task UnixListingAcceptsLargestKilobyteSizeThatFitsLong() {
        var items = await ParseListingAsync(
            "-rw-r--r-- 1 user group 9007199254740991K Jan 1 2024 filename");

        Assert.That(items.Single().Size, Is.EqualTo(9223372036854774784));
    }

    [Test]
    public async Task DosListingAcceptsMaximumDecimalSize() {
        var items = await ParseListingAsync("04-27-00 12:09PM 9223372036854775807 filename");

        Assert.That(items.Single().Size, Is.EqualTo(long.MaxValue));
    }

    [Test]
    public async Task DosListingRejectsDecimalSizeAboveMaximum() {
        var items = await ParseListingAsync("04-27-00 12:09PM 9223372036854775808 filename");

        Assert.That(items.Any(item => item.Name == "filename" && item.Parser == "DOS"), Is.False);
    }

    [Test]
    public async Task MlsdAcceptsMaximumDecimalSize() {
        var items = await ParseListingAsync("type=file;size=9223372036854775807; filename");

        Assert.That(items.Single().Size, Is.EqualTo(long.MaxValue));
    }

    [Test]
    public async Task MlsdRejectsDecimalSizeAboveMaximum() {
        var items = await ParseListingAsync("type=file;size=9223372036854775808; filename");

        Assert.That(items, Is.Empty);
    }

    [TestCase("+m,\tfilename")]
    [TestCase("+mabc123,\tfilename")]
    public void EplfListingRejectsInvalidNumericModificationTime(string line) {
        var parser = new EplfListingParser();
        var status = parser.Parse(new[] { new FtpListingItem(line) }, new FtpListingContext(), out _);

        Assert.That(status, Is.EqualTo(FtpListingItemParser.ParseStatus.NoMatch));
    }

    [TestCase("+s,\tfilename")]
    [TestCase("+sabc123,\tfilename")]
    public void EplfListingTreatsInvalidNumericSizeAsUnknown(string line) {
        var parser = new EplfListingParser();
        var status = parser.Parse(new[] { new FtpListingItem(line) }, new FtpListingContext(), out var item);

        Assert.That(status, Is.EqualTo(FtpListingItemParser.ParseStatus.Parsed));
        Assert.That(item.Size, Is.EqualTo(-1));
    }

    [Test]
    public void UnixYearInferenceMatchesFileZillaMonthBoundary() {
        var item = ParseUnixListing(
            "-rw-r--r-- 1 user group 5 May 1 12:00 filename",
            new DateTime(2025, 4, 30, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(item.Time, Is.EqualTo(new DateTime(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc)));
    }

    [TestCase("Jan 13", 2026, 1, 13)]
    [TestCase("Jan 14", 2025, 1, 14)]
    public void UnixYearInferenceUsesUtcClockAtDayBoundary(string date, int year, int month, int day) {
        var item = ParseUnixListing(
            $"-rw-r--r-- 1 user group 5 {date} 12:00 filename",
            TestUtcNow);

        Assert.That(item.Time, Is.EqualTo(new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task UnixListingYearMatchesTheFixedUtcClock() {
        var item = (await ParseListingWithFixedUtcClockAsync(DirectoryListServerType.Default,
            "-rw-r--r-- 1 user group 5 Oct 20 15:27 filename")).Single();

        Assert.That(item.Time, Is.EqualTo(new DateTime(Year(10, 20), 10, 20, 15, 27, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void UnixYearInferenceSelectsLeapYearBeforeValidatingDate() {
        var item = ParseUnixListing(
            "-rw-r--r-- 1 user group 5 Feb 29 12:00 filename",
            new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(item.Time, Is.EqualTo(new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void UnixListingAppliesTheConfiguredParserTimeOffset() {
        var parser = new UnixListingParser(expectDate: true,
            getUtcNow: () => new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc));
        var context = new FtpListingContext { TimeZoneOffset = TimeSpan.FromHours(2) };
        var status = parser.Parse(
            new[] { new FtpListingItem("-rw-r--r-- 1 user group 5 Apr 8 1994 filename") },
            context,
            out var item);

        Assert.That(status, Is.EqualTo(FtpListingItemParser.ParseStatus.Parsed));
        Assert.That(item.Time, Is.EqualTo(new DateTime(1994, 4, 8, 2, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task DirectoryListAppliesListingTimeOffsetToUnixTimestamps() {
        var item = (await ParseListingAsync(
            DirectoryListServerType.Default,
            ["-rw-r--r-- 1 user group 5 Apr 8 1994 filename"],
            null,
            TimeSpan.FromHours(2))).Single();

        Assert.That(item.Time, Is.EqualTo(new DateTime(1994, 4, 8, 2, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task DirectoryListAppliesServerTimeOffsetToUtcTimestamps() {
        var item = (await ParseListingAsync(
            DirectoryListServerType.Default,
            ["Type=file;modify=20081105165215;size=1234; 72-MLSD-file"],
            null,
            serverTimeOffset: TimeSpan.FromHours(1))).Single();

        Assert.That(item.Time, Is.EqualTo(new DateTime(2008, 11, 5, 17, 52, 15, DateTimeKind.Utc)));
    }

    [Test]
    public void EndTokenTrimsOnlyAsciiSpacesAndTabs() {
        var item = new FtpListingItem("filename\u00a0 \t");

        Assert.That(item.GetEndToken(0).ToString(), Is.EqualTo("filename\u00a0"));
        Assert.That(item.GetEndToken(0, true).ToString(), Is.EqualTo("filename\u00a0 \t"));
    }

    [Test]
    public async Task MlsdPreservesWhitespaceOnlyName() {
        var item = (await ParseListingAsync("type=file;   ")).Single();

        Assert.That(item.Name, Is.EqualTo("  "));
        Assert.That(item.Parser, Is.EqualTo("MLSD"));
    }

    [TestCase("obre", 10)]
    [TestCase("nbre", 11)]
    [TestCase("dbre", 12)]
    public async Task UnixListingMapsLocalizedMonthAliasesToCorrectMonth(string alias, int month) {
        var items = await ParseListingAsync($"-rw-r--r-- 1 user group 5 {alias} 1 2000 filename");

        Assert.That(items.Single().Time, Is.EqualTo(new DateTime(2000, month, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [TestCase("f\u00e9b", 2)]
    [TestCase("obre", 10)]
    [TestCase("nbre", 11)]
    [TestCase("dbre", 12)]
    [TestCase("f\u00e9b01", 2)]
    [TestCase("f\u00e9b02", 2)]
    [TestCase("f\u00e9b1", 2)]
    [TestCase("f\u00e9b2", 2)]
    [TestCase("obre09", 10)]
    [TestCase("obre10", 10)]
    [TestCase("obre9", 10)]
    [TestCase("obre0", 10)]
    [TestCase("nbre10", 11)]
    [TestCase("nbre11", 11)]
    [TestCase("nbre0", 11)]
    [TestCase("nbre1", 11)]
    [TestCase("dbre11", 12)]
    [TestCase("dbre12", 12)]
    [TestCase("dbre1", 12)]
    [TestCase("dbre2", 12)]
    public void FtpListingMonthMapMapsLocalizedMonthAliasesAndNumberCombinations(string alias, int month) {
        var monthMap = new FtpListingMonthMap();

        Assert.That(monthMap.Find(alias), Is.EqualTo(month));
    }

    [TestCase("type=cdir; 1 19-NOV-2001 21:41")]
    [TestCase("type=pdir; 1 19-NOV-2001 21:41")]
    public async Task MlsdCurrentAndParentDirectoryFactsAreSkipped(string line) {
        var items = await ParseListingAsync(line);
        Assert.That(items, Is.Empty);

        var followingItems = await ParseListingAsync(line, "type=file; following");
        Assert.That(followingItems.Select(item => item.Name), Is.EqualTo(new[] { "following" }));
    }

    [TestCase('\r')]
    [TestCase('\0')]
    public async Task CarriageReturnAndNulSeparateListingLines(char separator) {
        var items = await ParseChunksAsync($"type=file; first{separator}type=file; second");
        Assert.That(items.Select(item => item.Name), Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public async Task TerminalCarriageReturnTerminatesListingLine() {
        var items = await ParseChunksAsync("type=file; terminal\r");
        Assert.That(items.Select(item => item.Name), Is.EqualTo(new[] { "terminal" }));
    }

    [Test]
    public async Task CarriageReturnAndLineFeedSplitAcrossChunksStillTerminateOneLine() {
        var items = await ParseChunksAsync("type=file; first\r", "\n", "type=file; second");
        Assert.That(items.Select(item => item.Name), Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public async Task ListingLineAtUtf8ByteLimitIsAccepted() {
        const string prefix = "type=file; ";
        var name = new string('a', 10_000 - prefix.Length);
        var items = await ParseChunksAsync(prefix, name);

        Assert.That(items.Single().Name, Is.EqualTo(name));
    }

    [Test]
    public async Task ListingLineOverUtf8ByteLimitThrows() {
        const string prefix = "type=file; ";
        var line = prefix + new string('a', 10_001 - prefix.Length);
        var chunks = line.Select(character => character.ToString()).ToArray();

        await Assert.ThrowsAsync<InvalidDataException>(async () => {
            await ParseChunksAsync(chunks);
        });
    }

    [Test]
    public async Task ListingLineLimitCountsUtf8BytesForUnicodeText() {
        const string prefix = "type=file; ";
        var line = prefix + new string('\u4e00', 3_330);

        await Assert.ThrowsAsync<InvalidDataException>(async () => {
            await ParseChunksAsync(line);
        });
    }

    [Test]
    public async Task ListingLineAtUtf8ByteLimitAcceptsSurrogatePairSplitAcrossChunks() {
        const string prefix = "type=file; ";
        var name = new string('a', 9_985) + "\U0001F600";
        var items = await ParseChunksAsync(prefix, new string('a', 9_985), "\uD83D", "\uDE00");

        Assert.That(items.Single().Name, Is.EqualTo(name));
    }

    [Test]
    public async Task LeadingBomIsRemovedFromDosListingLine() {
        var items = await ParseChunksAsync("\uFEFF04-27-00  12:09PM       <DIR>          folder");
        Assert.That(items.Single().Name, Is.EqualTo("folder"));
        Assert.That(items.Single().Parser, Is.EqualTo("DOS"));
    }

    [Test]
    public async Task LeadingBomIsRemovedFromMlsdListingLine() {
        var items = await ParseChunksAsync("\uFEFFtype=dir; folder");
        Assert.That(items.Single().Flags, Is.EqualTo(DirectoryListItemFlags.Directory));
        Assert.That(items.Single().Parser, Is.EqualTo("MLSD"));
    }

    [Test]
    public async Task VmsMultilineEntrySurvivesBlankPhysicalLine() {
        const string listing =
            "file.txt;1\n\n10 2-JUL-2003 10:30:08.59 [OWNER] (RWED,RWED,RE,)\n";
        var item = (await ParseChunksAsync(listing)).Single();
        Assert.That(item.Name, Is.EqualTo("file.txt;1"));
        Assert.That(item.Size, Is.EqualTo(5120));
        Assert.That(item.OwnerGroup, Is.EqualTo("OWNER"));
        Assert.That(item.Permissions, Is.EqualTo("RWED,RWED,RE,"));
    }

    [Test]
    public async Task VmsListingAcceptsLargestBlockSizeThatFitsLong() {
        var items = await ParseListingAsync(
            "file;1 18014398509481983/8 7-JAN-2000 [OWNER] (RWE,RWE,RE,RE)");

        Assert.That(items.Single().Size, Is.EqualTo(long.MaxValue - 511));
    }

    [Test]
    public async Task VmsListingRejectsBlockSizeProductThatOverflowsLong() {
        var items = await ParseListingAsync(
            "file;1 18014398509481984/8 7-JAN-2000 [OWNER] (RWE,RWE,RE,RE)");

        Assert.That(items.Any(item => item.Name == "file;1" && item.Parser == "VMS"), Is.False);
    }

    [Test]
    public async Task ParsedLineIsYieldedBeforeRequestingTheNextChunk() {
        var nextChunkRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowNextChunk = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async IAsyncEnumerable<string> contents() {
            yield return "type=file; first\n";
            nextChunkRequested.TrySetResult(true);
            await allowNextChunk.Task;
            yield return "type=file; second\n";
        }

        var enumerator = DirectoryList.Parse(contents(), default).GetAsyncEnumerator();
        try {
            var firstMoveNext = enumerator.MoveNextAsync().AsTask();
            var signal = await Task.WhenAny(
                firstMoveNext,
                nextChunkRequested.Task,
                Task.Delay(TimeSpan.FromSeconds(5)));
            var firstItemWasAvailableImmediately = signal == firstMoveNext;
            allowNextChunk.TrySetResult(true);
            var hasItem = await firstMoveNext;

            Assert.That(firstItemWasAvailableImmediately, Is.True);
            Assert.That(hasItem, Is.True);
            Assert.That(enumerator.Current.Name, Is.EqualTo("first"));
        }
        finally {
            allowNextChunk.TrySetResult(true);
            await enumerator.DisposeAsync();
        }
    }

    [Test]
    public async Task ParseCancellationReachesContentSource() {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var sourceToken = CancellationToken.None;

        async IAsyncEnumerable<string> contents(
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            sourceToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            yield return "type=file; item\n";
            await Task.CompletedTask;
        }

        async Task consume() {
            await foreach (var item in DirectoryList.Parse(contents(), cancellation.Token)) {
            }
        }

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(consume);
        Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(sourceToken, Is.EqualTo(cancellation.Token));
    }

    [Test]
    public async Task EnumerationCancellationReachesContentSource() {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var sourceToken = CancellationToken.None;

        async IAsyncEnumerable<string> contents(
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            sourceToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            yield return "type=file; item\n";
            await Task.CompletedTask;
        }

        async Task consume() {
            await foreach (var item in DirectoryList.Parse(contents(), default).WithCancellation(cancellation.Token)) {
            }
        }

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(consume);
        Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(sourceToken, Is.EqualTo(cancellation.Token));
    }

    [TestCase("20081105165215.123", 123)]
    [TestCase("20081105165215.12", 0)]
    [TestCase("20081105165215.1234", 123)]
    [TestCase("2008-11-05T16:52:15.123", 123)]
    public async Task MlsdTimestampParsesFractionAndSeparators(string timestamp, int milliseconds) {
        var item = (await ParseChunksAsync($"type=file;modify={timestamp}; file")).Single();
        var expectedTime = new DateTime(2008, 11, 5, 16, 52, 15, milliseconds, DateTimeKind.Utc);
        Assert.That(item.Time, Is.EqualTo(expectedTime));
    }

    [Test]
    public async Task MvsMigratedFormatIsSelectedOnlyForMvsServer() {
        const string line = "Migrated\tSOME.FILE";
        var defaultItems = await ParseListingAsync(line);
        Assert.That(defaultItems.Single().Name, Is.EqualTo(line));
        Assert.That(defaultItems.Single().Parser, Is.Null);

        var mvsItems = await ParseListingAsync(DirectoryListServerType.Mvs, line);
        Assert.That(mvsItems.Single().Name, Is.EqualTo("SOME.FILE"));
        Assert.That(mvsItems.Single().Parser, Is.EqualTo("MVSMIGRATED"));
    }

    [Test]
    public async Task MvsPds2FormatIsSelectedOnlyForMvsServer() {
        const string line = "67-MVSPDSMEMBER2 00B308 000411  00 FO                31    ANY";
        var defaultItems = await ParseListingAsync(line);
        Assert.That(defaultItems, Is.Empty);

        var mvsItems = await ParseListingAsync(DirectoryListServerType.Mvs, line);
        Assert.That(mvsItems.Single().Name, Is.EqualTo("67-MVSPDSMEMBER2"));
        Assert.That(mvsItems.Single().Parser, Is.EqualTo("MVSPDS2"));
    }

    [Test]
    public async Task MvsPds2RejectsInvalidHexSize() {
        var items = await ParseListingAsync(DirectoryListServerType.Mvs, "MEMBER zzz 000411 00 FO 31 ANY");

        Assert.That(items, Is.Empty);
    }

    [Test]
    public async Task MvsPds2ParsesLowercaseHexSizeLikeUppercase() {
        const string uppercaseLine = "MEMBER 00B308 000411 00 FO 31 ANY";
        const string lowercaseLine = "MEMBER 00b308 000411 00 FO 31 ANY";
        var uppercaseItems = await ParseListingAsync(DirectoryListServerType.Mvs, uppercaseLine);
        var lowercaseItems = await ParseListingAsync(DirectoryListServerType.Mvs, lowercaseLine);

        Assert.That(lowercaseItems.Single().Size, Is.EqualTo(uppercaseItems.Single().Size));
        Assert.That(lowercaseItems.Single().Size, Is.EqualTo(45832));
    }

    [Test]
    public async Task MvsPds2AcceptsMaximumHexSize() {
        var items = await ParseListingAsync(
            DirectoryListServerType.Mvs, "MEMBER 7FFFFFFFFFFFFFFF 000411 00 FO 31 ANY");

        Assert.That(items.Single().Size, Is.EqualTo(long.MaxValue));
    }

    [Test]
    public async Task MvsPds2RejectsHexSizeAboveMaximum() {
        var items = await ParseListingAsync(
            DirectoryListServerType.Mvs, "MEMBER 8000000000000000 000411 00 FO 31 ANY");

        Assert.That(items, Is.Empty);
    }

    [Test]
    public async Task IbmListingMapsOverflowingSizeToUnknownSize() {
        var items = await ParseListingAsync("owner 9223372036854775808 04-27-00 12:30 0 filename");

        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0].Parser, Is.EqualTo("IBM"));
        Assert.That(items[0].Size, Is.EqualTo(-1));
    }

    [Test]
    public async Task MvsTapeFormatIsSelectedOnlyForMvsServer() {
        const string line = "VOL001 TAPE SOME.DATA";
        var defaultItems = await ParseListingAsync(line);
        Assert.That(defaultItems, Is.Empty);

        var mvsItems = await ParseListingAsync(DirectoryListServerType.Mvs, line);
        Assert.That(mvsItems.Single().Name, Is.EqualTo("SOME.DATA"));
        Assert.That(mvsItems.Single().Parser, Is.EqualTo("MVSTAPE"));
    }

    [Test]
    public async Task HpNonStopParserIsPrioritizedForSelectedServer() {
        const string line = "57-HP_NonStop 101 528 6-Apr-07 14:21:18 255, 0 \"oooo\"";
        var defaultItems = await ParseListingAsync(line);
        Assert.That(defaultItems, Is.Empty);

        var hpItems = await ParseListingAsync(DirectoryListServerType.HpNonStop, line);
        Assert.That(hpItems.Single().Name, Is.EqualTo("57-HP_NonStop"));
        Assert.That(hpItems.Single().Parser, Is.EqualTo("HPNONSTOP"));
    }

    [Test]
    public async Task ZvmParserIsPrioritizedForSelectedServer() {
        const string line = "70-ZVMFILE  TRACE   V        65      107        2 2005-10-04 15:28:42 060191";
        var defaultItems = await ParseListingAsync(line);
        Assert.That(defaultItems, Is.Empty);

        var zvmItems = await ParseListingAsync(DirectoryListServerType.Zvm, line);
        Assert.That(zvmItems.Single().Name, Is.EqualTo("70-ZVMFILE.TRACE"));
        Assert.That(zvmItems.Single().Parser, Is.EqualTo("ZVM"));
    }

    [Test]
    public async Task ZvmAcceptsLargestRecordProductThatFitsLong() {
        var items = await ParseListingAsync(
            DirectoryListServerType.Zvm,
            "FILE TRACE V 1 9223372036854775807 2 2005-10-04 15:28:42 OWNER");

        Assert.That(items.Single().Size, Is.EqualTo(long.MaxValue));
    }

    [Test]
    public async Task ZvmRejectsRecordProductThatOverflowsLong() {
        var items = await ParseListingAsync(
            DirectoryListServerType.Zvm,
            "FILE TRACE V 2 4611686018427387904 2 2005-10-04 15:28:42 OWNER");

        Assert.That(items, Is.Empty);
    }

    [Test]
    public async Task ZvmRejectsOverflowingRecordCount() {
        var items = await ParseListingAsync(
            DirectoryListServerType.Zvm,
            "FILE TRACE V 1 9223372036854775808 2 2005-10-04 15:28:42 OWNER");

        Assert.That(items, Is.Empty);
    }

    [Test]
    public async Task RawFilenameListFiltersCurrentAndParentDirectories() {
        var items = await ParseListingAsync(".", "..", "README");
        Assert.That(items.Select(item => item.Name), Is.EqualTo(new[] { "README" }));
    }

    [TestCase("9223372036854775808")]
    [TestCase("4294969296")]
    public async Task RequiredYearCannotOverflowOrWrap(string year) {
        var unixItems = await ParseListingAsync($"-rw-r--r-- 1 user group 5 Jan 1 {year} filename");
        Assert.That(unixItems.Any(item => item.Name == "filename" && item.Time.HasValue), Is.False);

        var otherItems = await ParseListingAsync($"5 Jan 1 {year} 12:00 filename");
        Assert.That(otherItems, Is.Empty);
    }

    [TestCase("9007199254740991.0K", 9223372036854774784L)]
    [TestCase("9007199254740991.999999999999999999999999K", long.MaxValue)]
    [TestCase("8796093022207.999999999999999999999999M", long.MaxValue)]
    [TestCase("8589934591.999999999999999999999999G", long.MaxValue)]
    [TestCase("8388607.999999999999999999999999T", long.MaxValue)]
    [TestCase("1.2345678901234567890123456789K", 1264L)]
    [TestCase(".999K", 1022L)]
    public async Task FractionalUnitSizeUsesTheFinalScaledValue(string size, long expected) {
        var items = await ParseListingAsync($"-rw-r--r-- 1 user group {size} Jan 1 2024 filename");
        Assert.That(items.Single().Size, Is.EqualTo(expected));
    }

    [TestCase("9007199254740992.0K")]
    [TestCase("9223372036854775808.0")]
    [TestCase("1.2.3K")]
    public async Task FractionalUnitSizeRejectsInvalidOrOutOfRangeResults(string size) {
        var items = await ParseListingAsync($"-rw-r--r-- 1 user group {size} Jan 1 2024 filename");
        Assert.That(items.Any(item => item.Name == "filename" && item.Time.HasValue), Is.False);
    }

    [Test]
    public async Task FractionalVmsBlocksAcceptTheLargestRepresentableResult() {
        var item = (await ParseListingAsync(
            "file;1 18014398509481983.999999999999999999999999/8 7-JAN-2000 [OWNER]")).Single();
        Assert.That(item.Size, Is.EqualTo(long.MaxValue));
    }

    [TestCase("file.txt;1 10 2-JUL-2003")]
    [TestCase("file.txt;1\n10 2-JUL-2003")]
    [TestCase("file.txt;1 10 2-JUL-2003 00:00")]
    public async Task VmsOffsetsAreAppliedExactlyOnceOnEverySuccessfulPath(string listing) {
        var item = (await ParseListingAsync(DirectoryListServerType.Default, [listing], null,
            TimeSpan.FromHours(2), TimeSpan.FromHours(-1))).Single();
        Assert.That(item.Time, Is.EqualTo(new DateTime(2003, 7, 2, 1, 0, 0, DateTimeKind.Utc)));
    }

    [TestCase("/")]
    [TestCase("directory/")]
    [TestCase("a*")]
    [TestCase("a|")]
    public async Task UnixLinkPreservesLiteralTargetBeforeTrimmingNameMarkers(string target) {
        var item = (await ParseListingAsync(
            $"lrwxrwxrwx 1 user group 1 Jan 1 2024 link* -> {target}")).Single();
        Assert.That(item.Name, Is.EqualTo("link"));
        Assert.That(item.Target, Is.EqualTo(target));
    }

    [TestCase("\u00a0")]
    [TestCase("\u2003\u2003")]
    public async Task RawFilenameListPreservesUnicodeWhitespaceNames(string name) {
        var items = await ParseListingAsync("", " \t", name, "README");
        Assert.That(items.Select(item => item.Name), Is.EqualTo(new[] { name, "README" }));
    }

    [TestCaseSource(nameof(TestCases))]
    public async Task ChunkBoundariesPreserveSampleListings(string line, DirectoryListItem expected) {
        async IAsyncEnumerable<string> contents() {
            foreach (var character in line + "\r\n") {
                yield return character.ToString();
            }
            await Task.CompletedTask;
        }

        var parser = new FtpListingParser(ServerTypeForLine(line), () => TestUtcNow);
        var item = (await ToListAsync(parser.ParseContents(contents(), default))).Single();
        Assert.That(new object[] { item.Name, item.Size, item.Time, item.Target, item.Flags,
            item.Permissions, item.OwnerGroup }, Is.EqualTo(new object[] {
            expected.Name, expected.Size, expected.Time, expected.Target, expected.Flags,
            expected.Permissions, expected.OwnerGroup }));
    }

    [TestCase("-rw-r--r-- 1 user group 5 Jan 1 2024 filename", DirectoryListServerType.Default, true)]
    [TestCase("04-27-00 12:09PM 5 filename", DirectoryListServerType.Default, true)]
    [TestCase("file.txt;1 10 2-JUL-2003", DirectoryListServerType.Default, true)]
    [TestCase("owner 5 04-27-00 12:30 0 filename", DirectoryListServerType.Default, true)]
    [TestCase("filename 1014196 06/03/04 Thur. 10:20:03", DirectoryListServerType.Default, true)]
    [TestCase("5 Jan 1 2000 12:00 filename", DirectoryListServerType.Default, true)]
    [TestCase("66-MVSPDSMEMBER 01.01 2004/06/22 2004/06/22 16:32 128 128 0 BOBY12", DirectoryListServerType.Mvs, true)]
    [TestCase("FILE TRACE V 65 107 2 2005-10-04 15:28:42 OWNER", DirectoryListServerType.Zvm, true)]
    [TestCase("Type=file;modify=20081105165215; filename", DirectoryListServerType.Default, false)]
    [TestCase("+m825718503,s280,\tfilename", DirectoryListServerType.Default, false)]
    [TestCase("0100644 500 101 12345 123456789 filename", DirectoryListServerType.Default, false)]
    [TestCase("WYOSPT 3420 2003/05/21 1 200 FB 80 8053 PS filename", DirectoryListServerType.Mvs, false)]
    [TestCase("filename 101 528 6-Apr-07 14:21:18 255,255 \"oooo\"", DirectoryListServerType.HpNonStop, false)]
    [TestCase("20.20 07/03/29 1026 d-ewrewr 2650 85920 filename", DirectoryListServerType.Default, false)]
    public async Task TimestampOffsetsFollowTheDocumentedFormatPolicy(string line,
        DirectoryListServerType serverType, bool usesListingOffset) {
        var baseline = (await ParseListingAsync(serverType, line)).Single();
        var adjusted = (await ParseListingAsync(serverType, [line], null,
            TimeSpan.FromHours(2), TimeSpan.FromHours(-1))).Single();
        Assert.That(baseline.Time.HasValue, Is.True);
        Assert.That(adjusted.Parser, Is.EqualTo(baseline.Parser));
        Assert.That(adjusted.Time,
            Is.EqualTo(baseline.Time.Value.AddHours(usesListingOffset ? 1 : -1)));
    }

    [Test]
    public async Task DateOnlyVmsMetadataRetainsTheDocumentedNullRepresentation() {
        var item = (await ParseListingAsync("file.txt;1 10 2-JUL-2003")).Single();
        Assert.That(item.Permissions, Is.Null);
        Assert.That(item.OwnerGroup, Is.Null);
    }

    [TestCase("filename", "")]
    [TestCase("filename.tar.gz", ".gz")]
    [TestCase(".gitignore", ".gitignore")]
    public void EntryPropertiesMatchTheDocumentedApi(string name, string extension) {
        var item = new DirectoryListItem(name, null, null, "", "", null, DirectoryListItemFlags.File);
        Assert.That(item.Extension, Is.EqualTo(extension));
        Assert.That(item.Target, Is.Null);
        Assert.That((int)item.Flags, Is.Zero);
    }

    public static IEnumerable<object[]> TestLookup => TestCases.OfType<object[]>().Select((arr, i) => new object[] {
        i,
        arr[1]
    });

    [TestCaseSource(nameof(TestLookup))]
    public async Task NameIsParsedAtIndex(int i, DirectoryListItem info) {
        await CompareAsync(i, info, e => e.Name);
    }

    [TestCaseSource(nameof(TestLookup))]
    public async Task PermissionsAreParsedAtIndex(int i, DirectoryListItem info) {
        await CompareAsync(i, info, e => e.Permissions);
    }

    [TestCaseSource(nameof(TestLookup))]
    public async Task SizeIsParsedAtIndex(int i, DirectoryListItem info) {
        await CompareAsync(i, info, e => e.Size);
    }

    [TestCaseSource(nameof(TestLookup))]
    public async Task OwnerGroupIsParsedAtIndex(int i, DirectoryListItem info) {
        await CompareAsync(i, info, e => e.OwnerGroup);
    }

    [TestCaseSource(nameof(TestLookup))]
    public async Task TimeIsParsedAtIndex(int i, DirectoryListItem info) {
        await CompareAsync(i, info, e => e.Time);
    }

    [TestCaseSource(nameof(TestLookup))]
    public async Task FlagsAreParsedAtIndex(int i, DirectoryListItem info) {
        await CompareAsync(i, info, e => e.Flags);
    }

    [TestCaseSource(nameof(TestLookup))]
    public async Task TargetIsParsedAtIndex(int i, DirectoryListItem info) {
        await CompareAsync(i, info, e => e.Target);
    }
}
