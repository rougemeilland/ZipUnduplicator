using System;
using System.Collections.Generic;
using Palmtree;
using Palmtree.IO;
using Palmtree.IO.Compression.Archive.Zip;

namespace ZipUnduplicator.CUI
{
    internal sealed class ZipEntrySummary
    {
        private sealed class EntriesComparerById
            : IComparer<ZipEntrySummary>
        {
            public int Compare(ZipEntrySummary? x, ZipEntrySummary? y)
            {
                if (x is null)
                    return y is null ? 0 : -1;
                if (y is null)
                    return 1;
                return x.Id.CompareTo(y.Id);
            }
        }

        private sealed class EntriesComparerByFullName
            : IComparer<ZipEntrySummary>
        {
            public int Compare(ZipEntrySummary? x, ZipEntrySummary? y)
            {
                if (x is null)
                    return y is null ? 0 : -1;
                if (y is null)
                    return 1;
                int c;
                if ((c = string.Compare(x.FullName, y.FullName, StringComparison.OrdinalIgnoreCase)) != 0)
                    return c;
                return x.Id.CompareTo(y.Id);
            }
        }

        private sealed class EntriesComparerBySizeAndCrc
            : IComparer<ZipEntrySummary>
        {
            public int Compare(ZipEntrySummary? x, ZipEntrySummary? y)
            {
                if (x is null)
                    return y is null ? 0 : -1;
                if (y is null)
                    return 1;
                int c;
                if ((c = x.Size.CompareTo(y.Size)) != 0)
                    return c;
                if ((c = x.Crc.CompareTo(y.Crc)) != 0)
                    return c;
                if ((c = string.Compare(x.FullName, y.FullName, StringComparison.OrdinalIgnoreCase)) != 0)
                    return c;
                return x.Id.CompareTo(y.Id);
            }
        }

        static ZipEntrySummary()
        {
            ComparerById = new EntriesComparerById();
            ComparerByFullName = new EntriesComparerByFullName();
            ComparerBySizeAndCrc = new EntriesComparerBySizeAndCrc();
        }

        private ZipEntrySummary(ZipEntryId id, string fullName, ulong size, uint crc, DateTimeOffset? lastWriteTimeUtc)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

            Id = id;
            FullName = fullName;
            Size = size;
            Crc = crc;
            LastWriteTimeUtc = lastWriteTimeUtc;
        }

        public static IComparer<ZipEntrySummary> ComparerById { get; }
        public static IComparer<ZipEntrySummary> ComparerByFullName { get; }
        public static IComparer<ZipEntrySummary> ComparerBySizeAndCrc { get; }
        public ZipEntryId Id { get; }
        public string FullName { get; }
        public ulong Size { get; }
        public uint Crc { get; }
        public DateTimeOffset? LastWriteTimeUtc { get; }

        public static ZipEntrySummary CreateInstance(ZipSourceEntry entry)
            => new(entry.ID, entry.FullName, entry.Size, entry.Crc, entry.LastWriteTimeOffsetUtc);

        public static bool EqualsByFullNameAndSizeAndCrc(ZipEntrySummary entry1, ZipEntrySummary entry2)
        {
            if (entry1.Size != entry2.Size)
                return false;
            if (entry1.Crc != entry2.Crc)
                return false;
            if (!string.Equals(entry1.FullName, entry2.FullName, StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        public static bool EqualsByFullNameAndSizeAndCrc(ZipSourceEntry entry1, ZipSourceEntry entry2, IProgress<double> progress)
        {
            if (entry1.Size != entry2.Size)
                return false;
            if (entry1.Crc != entry2.Crc)
                return false;
            if (!string.Equals(entry1.FullName, entry2.FullName, StringComparison.OrdinalIgnoreCase))
                return false;
            var progressCounter = new ProgressCounter<double>(progress.Report, 0);
            progressCounter.Report();
            using var contentStream1 = entry1.OpenContentStream();
            using var contentStream2 = entry2.OpenContentStream();
            var result = contentStream1.StreamBytesEqual(contentStream2, new SimpleProgress<ulong>(value => progressCounter.AddValue((double)value / entry1.Size)));
            progressCounter.Report();
            return result;
        }

        public static bool EqualsBySizeAndCrc(ZipEntrySummary entry1, ZipEntrySummary entry2)
        {
            if (entry1.Size != entry2.Size)
                return false;
            if (entry1.Crc != entry2.Crc)
                return false;
            return true;
        }

        public static bool EqualsBySizeAndCrc(ZipSourceEntry entry1, ZipSourceEntry entry2, IProgress<double> progress)
        {
            if (entry1.Size != entry2.Size)
                return false;
            if (entry1.Crc != entry2.Crc)
                return false;
            var progressCounter = new ProgressCounter<double>(progress.Report, 0);
            progressCounter.Report();
            using var contentStream1 = entry1.OpenContentStream();
            using var contentStream2 = entry2.OpenContentStream();
            var result = contentStream1.StreamBytesEqual(contentStream2, new SimpleProgress<ulong>(value => progressCounter.AddValue((double)value / entry1.Size)));
            progressCounter.Report();
            return result;
        }
    }
}
