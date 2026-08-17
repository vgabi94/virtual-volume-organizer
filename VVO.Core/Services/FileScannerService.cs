using System.Diagnostics;
using System.IO.Enumeration;
using VVO.Core.Models;

namespace VVO.Core.Services;

public class FileScannerService : IFileScannerService
{
    // Often enough to look alive, rarely enough that the reports cost nothing next to the walk
    private const int ReportEveryMilliseconds = 200;

    /// <summary>
    /// Counts what the walk could not open rather than passing over it in silence. The runtime
    /// discards access errors before <see cref="ContinueOnError"/> is ever consulted, so the
    /// option that hides them has to be off for any of them to be seen at all.
    /// </summary>
    private sealed class CountingEnumerator : FileSystemEnumerator<FileRecord>
    {
        private readonly FileSystemEnumerable<FileRecord>.FindTransform _transform;

        public int Skipped { get; private set; }

        public CountingEnumerator(
            string directory,
            FileSystemEnumerable<FileRecord>.FindTransform transform,
            EnumerationOptions options)
            : base(directory, options)
        {
            _transform = transform;
        }

        protected override FileRecord TransformEntry(ref FileSystemEntry entry) => _transform(ref entry);

        // Carrying on is what IgnoreInaccessible did; the difference is that what was left
        // out is now counted rather than lost
        protected override bool ContinueOnError(int error)
        {
            Skipped++;
            return true;
        }
    }

