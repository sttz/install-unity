using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using static sttz.InstallUnity.UnityReleaseAPIClient;

namespace sttz.InstallUnity
{

/// <summary>
/// Information about a Unity version available to install.
/// </summary>
public struct VersionMetadata
{
    /// <summary>
    /// Create a new version from a release.
    /// </summary>
    public static VersionMetadata FromRelease(Release release)
    {
        return new VersionMetadata() { release = release };
    }

    /// <summary>
    /// The release metadata, in the format of the Unity Release API.
    /// </summary>
    public Release release;

    /// <summary>
    /// Shortcut to the Unity version of this release.
    /// </summary>
    public UnityVersion Version => release?.version ?? default;

    /// <summary>
    /// Base URL of where INIs are stored.
    /// </summary>
    public string baseUrl;

    /// <summary>
    /// Determine wether the packages metadata has been loaded.
    /// </summary>
    public bool HasDownload(Platform platform, Architecture architecture)
    {
        return GetEditorDownload(platform, architecture) != null;
    }

    /// <summary>
    /// Get platform specific packages without adding virtual packages.
    /// </summary>
    public EditorDownload GetEditorDownload(Platform platform, Architecture architecture)
    {
        if (release.downloads == null)
            return null;

        foreach (var editor in release.downloads) {
            if (editor.platform == platform && editor.architecture == architecture)
                return editor;
        }

        return null;
    }

    /// <summary>
    /// Set platform specific packages.
    /// </summary>
    public void SetEditorDownload(EditorDownload download)
    {
        if (release.downloads == null)
            release.downloads = new List<EditorDownload>();

        for (int i = 0; i < release.downloads.Count; i++) {
            var editor = release.downloads[i];
            if (editor.platform == download.platform && editor.architecture == download.architecture) {
                // Replace existing download
                release.downloads[i] = download;
                return;
            }
        }

        // Add new download
        release.downloads.Add(download);
    }

    /// <summary>
    /// Find a package by identifier, ignoring case.
    /// </summary>
    public Module GetModule(Platform platform, Architecture architecture, string id)
    {
        var editor = GetEditorDownload(platform, architecture);
        if (editor == null) return null;

        foreach (var module in editor.modules) {
            if (module.id.Equals(id, StringComparison.OrdinalIgnoreCase))
                return module;
        }

        return null;
    }
}

/// <summary>
/// Index of available Unity versions.
/// </summary>
public class VersionsCache : IEnumerable<VersionMetadata>
{
    string dataFilePath;
    Cache cache;

    ILogger Logger = UnityInstaller.CreateLogger<VersionsCache>();

    /// <summary>
    /// Version of cache format.
    /// </summary>
    const int CACHE_FORMAT = 3;

    /// <summary>
    /// Data written out to JSON file.
    /// </summary>
    struct Cache
    {
        public int format;
        public List<VersionMetadata> versions;
        public Dictionary<UnityVersion.Type, DateTime> updated;
    }

    /// <summary>
    /// Create a new database.
    /// </summary>
    /// <param name="dataPath">Path to the database file.</param>
    public VersionsCache(string dataFilePath)
    {
        this.dataFilePath = dataFilePath;

        if (File.Exists(dataFilePath)) {
            try {
                var json = File.ReadAllText(dataFilePath);
                cache = JsonConvert.DeserializeObject<Cache>(json);
                if (cache.format != CACHE_FORMAT) {
                    Logger.LogInformation($"Cache format is outdated, resetting cache.");
                    cache = new Cache();
                } else {
                    SortVersions();
                    Logger.LogInformation($"Loaded versions cache from '{dataFilePath}'");
                }
            } catch (Exception e) {
                Console.Error.WriteLine("ERROR: Could not read versions database file: " + e.Message);
                Console.Error.WriteLine(e.InnerException);
            }
        }

        cache.format = CACHE_FORMAT;

        if (cache.versions == null) {
            Logger.LogInformation("Creating a new empty versions cache");
            cache.versions = new List<VersionMetadata>();
        }
        if (cache.updated == null) {
            cache.updated = new Dictionary<UnityVersion.Type, DateTime>();
        }
    }

    /// <summary>
    /// Sort versions in descending order.
    /// </summary>
    void SortVersions()
    {
        cache.versions.Sort((m1, m2) => m2.release.version.CompareTo(m1.release.version));
    }

