using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Palmtree;
using Palmtree.Application;
using Palmtree.IO;
using Palmtree.IO.Compression.Archive.Zip;
using Palmtree.IO.Compression.Stream.Plugin.SevenZip;
using Palmtree.IO.Console;
using Palmtree.Linq;

namespace ZipUnduplicator.CUI
{
    internal class UnduplicatorApplication
        : BatchApplication
    {
        private class ZipArchiveGroups
        {
            private readonly List<Dictionary<string, ZipArchiveSummary>> _groups;
            private readonly HashSet<string> _files;

            public ZipArchiveGroups()
            {
                _groups = new List<Dictionary<string, ZipArchiveSummary>>();
                _files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            public void Add(ZipArchiveSummary summary1, ZipArchiveSummary summary2)
            {
                var zipFilePath1 = summary1.ZipArchive.FullName;
                var zipFilePath2 = summary2.ZipArchive.FullName;
                var group =
                    _groups
                    .Where(group => group.ContainsKey(zipFilePath1) || group.ContainsKey(zipFilePath2))
                    .FirstOrDefault();
                if (group is not null)
                {
                    _ = group.TryAdd(zipFilePath1, summary1);
                    _ = group.TryAdd(zipFilePath2, summary2);
                }
                else
                {
                    var dic = new Dictionary<string, ZipArchiveSummary>(StringComparer.OrdinalIgnoreCase)
                    {
                        { zipFilePath1, summary1 },
                        { zipFilePath2, summary2 }
                    };
                    _groups.Add(dic);
                }

                _ = _files.Add(zipFilePath1);
                _ = _files.Add(zipFilePath2);
            }

            public IEnumerable<IEnumerable<ZipArchiveSummary>> EnumerateGroup()
            {
                foreach (var group in _groups)
                    yield return EnumerateSummary(group);
            }

            public ulong Count => _groups.Aggregate(0UL, (value, group) => checked(value + (ulong)group.Count));

            public bool ContainsZipArchive(FilePath zipArchive)
                => _files.Contains(zipArchive.FullName);

            private static IEnumerable<ZipArchiveSummary> EnumerateSummary(Dictionary<string, ZipArchiveSummary> group)
            {
                foreach (var summary in group.Values)
                    yield return summary;
            }
        }

        private class ZipArchiveInclusion
        {
            private readonly List<(ZipArchiveSummary zipArchiveSummary, ZipArchiveSummary subZipArchiveSummary)> _inclusion;

            public ZipArchiveInclusion()
            {
                _inclusion = new List<(ZipArchiveSummary zipArchiveSummary, ZipArchiveSummary subZipArchiveSummary)>();
            }

            public ulong Count => checked((ulong)_inclusion.Count);

            public void Add(ZipArchiveSummary zipArchiveSummary, ZipArchiveSummary subZipArchiveSummary)
                => _inclusion.Add((zipArchiveSummary, subZipArchiveSummary));

            public IEnumerable<(ZipArchiveSummary zipArchiveSummary, ZipArchiveSummary subZipArchiveSummary)> EnumerateInclusions()
                => _inclusion;
        }

        private class FilePathEqualityComparer
            : IEqualityComparer<FilePath>
        {
            public bool Equals(FilePath? x, FilePath? y)
                => x is null
                    ? y is null
                    : y is not null && string.Equals(x.FullName, y.FullName, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode([DisallowNull] FilePath obj)
                => obj.FullName.ToUpperInvariant().GetHashCode();
        }

        private static readonly IEqualityComparer<FilePath> _filePathEqualityComparer;

        private readonly string? _title;
        private readonly Encoding? _encoding;

        static UnduplicatorApplication()
        {
            _filePathEqualityComparer = new FilePathEqualityComparer();
            Bzip2CoderPlugin.EnablePlugin();
            DeflateCoderPlugin.EnablePlugin();
            Deflate64CoderPlugin.EnablePlugin();
            LzmaCoderPlugin.EnablePlugin();
        }

        public UnduplicatorApplication(string? title, Encoding? encoding)
        {
            _title = title;
            _encoding = encoding;
        }

        protected override string ConsoleWindowTitle => _title ?? base.ConsoleWindowTitle;
        protected override Encoding? InputOutputEncoding => _encoding;

        protected override ResultCode Main(string[] args)
        {
            try
            {
                var newArgs = new List<string>();
                var strict = false;
                for (var index = 0; index < args.Length; ++index)
                {
                    var arg = args[index];
                    if (arg == "--strict")
                        strict = true;
                    else if (arg.StartsWith('-'))
                        throw new Exception($"An unsupported option is specified on the command line.: \"{arg}\"");
                    else
                        newArgs.Add(arg);
                }

                ReportProgress("Searching files...");
                var zipFilesByDirectory =
                    newArgs
                    .EnumerateFilesFromArgument(true)
                    .Where(IsValidPath)
                    .Where(file => string.Equals(file.Extension, ".zip", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(file => file.Directory.FullName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => (baseDirectory: g.First().Directory, zipFiles: g.ToArray()))
                    .Where(item => item.zipFiles.Length > 1)
                    .ToList();

                var totalCount = zipFilesByDirectory.Aggregate(0UL, (value, item) => checked(value + (ulong)item.zipFiles.Length));
                var processedCount = 0UL;
                foreach (var (baseDirectory, zipFiles) in zipFilesByDirectory)
                {
                    var a = (double)processedCount / totalCount;
                    var b = (double)zipFiles.Length / totalCount;
                    UnduplicateFilesOnDirectory(baseDirectory, zipFiles, strict, value => a + b * value);
                    checked
                    {
                        processedCount += (ulong)zipFiles.Length;
                    }
#if DEBUG
                    if (processedCount > totalCount)
                        throw new Exception();
#endif
                }

                ReportProgress(1, "", (progressRate, content) => $"{progressRate}");

                return ResultCode.Success;
            }
            catch (OperationCanceledException)
            {
                return ResultCode.Cancelled;
            }
            catch (Exception ex)
            {
                ReportException(ex);
                return ResultCode.Failed;
            }

            static bool IsValidPath(FilePath file)
            {
                if (file.Name.StartsWith('.'))
                    return false;
                for (var directory = file.Directory; directory is not null; directory = directory.Parent)
                {
                    if (directory.Name.StartsWith('.'))
                        return false;
                }

                return true;
            }
        }

        protected override void Finish(ResultCode result, bool isLaunchedByConsoleApplicationLauncher)
        {
            if (result == ResultCode.Success)
                TinyConsole.WriteLine("終了しました。");
            else if (result == ResultCode.Cancelled)
                TinyConsole.WriteLine("中断されました。");

            if (isLaunchedByConsoleApplicationLauncher)
            {
                TinyConsole.Beep();
                TinyConsole.WriteLine("ENTER キーを押すとウィンドウが閉じます。");
                _ = TinyConsole.ReadLine();
            }
        }

        private void UnduplicateFilesOnDirectory(DirectoryPath baseDirectory, FilePath[] zipFiles, bool strict, Func<double, double> progressValueConverter)
        {
            var costOfAnalyzing = 10.0 * zipFiles.Length;
            var costOfComparing = 1.0 * zipFiles.Length * (zipFiles.Length - 1);
            var totalCost = costOfAnalyzing + costOfComparing;

            var progressPass1_C = 0.0;
            var progressPass1_K = costOfAnalyzing / totalCost;
            var progressPass2_C = costOfAnalyzing / totalCost;
            var progressPass2_K = costOfComparing / totalCost;
            var invalidZipArchives = new HashSet<FilePath>(_filePathEqualityComparer);
            var zipArchiveSummaries = AnalyzeZipArchives(zipFiles, invalidZipArchives, value => progressValueConverter(progressPass1_C + progressPass1_K * value));
            var (groups, inclusions) = CompareZipArchives(baseDirectory, zipArchiveSummaries, invalidZipArchives, strict, value => progressValueConverter(progressPass2_C + progressPass2_K * value));
            DisposeZipArchives(groups, inclusions);
        }

        private ZipArchiveSummary[] AnalyzeZipArchives(FilePath[] zipFiles, HashSet<FilePath> invalidZipArchives, Func<double, double> valueConverter)
            => zipFiles
                .Select((zipFile, index) =>
                {
                    if (IsPressedBreak)
                        throw new OperationCanceledException();
                    ReportProgress(valueConverter((double)index / zipFiles.Length), zipFile.FullName, (progressRate, content) => $"{progressRate} analyzing \"{content}\".");
                    try
                    {
                        try
                        {
                            return ZipArchiveSummary.CreateInstance(zipFile);
                        }
                        catch (Exception ex)
                        {
                            throw new Exception($"Failed to read zip archive.: \"{zipFile.FullName}\"", ex);
                        }
                    }
                    catch (Exception ex)
                    {
                        ReportException(ex);
                        _ = invalidZipArchives.Add(zipFile);
                        return null;
                    }
                })
                .WhereNotNull()
                .ToArray();

        private (ZipArchiveGroups groups, ZipArchiveInclusion inclusions) CompareZipArchives(DirectoryPath baseDirectory, ZipArchiveSummary[] zipArchiveSummaries, HashSet<FilePath> invalidZipArchives, bool strict, Func<double, double> progressValueConverter)
        {
            var progressCounter = new ProgressCounter<double>(value => Report(progressValueConverter(value)), 0);
            var totalFileCount = checked((ulong)zipArchiveSummaries.Length * (ulong)(zipArchiveSummaries.Length - 1));
            var processedFileCount = 0UL;
            Report(0);
            var groups = new ZipArchiveGroups();
            var inclusions = new ZipArchiveInclusion();
            for (var index1 = 0; index1 < zipArchiveSummaries.Length; ++index1)
            {
                var zipArchiveSummary1 = zipArchiveSummaries[index1];
                for (var index2 = 0; index2 < zipArchiveSummaries.Length; ++index2)
                {
                    if (index1 != index2)
                    {
                        var zipArchiveSummary2 = zipArchiveSummaries[index2];
                        if (IsPressedBreak)
                            throw new OperationCanceledException();

                        if (!invalidZipArchives.Contains(zipArchiveSummary1.ZipArchive) && !invalidZipArchives.Contains(zipArchiveSummary2.ZipArchive))
                        {
                            try
                            {
                                try
                                {
                                    if (index1 < index2
                                        && !(groups.ContainsZipArchive(zipArchiveSummary1.ZipArchive) && groups.ContainsZipArchive(zipArchiveSummary2.ZipArchive)))
                                    {
                                        if (zipArchiveSummary1.EqualEntries(zipArchiveSummary2, strict)
                                            && zipArchiveSummary1.EqualEntryContents(zipArchiveSummary2, strict, new SimpleProgress<double>(value => progressCounter.SetValue((processedFileCount + value) / totalFileCount))))
                                        {
                                            groups.Add(zipArchiveSummary1, zipArchiveSummary2);
                                        }
                                    }

                                    if (zipArchiveSummary1.ContainEntries(zipArchiveSummary2)
                                        && zipArchiveSummary1.ContainEntryContents(zipArchiveSummary2, new SimpleProgress<double>(value => progressCounter.SetValue((processedFileCount + value) / totalFileCount))))
                                    {
                                        inclusions.Add(zipArchiveSummary1, zipArchiveSummary2);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    ValidateZipArchive(invalidZipArchives, zipArchiveSummary1.ZipArchive);
                                    ValidateZipArchive(invalidZipArchives, zipArchiveSummary2.ZipArchive);
                                    throw new Exception($"Failed to compare ZIP archives.: archive1=\"{zipArchiveSummary1.ZipArchive}\", archive2=\"{zipArchiveSummary2.ZipArchive}\"", ex);
                                }
                            }
                            catch (Exception ex)
                            {
                                ReportException(ex);
                            }
                        }

                        ++processedFileCount;
#if DEBUG
                        if (processedFileCount > totalFileCount)
                            throw new Exception();
#endif
                        progressCounter.SetValue((double)processedFileCount / totalFileCount);
                    }
                }
            }

            progressCounter.SetValue(1);
            return (groups, inclusions);

            void Report(double value)
                => ReportProgress(progressValueConverter(value), baseDirectory.FullName, (progressRate, content) => $"{progressRate} comparing files on directory \"{content}\".");

            static void ValidateZipArchive(HashSet<FilePath> invalidZipArchives, FilePath zipArchive)
            {
                try
                {
                    using var zipReader = zipArchive.OpenAsZipFile();
                    foreach (var entry in zipReader.EnumerateEntries())
                    {
                        using var cpntentStream = entry.OpenContentStream();
                        var buffer = new byte[64 * 1024];
                        while (true)
                        {
                            var length = cpntentStream.Read(buffer);
                            if (length <= 0)
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _ = invalidZipArchives.Add(zipArchive);
                    throw new Exception($"ZIP archive is corrupted.: \"{zipArchive.FullName}\"", ex);
                }
            }
        }

        private void DisposeZipArchives(ZipArchiveGroups groups, ZipArchiveInclusion inclusions)
        {
            var trashBox = TrashBox.OpenTrashBox();
            foreach (var group in groups.EnumerateGroup())
            {
                var disposedFiles =
                    group
                    .OrderBy(summary => summary, ZipArchiveSummary.ComparerByUsefullness)
                    .Skip(1)
                    .Select(summary => summary.ZipArchive);
                foreach (var disposedFile in disposedFiles)
                {
                    if (disposedFile.Exists)
                    {
                        _=trashBox.DisposeFile(disposedFile);
                        ReportInformationMessage($"Duplicate ZIP archives has been disposed.: \"{disposedFile.FullName}\"");
                    }
                }
            }

            var uselessFiles =
                inclusions.EnumerateInclusions()
                .Select(inclusion => inclusion.subZipArchiveSummary.ZipArchive);
            foreach (var uselessFile in uselessFiles)
            {
                if (uselessFile.Exists)
                {
                    var destinationDirectory = uselessFile.Directory.GetSubDirectory(".disposed").Create();
                    var pattern = new Regex(@"^(?<body>.*?)( +\(\d+\))?$", RegexOptions.Compiled);
                    var match = pattern.Match(uselessFile.NameWithoutExtension);
                    var body = match.Groups["body"].Value;
                    for (var count = 1; ; ++count)
                    {
                        var destinationFile = destinationDirectory.GetFile($"{body}{(count <= 1 ? "" : $" ({count})")}{uselessFile.Extension}");
                        if (!destinationFile.Exists)
                        {
                            uselessFile.MoveTo(destinationFile);
                            ReportInformationMessage($"Useless ZIP archives have been moved.: uselessArchive=\"{uselessFile.FullName}\", movedTo=\"{destinationFile}\"");
                            break;
                        }
                    }
                }
            }
        }
    }
}
