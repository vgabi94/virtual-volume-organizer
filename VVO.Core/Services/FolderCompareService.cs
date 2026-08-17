using VVO.Core.Models;

namespace VVO.Core.Services;

public class FolderCompareService : IFolderCompareService
{
    public Task<IReadOnlyList<ComparisonResult>> CompareAsync(
        RootFolderMetadata leftMetadata,
        IReadOnlyCollection<FileRecord> leftRecords,
        RootFolderMetadata rightMetadata,
        IReadOnlyCollection<FileRecord> rightRecords,
        bool includeUnchanged = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var leftRoot = FindRoot(leftMetadata, leftRecords, nameof(leftRecords));
        var rightRoot = FindRoot(rightMetadata, rightRecords, nameof(rightRecords));

        return Task.Run<IReadOnlyList<ComparisonResult>>(() =>
        {
            progress?.Report("Indexing folders...");
            var leftChildren = BuildChildrenMap(leftRecords, cancellationToken);
            var rightChildren = BuildChildrenMap(rightRecords, cancellationToken);

            progress?.Report("Comparing folders...");
            var results = new List<ComparisonResult>();
            CompareFolders(leftRoot, rightRoot, string.Empty);

            return results;

            // Appends everything that differs beneath a folder pair present on both sides.
            // Returns true when anything in the subtree differed.
            bool CompareFolders(FileRecord left, FileRecord right, string relativePath)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // The folder's own row has to come before its children, but its status is
                // only known once they have all been compared, so reserve the slot now and
                // fill it in on the way back up.
                var slot = results.Count;
                results.Add(null!);

                var changed = false;
                foreach (var (name, leftChild, rightChild) in PairChildren(left, right))
                {
                    var childPath = Combine(relativePath, name);

                    if (leftChild == null)
                    {
                        AddCollapsed(rightChild!, childPath, ComparisonStatus.Added, rightChildren);
                        changed = true;
                    }
                    else if (rightChild == null)
                    {
                        AddCollapsed(leftChild, childPath, ComparisonStatus.Removed, leftChildren);
                        changed = true;
                    }
                    else if (leftChild.IsFolder != rightChild.IsFolder)
                    {
                        // A file replaced by a folder has nothing to compare against
                        AddCollapsed(leftChild, childPath, ComparisonStatus.Removed, leftChildren);
                        AddCollapsed(rightChild, childPath, ComparisonStatus.Added, rightChildren);
                        changed = true;
                    }
                    else if (leftChild.IsFolder)
                    {
                        changed |= CompareFolders(leftChild, rightChild, childPath);
                    }
                    else
                    {
                        changed |= CompareFiles(leftChild, rightChild, childPath);
                    }
                }

                if (changed)
                {
                    results[slot] = new ComparisonResult
                    {
                        RelativePath = relativePath,
                        Status = ComparisonStatus.Changed,
                        Left = left,
                        Right = right
                    };

                    return true;
                }

                // Nothing below this folder differs, so the whole subtree collapses into a
                // single row here, or into no row at all when only differences were asked for.
                results.RemoveRange(slot, results.Count - slot);
                if (includeUnchanged)
                {
                    results.Add(new ComparisonResult
                    {
                        RelativePath = relativePath,
                        Status = ComparisonStatus.Unchanged,
                        Left = left,
                        Right = right,
                        DescendantCount = CountDescendants(left, leftChildren)
                    });
                }

                return false;
            }

            // Returns true when the two differ
            bool CompareFiles(FileRecord left, FileRecord right, string relativePath)
            {
                var changes = ChangeKind.None;
                if (left.Size != right.Size)
                {
                    changes |= ChangeKind.Size;
                }

                if (left.Modified != right.Modified)
                {
                    changes |= ChangeKind.Modified;
                }

                if (changes == ChangeKind.None)
                {
                    if (includeUnchanged)
                    {
                        results.Add(new ComparisonResult
                        {
                            RelativePath = relativePath,
                            Status = ComparisonStatus.Unchanged,
                            Left = left,
                            Right = right
                        });
                    }

                    return false;
                }

                results.Add(new ComparisonResult
                {
                    RelativePath = relativePath,
                    Status = ComparisonStatus.Changed,
                    Changes = changes,
                    Left = left,
                    Right = right
                });

                return true;
            }

            // A subtree that is entirely added or entirely removed becomes one row. It is
            // still walked, but only to count what is inside it.
            void AddCollapsed(FileRecord record, string relativePath, ComparisonStatus status,
                Dictionary<Guid, List<FileRecord>> children)
            {
                results.Add(new ComparisonResult
                {
                    RelativePath = relativePath,
                    Status = status,
                    Left = status == ComparisonStatus.Removed ? record : null,
                    Right = status == ComparisonStatus.Added ? record : null,
                    DescendantCount = record.IsFolder ? CountDescendants(record, children) : null
                });
            }

            int CountDescendants(FileRecord folder, Dictionary<Guid, List<FileRecord>> children)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!children.TryGetValue(folder.Id, out var direct))
                {
                    return 0;
                }

                var count = direct.Count;
                foreach (var child in direct)
                {
                    if (child.IsFolder)
                    {
                        count += CountDescendants(child, children);
                    }
                }

                return count;
            }

            // Merges the children of a folder pair by name, in a stable order so that the
            // result never depends on the order the records arrive in.
            IEnumerable<(string Name, FileRecord? Left, FileRecord? Right)> PairChildren(
                FileRecord left, FileRecord right)
            {
                var leftByName = ByName(left, leftChildren);
                var rightByName = ByName(right, rightChildren);

                var names = new List<string>(leftByName.Keys);
                foreach (var name in rightByName.Keys)
                {
                    if (!leftByName.ContainsKey(name))
                    {
                        names.Add(name);
                    }
                }

                names.Sort(StringComparer.OrdinalIgnoreCase);

                foreach (var name in names)
                {
                    leftByName.TryGetValue(name, out var leftChild);
                    rightByName.TryGetValue(name, out var rightChild);

                    yield return (name, leftChild, rightChild);
                }
            }

            Dictionary<string, FileRecord> ByName(FileRecord folder, Dictionary<Guid, List<FileRecord>> children)
            {
                var byName = new Dictionary<string, FileRecord>(StringComparer.OrdinalIgnoreCase);
                if (children.TryGetValue(folder.Id, out var direct))
                {
                    foreach (var child in direct)
                    {
                        byName[child.Name] = child;
                    }
                }

                return byName;
            }
        }, cancellationToken);
    }

    private static FileRecord FindRoot(RootFolderMetadata metadata, IReadOnlyCollection<FileRecord> records,
        string paramName)
    {
        var root = records.FirstOrDefault(record => record.Id == metadata.TreeId);
        if (root == null)
        {
            throw new ArgumentException(
                $"The records do not contain the root folder '{metadata.TreeId}' described by the given metadata.",
                paramName);
        }

        return root;
    }

    private static Dictionary<Guid, List<FileRecord>> BuildChildrenMap(IReadOnlyCollection<FileRecord> records,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, List<FileRecord>>();

        foreach (var record in records)
        {
            if (record.ParentId == null) continue;

            if (!map.TryGetValue(record.ParentId.Value, out var children))
            {
                children = new List<FileRecord>();
                map[record.ParentId.Value] = children;
            }

            children.Add(record);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return map;
    }

    private static string Combine(string parentPath, string name)
    {
        return parentPath.Length == 0 ? name : parentPath + Path.DirectorySeparatorChar + name;
    }
}