    /// <summary>
    /// Save the versions database.
    /// </summary>
    /// <returns>Wether the database was saved successfully.</returns>
    public bool Save()
    {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(dataFilePath));
            var json = JsonConvert.SerializeObject(cache, Formatting.Indented);
            File.WriteAllText(dataFilePath, json);
            Logger.LogDebug($"Saved versions cache to '{dataFilePath}'");
            return true;
        } catch (Exception e) {
            Console.Error.WriteLine("ERROR: Could not save versions database file: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// Remove all versions in the database.
    /// </summary>
    public void Clear()
    {
        cache.versions.Clear();
        cache.updated.Clear();
        Logger.LogDebug("Cleared versions cache");
    }

    /// <summary>
    /// Add a version to the database. Existing version will be overwritten.
    /// </summary>
    /// <returns>True if the version didn't exist in the cache, false if it was only updated.</returns>
    public bool Add(VersionMetadata metadata)
    {
        for (int i = 0; i < cache.versions.Count; i++) {
            if (cache.versions[i].Version == metadata.Version) {
                UpdateVersion(i, metadata);
                Logger.LogDebug($"Updated version in cache: {metadata.Version}");
                return false;
            }
        }

        cache.versions.Add(metadata);
        SortVersions();
        Logger.LogDebug($"Added version to cache: {metadata.Version}");
        return true;
    }

    /// <summary>
    /// Add multiple version to the database. Existing version will be overwritten.
    /// </summary>
    /// <param name="newVersions">Pass in an optional IList, which gets filled with the added versions that weren't in the cache.</param>
    public void Add(IEnumerable<VersionMetadata> metadatas, IList<VersionMetadata> newVersions = null)
    {
        foreach (var metadata in metadatas) {
            for (int i = 0; i < cache.versions.Count; i++) {
                if (cache.versions[i].Version == metadata.Version) {
                    UpdateVersion(i, metadata);
                    Logger.LogDebug($"Updated version in cache: {metadata.Version}");
                    goto continueOuter;
                }
            }
            cache.versions.Add(metadata);
            if (newVersions != null) newVersions.Add(metadata);
            Logger.LogDebug($"Added version to cache: {metadata.Version}");
            continueOuter:;
        }

        SortVersions();
    }

    /// <summary>
    /// Update a version, merging its platform-specific data.
    /// </summary>
    void UpdateVersion(int index, VersionMetadata with)
    {
        var existing = cache.versions[index];

        // Same release instance, nothing to update
        if (existing.release == with.release)
            return;

        if (with.baseUrl != null) {
            existing.baseUrl = with.baseUrl;
        }
        foreach (var editor in with.release.downloads) {
            existing.SetEditorDownload(editor);
        }

        cache.versions[index] = existing;
    }

    /// <summary>
    /// Get a version from the database.
    /// </summary>
    /// <remarks>
    /// If the version is incomplete, the latest version matching will be returned.
    /// </remarks>
    public VersionMetadata Find(UnityVersion version)
    {
        if (version.IsFullVersion) {
            // Do exact match
            foreach (var metadata in cache.versions) {
                if (version.MatchesVersionOrHash(metadata.Version)) {
                    return metadata;
                }
            }
            return default;
        }

        // Do fuzzy match
        foreach (var metadata in cache.versions) {
            if (version.FuzzyMatches(metadata.Version)) {
                return metadata;
            }
        }
        return default;
    }

    /// <summary>
    /// Get the time the cache was last updated.
    /// </summary>
    /// <param name="type">Release type to check for</param>
    /// <returns>The last update time or DateTime.MinValue if the cache was never updated.</returns>
    public DateTime GetLastUpdate(UnityVersion.Type type)
    {
        DateTime time;
        if (!cache.updated.TryGetValue(type, out time)) {
            return DateTime.MinValue;
        } else {
            return time;
        }
    }

    /// <summary>
    /// Set the time the cache was updated.
    /// </summary>
    public void SetLastUpdate(UnityVersion.Type type, DateTime time)
    {
        cache.updated[type] = time;
    }

    // -------- IEnumerable ------

    public IEnumerator<VersionMetadata> GetEnumerator()
    {
        return cache.versions.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return cache.versions.GetEnumerator();
    }
}

}
