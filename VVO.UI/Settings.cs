using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace VVO.UI
{
    public class Settings
    {
        public readonly struct SettingsData
        {
            public List<string> RecentFiles { get; init; }
            public int MaxRecentFiles { get; init; }

            // Database path -> ids of the virtual volumes the user has collapsed there. Only
            // the collapsed ones are listed, so a volume the user has never touched opens.
            public Dictionary<string, List<string>> CollapsedVolumes { get; init; }

            // When false a folder's path and description show only while it is under the
            // pointer or selected
            public bool ShowFolderDetailsAlways { get; init; }

            // When false a scan leaves out hidden and system entries, which on a system drive
            // is most of ProgramData and every AppData
            public bool ScanHiddenAndSystem { get; init; }

            // Nullable so that a file written before there was a theme to choose reads as the
            // dark one it was wearing, rather than as a light one the user never asked for
            public bool? DarkTheme { get; init; }
        }

        private const int DefaultMaxRecentFiles = 7;

        public SettingsData Data { get; private set; } = new()
        {
            RecentFiles = [],
            MaxRecentFiles = DefaultMaxRecentFiles,
            CollapsedVolumes = [],
            DarkTheme = true
        };

        private readonly string _filePath;
        private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public Settings()
            : this(Path.Combine(AppContext.BaseDirectory, "settings.json"))
        {
        }

        /// <summary>
        /// Reads and writes the settings at a chosen path, which is what lets a test work on a
        /// file of its own instead of the one beside the executable.
        /// </summary>
        public Settings(string filePath)
        {
            _filePath = filePath;

            if (File.Exists(_filePath))
            {
                try
                {
                    string json = File.ReadAllText(_filePath);
                    var loaded = JsonSerializer.Deserialize<SettingsData>(json);

                    // A file missing either member deserializes to a null list and a cap of
                    // zero, and a cap of zero silently discards every entry as it is added
                    Data = new SettingsData
                    {
                        RecentFiles = loaded.RecentFiles ?? [],
                        MaxRecentFiles = loaded.MaxRecentFiles > 0 ? loaded.MaxRecentFiles : DefaultMaxRecentFiles,
                        CollapsedVolumes = loaded.CollapsedVolumes ?? [],
                        ShowFolderDetailsAlways = loaded.ShowFolderDetailsAlways,
                        ScanHiddenAndSystem = loaded.ScanHiddenAndSystem,
                        DarkTheme = loaded.DarkTheme
                    };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading settings, using defaults: {ex.Message}");
                    Save();
                }
            }
            else
            {
                Save();
            }
        }

        public void Save()
        {
            string json = JsonSerializer.Serialize(Data, _jsonOptions);
            File.WriteAllText(_filePath, json);
        }

        public void AddRecentFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            Data.RecentFiles.Remove(path);
            Data.RecentFiles.Insert(0, path);

            if (Data.RecentFiles.Count > Data.MaxRecentFiles)
            {
                Data.RecentFiles.RemoveAt(Data.RecentFiles.Count - 1);
            }

            Save();
        }

        public void RemoveRecentFile(string path)
        {
            if (Data.RecentFiles.Remove(path))
            {
                Save();
            }
        }

        public void ClearRecentFiles()
        {
            Data.RecentFiles.Clear();
            Save();
        }

        public void SetShowFolderDetailsAlways(bool value)
        {
            if (Data.ShowFolderDetailsAlways == value) return;

            Data = Data with { ShowFolderDetailsAlways = value };
            Save();
        }

        /// <summary>The theme to open on, dark until the user says otherwise.</summary>
        public bool IsDarkTheme => Data.DarkTheme ?? true;

        public void SetDarkTheme(bool value)
        {
            if (Data.DarkTheme == value) return;

            Data = Data with { DarkTheme = value };
            Save();
        }

        public void SetScanHiddenAndSystem(bool value)
        {
            if (Data.ScanHiddenAndSystem == value) return;

            Data = Data with { ScanHiddenAndSystem = value };
            Save();
        }

        public void SetMaxRecentFiles(int value)
        {
            if (value < 1 || Data.MaxRecentFiles == value) return;

            Data = Data with { MaxRecentFiles = value };

            // Lowering the cap only takes effect on the next open unless the list is trimmed now
            while (Data.RecentFiles.Count > value)
            {
                Data.RecentFiles.RemoveAt(Data.RecentFiles.Count - 1);
            }

            Save();
        }

        public bool IsVolumeExpanded(string databasePath, Guid volumeId)
        {
            return !Data.CollapsedVolumes.TryGetValue(databasePath, out var collapsed)
                   || !collapsed.Contains(volumeId.ToString());
        }

        public void SetVolumeExpanded(string databasePath, Guid volumeId, bool expanded)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) return;

            var id = volumeId.ToString();
            Data.CollapsedVolumes.TryGetValue(databasePath, out var collapsed);

            if (expanded)
            {
                if (collapsed == null || !collapsed.Remove(id)) return;

                if (collapsed.Count == 0)
                {
                    Data.CollapsedVolumes.Remove(databasePath);
                }
            }
            else
            {
                if (collapsed == null)
                {
                    collapsed = [];
                    Data.CollapsedVolumes[databasePath] = collapsed;
                }

                if (collapsed.Contains(id)) return;

                collapsed.Add(id);
            }

            Save();
        }

        public void ForgetVolume(string databasePath, Guid volumeId)
        {
            SetVolumeExpanded(databasePath, volumeId, expanded: true);
        }
    }
}
