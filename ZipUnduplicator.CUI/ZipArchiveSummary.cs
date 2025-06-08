using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Palmtree;
using Palmtree.IO;
using Palmtree.IO.Compression.Archive.Zip;
using Palmtree.Linq;

namespace ZipUnduplicator.CUI
{
    internal sealed partial class ZipArchiveSummary
    {
        private sealed class ReadOnlyCollection<ELEMENT_T>
            : IReadOnlyCollection<ELEMENT_T>
        {
            private readonly List<ELEMENT_T> _elements;

            public ReadOnlyCollection(IEnumerable<ELEMENT_T> elements)
            {
                _elements = [.. elements];
            }

            public int Count => _elements.Count;

            public IEnumerator<ELEMENT_T> GetEnumerator() => _elements.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed partial class ZipArchiveSummaryComparerByUsefullness
            : IComparer<ZipArchiveSummary>
        {
            public int Compare(ZipArchiveSummary? x, ZipArchiveSummary? y)
            {
                if (x is null)
                    return y is null ? 0 : -1;
                if (y is null)
                    return 1;
                var t1 = x.LastWriteTime;
                var t2 = y.LastWriteTime;
                int c;
                if (t1 is null)
                {
                    if (t2 is not null)
                        return 1;
                }
                else
                {
                    if (t2 is null)
                    {
                        return -1;
                    }
                    else
                    {
                        if ((c = t1.Value.CompareTo(t2.Value)) != 0)
                            return c;
                    }
                }

                var (body1, number1) = ParseArchiveFileName(x);
                var (body2, number2) = ParseArchiveFileName(y);
                if ((c = body1.Length.CompareTo(body2.Length)) != 0)
                    return -c;
                if ((c = number1.CompareTo(number2)) != 0)
                    return c;
                return 0;

                static (string body, int number) ParseArchiveFileName(ZipArchiveSummary zipArchiveSummary)
                {
                    var match = GetZipArchiveFileNamePattern().Match(zipArchiveSummary.ZipArchive.NameWithoutExtension);
                    Validation.Assert(match.Success == true, "match1.Success == true");
                    var body = match.Groups["body"].Value;
                    var numberMatchGroup = match.Groups["number"];
                    var number = numberMatchGroup.Success ? int.Parse(numberMatchGroup.Value, CultureInfo.InvariantCulture.NumberFormat) : -1;
                    return (body, number);
                }
            }

            [GeneratedRegex(@"^(?<body>.*?)( +\((?<number>\d+)\))?$", RegexOptions.Compiled)]
            private static partial Regex GetZipArchiveFileNamePattern();
        }

        static ZipArchiveSummary()
        {
            ComparerByUsefullness = new ZipArchiveSummaryComparerByUsefullness();
        }

        private ZipArchiveSummary(FilePath zipArchive, IEnumerable<ZipEntrySummary> entries)
        {
            ZipArchive = zipArchive;
            var entriesArray = entries.ToArray().AsEnumerable();
            LastWriteTime =
                entriesArray.Aggregate(
                    (DateTimeOffset?)null,
                    (value, element) =>
                    {
                        var otherValue = element.LastWriteTimeUtc;
                        if (value is null)
                            return otherValue is null ? null : otherValue;
                        if (otherValue is null)
                            return value;
                        return value.Value.Maximum(otherValue.Value);
                    });
            EntriesOrderById = new ReadOnlyCollection<ZipEntrySummary>(entriesArray.QuickSort(ZipEntrySummary.ComparerById));
            EntriesOrderByFullName = new ReadOnlyCollection<ZipEntrySummary>(entriesArray.QuickSort(ZipEntrySummary.ComparerByFullName));
            EntriesOrderBySizeAndCrc = new ReadOnlyCollection<ZipEntrySummary>(entriesArray.QuickSort(ZipEntrySummary.ComparerBySizeAndCrc));
        }

        public static IComparer<ZipArchiveSummary> ComparerByUsefullness { get; }
        public FilePath ZipArchive { get; }
        public DateTimeOffset? LastWriteTime { get; }
        public IReadOnlyCollection<ZipEntrySummary> EntriesOrderById { get; }
        public IReadOnlyCollection<ZipEntrySummary> EntriesOrderByFullName { get; }
        public IReadOnlyCollection<ZipEntrySummary> EntriesOrderBySizeAndCrc { get; }

        public static ZipArchiveSummary CreateInstance(FilePath zipArchive)
        {
            using var zipReader = zipArchive.OpenAsZipFile();
            return
                new ZipArchiveSummary(
                    zipArchive,
                    zipReader
                        .EnumerateEntries()
                        .Where(entry => entry.IsFile)
                        .Select(ZipEntrySummary.CreateInstance));
        }

        public bool EqualEntries(ZipArchiveSummary other, bool strict)
        {
            var entries1 = strict ? EntriesOrderById : EntriesOrderByFullName;
            var entries2 = strict ? other.EntriesOrderById : other.EntriesOrderByFullName;
            if (entries1.Count != entries2.Count)
                return false;
            var entryPairs =
                entries1.Zip(entries2, (x, y) => (x, y))
                .ToList()
                .AsEnumerable();

            var equalityComparer = strict ? (Func<ZipEntrySummary, ZipEntrySummary, bool>)ZipEntrySummary.EqualsByFullNameAndSizeAndCrc : ZipEntrySummary.EqualsBySizeAndCrc;
            if (entryPairs.NotAny(item => equalityComparer(item.x, item.y)))
                return false;
            return true;
        }

        public bool EqualEntryContents(ZipArchiveSummary other, bool strict, IProgress<double> progress)
        {
            using var zipReader1 = ZipArchive.OpenAsZipFile();
            using var zipReader2 = other.ZipArchive.OpenAsZipFile();
            var entries1 = GetEntries(zipReader1, strict);
            var entries2 = GetEntries(zipReader2, strict);
            Validation.Assert(entries1.Count() == entries2.Count(), "entries1.Count() == entries2.Count()");
            var entryPairs =
                entries1.Zip(entries2, (x, y) => (x, y))
                .ToList();
            var progressCounter = new ProgressCounter<double>(progress.Report, 0);
            progressCounter.Report();
            try
            {
                var equalityComparer = strict ? (Func<ZipSourceEntry, ZipSourceEntry, IProgress<double>, bool>)ZipEntrySummary.EqualsByFullNameAndSizeAndCrc : ZipEntrySummary.EqualsBySizeAndCrc;
                foreach (var (x, y) in entryPairs)
                {
                    if (!equalityComparer(x, y, new SimpleProgress<double>(value => progressCounter.AddValue(value / entryPairs.Count))))
                        return false;
                }

                return true;
            }
            finally
            {
                progressCounter.SetValue(1);
            }

            static IEnumerable<ZipSourceEntry> GetEntries(ZipArchiveFileReader zipReader, bool strict)
            {
                var entries =
                    zipReader.EnumerateEntries()
                    .Where(entry => entry.IsFile);
                entries =
                    strict
                    ? entries.QuickSort(entry => entry.ID).AsEnumerable()
                    : entries.QuickSort(entry => entry.FullName, StringComparer.OrdinalIgnoreCase).AsEnumerable();
                return entries;
            }
        }

        public bool ContainEntries(ZipArchiveSummary other)
        {
            if (EntriesOrderBySizeAndCrc.Count <= other.EntriesOrderBySizeAndCrc.Count)
                return false;
            var entries1 = new List<ZipEntrySummary>(EntriesOrderBySizeAndCrc);
            var entries2 = new List<ZipEntrySummary>(other.EntriesOrderBySizeAndCrc);
            while (entries2.Count > 0)
            {
                if (entries1.Count <= entries2.Count)
                    break;
                Validation.Assert(entries1.Count > 0, "entries1.Count > 0");
                if (ZipEntrySummary.EqualsBySizeAndCrc(entries1[0], entries2[0]))
                    entries2.RemoveAt(0);
                entries1.RemoveAt(0);
            }

            return entries2.Count <= 0;
        }

        public bool ContainEntryContents(ZipArchiveSummary other, IProgress<double> progress)
        {
            using var zipReader1 = ZipArchive.OpenAsZipFile();
            using var zipReader2 = other.ZipArchive.OpenAsZipFile();
            var entries1 = GetEntries(zipReader1);
            var entries2 = GetEntries(zipReader2);
            Validation.Assert(entries1.Count > entries2.Count, "entries1.Count > entries2.Count");

            var progressCounter = new ProgressCounter<double>(progress.Report, 0);
            var totalCount = entries1.Count;
            progressCounter.Report();
            try
            {
                while (entries2.Count > 0)
                {
                    if (entries1.Count <= entries2.Count)
                        break;
                    Validation.Assert(entries1.Count > 0, "entries1.Count > 0");
                    if (ZipEntrySummary.EqualsBySizeAndCrc(entries1[0], entries2[0], new SimpleProgress<double>(value => progressCounter.AddValue(value / totalCount))))
                        entries2.RemoveAt(0);
                    entries1.RemoveAt(0);
                }

                return entries2.Count <= 0;
            }
            finally
            {
                progressCounter.SetValue(1);
            }

            static List<ZipSourceEntry> GetEntries(ZipArchiveFileReader zipReader)
                => [.. zipReader.EnumerateEntries().Where(entry => entry.IsFile)];
        }
    }
}