    public async Task<ScanResult> ScanDirectoryAsync(
        string rootPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        string? label = null,
        string? description = null,
        bool includeHiddenAndSystem = false)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"The path '{rootPath}' does not exist or is inaccessible.");
        }

        rootPath = Path.GetFullPath(rootPath);
        rootPath = Path.TrimEndingDirectorySeparator(rootPath);
        
        string rootName = Path.GetFileName(rootPath);
        if (string.IsNullOrEmpty(rootName))
        {
            var drive = new DriveInfo(rootPath);
            if (drive.IsReady)
            {
                rootName = !string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.VolumeLabel : drive.Name;
            } 
        }
        
        var rootFolderId = Guid.NewGuid();
        var rootFolder = new FileRecord
        {
            Id = rootFolderId,
            RootFolderId = rootFolderId,
            ParentId = null,
            Name = rootName,
            IsFolder = true,
            Size = 0, // will be computed before return
            Created = TruncateToMilliseconds(Directory.GetCreationTimeUtc(rootPath)),
            Modified = TruncateToMilliseconds(Directory.GetLastWriteTimeUtc(rootPath)),
        };

        var rootFolderMetadata = new RootFolderMetadata
        {
            Id = Guid.NewGuid(),
            TreeId = rootFolderId,
            Path = rootPath,
            LastScanned = DateTime.UtcNow,
            Label = label,
            Description = description
        };

        // The walk reports each entry with its parent's path rather than an id, so the ids
        // handed out have to be findable by path again to link a child to its parent
        var dirPathToId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
        {
            [rootPath] = rootFolderId
        };

        var enumerationOptions = new EnumerationOptions
        {
            // Off so the errors reach CountingEnumerator, which goes on past them itself
            IgnoreInaccessible = false,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false,

            // The default drops hidden and system entries, which is most of a system drive
            AttributesToSkip = includeHiddenAndSystem
                ? FileAttributes.None
                : FileAttributes.Hidden | FileAttributes.System
        };

        var folderSize = new Dictionary<Guid, long>()
        {
            [rootFolderId] = 0
        };
        
        string? lastParentPath = null;
        var lastParentId = Guid.Empty;
        
        FileSystemEnumerable<FileRecord>.FindTransform transform =
            (ref FileSystemEntry entry) =>
            {
                var isDir = entry.IsDirectory;
                Guid parentId;

                // Fast path: avoid string allocation and dictionary lookup for consecutive entries in the same directory
                if (lastParentPath != null && entry.Directory.SequenceEqual(lastParentPath.AsSpan()))
                {
                    parentId = lastParentId;
                }
                else
                {
                    var parentPath = entry.Directory.ToString();
                    if (!dirPathToId.TryGetValue(parentPath, out parentId))
                    {
                        parentId = Guid.NewGuid();
                        dirPathToId[parentPath] = parentId;
                        folderSize[parentId] = 0;
                    }
                    lastParentPath = parentPath;
                    lastParentId = parentId;
                }

                Guid recordId;
                if (isDir)
                {
                    var fullPath = entry.ToFullPath();
                    if (!dirPathToId.TryGetValue(fullPath, out recordId))
                    {
                        recordId = Guid.NewGuid();
                        dirPathToId[fullPath] = recordId;
                        folderSize[recordId] = 0;
                    }
                }
                else
                {
                    recordId = Guid.NewGuid();
                }
                    
                var fileEntry = new FileRecord
                {
                    Id = recordId,
                    RootFolderId = rootFolderId,
                    ParentId = parentId,
                    Name = entry.FileName.ToString(),
                    IsFolder = isDir,
                    Size = entry.Length,
                    Created = TruncateToMilliseconds(entry.CreationTimeUtc.UtcDateTime),
                    Modified = TruncateToMilliseconds(entry.LastWriteTimeUtc.UtcDateTime),
                };

                if (!isDir)
                {
                    folderSize[parentId] += entry.Length;
                }

                return fileEntry;
            };

        return await Task.Run(() =>
        {
            var entries = new List<FileRecord>();
            int skipped;

            progress?.Report("Scanning files...");

            // A walk of a system drive runs for minutes with nothing to say for itself, which
            // reads as a hang. The count cannot be a fraction — the total is only known once
            // the walk is over — so it climbs instead, on a clock rather than per entry, since
            // every report crosses to the UI thread.
            var since = Stopwatch.StartNew();

            using (var enumerator = new CountingEnumerator(rootPath, transform, enumerationOptions))
            {
                while (enumerator.MoveNext())
                {
                    entries.Add(enumerator.Current);

                    if (since.ElapsedMilliseconds >= ReportEveryMilliseconds)
                    {
                        progress?.Report($"Scanning files... {entries.Count:N0} found");
                        since.Restart();
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }

                skipped = enumerator.Skipped;
            }

            progress?.Report($"Building folder map... {entries.Count:N0} entries");
            var childrenFolderMap = new Dictionary<Guid, List<Guid>>();
            
            foreach (var entry in entries)
            {
                // We build the children map only for folders
                // because individual sizes of files were already summed up
                // during the FileSystemEnumerable iteration above.
                if (!entry.IsFolder) continue;

                Debug.Assert(entry.ParentId != null, "entry.ParentId should not be null!");
                if (!childrenFolderMap.TryGetValue(entry.ParentId.Value, out var list))
                {
                    list = new List<Guid>();
                    childrenFolderMap[entry.ParentId.Value] = list;
                }
                
                list.Add(entry.Id);
                cancellationToken.ThrowIfCancellationRequested();
            }
            
            progress?.Report($"Computing folder sizes... {entries.Count:N0} entries");
            ComputeSize(rootFolderId);
            
            progress?.Report($"Assigning folder sizes... {entries.Count:N0} entries");
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.IsFolder)
                {
                    entries[i] = entry with { Size = folderSize[entry.Id] };
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            entries.Add(rootFolder with { Size = folderSize[rootFolderId] });
            return new ScanResult(rootFolderMetadata, entries, skipped);
            
            long ComputeSize(Guid folderId)
            {
                long size = folderSize.GetValueOrDefault(folderId, 0);
                if (childrenFolderMap.TryGetValue(folderId, out var childrenFolders))
                {
                    foreach (var childFolderId in childrenFolders)
                    {
                        size += ComputeSize(childFolderId);
                    }
                }
                
                folderSize[folderId] = size;
                cancellationToken.ThrowIfCancellationRequested();
                
                return size;
            }
        }, cancellationToken);
    }

    // Dates persist with millisecond precision, so drop the extra ticks up front
    // rather than let scanned records differ from the ones read back later.
    private static DateTime TruncateToMilliseconds(DateTime value)
    {
        return new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, value.Kind);
    }
}