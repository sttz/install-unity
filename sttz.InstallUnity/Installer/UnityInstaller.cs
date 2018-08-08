using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace sttz.InstallUnity
{

// TODO
// - Offline installation flow

/// <summary>
/// Main Unity installer class.
/// </summary>
public class UnityInstaller
{
    // -------- Logging --------

    /// <summary>
    /// Logger factory used by Unity Installer.
    /// </summary>
    public static ILoggerFactory LoggerFactory { get; set; }

    /// <summary>
    /// Create a new logger based on a type.
    /// </summary>
    public static ILogger CreateLogger<T>()
    {
        return LoggerFactory.CreateLogger<T>();
    }

    /// <summary>
    /// Create a new logger with a custom category name.
    /// </summary>
    public static ILogger CreateLogger(string categoryName)
    {
        return LoggerFactory.CreateLogger(categoryName);
    }

    /// <summary>
    /// Generic ILogger instance. 
    /// </summary>
    public static ILogger GlobalLogger { get; set; }

    // -------- Components --------

    /// <summary>
    /// The generic installer configuration.
    /// </summary>
    public Configuration Configuration { get; protected set; }

    /// <summary>
    /// Manage existing installations. installer.Platform.PrepareInstall()
    /// </summary>
    public IInstallerPlatform Platform { get; protected set; }

    /// <summary>
    /// Database of known Unity versions.
    /// </summary>
    public VersionsCache Versions { get; protected set; }

    /// <summary>
    /// Discover available Unity versions.
    /// </summary>
    public Scraper Scraper { get; protected set; }

    // -------- API --------

    /// <summary>
    /// Name used to refer to this library.
    /// </summary>
    public const string PRODUCT_NAME = "InstallUnity";

    /// <summary>
    /// Name of the configuration JSON file.
    /// </summary>
    public const string CONFIG_FILENAME = "config.json";

    /// <summary>
    /// Name of the cache JSON file.
    /// </summary>
    public const string CACHE_FILENAME = "cache.json";

    /// <summary>
    /// Overrides the default locations where data is stored.
    /// </summary>
    public string DataPath { get; protected set; }

    /// <summary>
    /// Enum describing steps of the installation.
    /// </summary>
    [Flags]
    public enum InstallStep
    {
        None,
        Download = 1<<0,
        Install  = 1<<1,
        DownloadAndInstall = Download | Install
    }

    /// <summary>
    /// Queue describing what should be installed.
    /// </summary>
    public class Queue
    {
        public VersionMetadata metadata;
        public IList<QueueItem> items;
    }

    /// <summary>
    /// Single package to be installed as part of a queue.
    /// </summary>
    public class QueueItem
    {
        /// <summary>
        /// Description of the item's curent state.
        /// </summary>
        public enum State {
            /// <summary>
            /// Waiting for the download to start.
            /// </summary>
            WaitingForDownload,
            /// <summary>
            /// File or partial file exists and is being hashed.
            /// </summary>
            Hashing,
            /// <summary>
            /// File is downloading.
            /// </summary>
            Downloading,
            /// <summary>
            /// File exists or has been downloaded and is waiting to be installed.
            /// </summary>
            WaitingForInstall,
            /// <summary>
            /// File is being installed.
            /// </summary>
            Installing,
            /// <summary>
            /// Item has completed.
            /// </summary>
            Complete
        }

        /// <summary>
        /// The package metadata of this item.
        /// </summary>
        public PackageMetadata package;
        /// <summary>
        /// The item's current state.
        /// </summary>
        public State currentState;
        /// <summary>
        /// The URI to download the file from.
        /// </summary>
        public Uri downloadUrl;
        /// <summary>
        /// The path where the file will be or is stored.
        /// </summary>
        public string filePath;
        /// <summary>
        /// The Downloader instance handling the download.
        /// </summary>
        public Downloader downloader;

        /// <summary>
        /// When downloading, the task of the download.
        /// </summary>
        public Task downloadTask;
        /// <summary>
        /// When installing, the task of the install.
        /// </summary>
        public Task installTask;

        /// <summary>
        /// Used to cache a status line when downloading.
        /// </summary>
        public string status;
    }

    static HttpClient client = new HttpClient();

    ILogger Logger;

    /// <summary>
    /// Create a new installer.
    /// </summary>
    public UnityInstaller(Configuration config = null, string dataPath = null, ILoggerFactory loggerFactory = null)
    {
        // Initializer logging
        if (loggerFactory != null) {
            LoggerFactory = loggerFactory;
        } else {
            LoggerFactory = new LoggerFactory()
                .AddConsole(LogLevel.Information);
        }
        Logger = CreateLogger<UnityInstaller>();
        GlobalLogger = CreateLogger("Global");

        // Initialize platform-specific classes
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            Logger.LogDebug("Loading plaform integration for macOS");
            Platform = new MacPlatform();
        } else {
            throw new NotImplementedException("Installer does not currently support the platform: " + RuntimeInformation.OSDescription);
        }

        DataPath = dataPath;
        if (DataPath != null) {
            Logger.LogInformation("Data path set to: " + DataPath);
            if (!Directory.Exists(DataPath)) {
                Directory.CreateDirectory(DataPath);
            }
        }

        // Initialize configuration (from argument, from default location or default config)
        Configuration = config;
        if (Configuration == null) {
            var configPath = GetConfigFilePath();
            if (File.Exists(configPath)) {
                Configuration = Configuration.Load(configPath);
                Logger.LogInformation($"Loaded configuration from '{configPath}'");
            }
            if (Configuration == null) {
                Configuration = new Configuration();
                Logger.LogInformation("Use default configuration");
            }
        }

        // Initialize components
        Versions = new VersionsCache(GetCacheFilePath());
        Scraper = new Scraper();
    }

    /// <summary>
    /// Get the path to the config file (determined by DataPath or platform).
    /// </summary>
    public string GetConfigFilePath()
    {
        var configPath = DataPath ?? Platform.GetConfigurationDirectory();
        return Path.Combine(configPath, CONFIG_FILENAME);
    }

    /// <summary>
    /// Get the path to the cache file (determined by DataPath or platform).
    /// </summary>
    public string GetCacheFilePath()
    {
        var cachePath = DataPath ?? Platform.GetCacheDirectory();
        return Path.Combine(cachePath, CACHE_FILENAME);
    }

    /// <summary>
    /// Get the path to the download directory (determined by DataPath or platform).
    /// </summary>
    public string GetDownloadDirectory(VersionMetadata metadata)
    {
        var downloadPath = DataPath ?? Platform.GetDownloadDirectory();
        return Path.Combine(downloadPath, string.Format(Configuration.downloadSubdirectory, metadata.version));
    }

    /// <summary>
    /// Update the versions cache if it's outdated.
    /// </summary>
    /// <param name="type">Undefined = update latest, others = update archive of type and higher types</param>
    /// <returns>Task returning the newly discovered versions</returns>
    public bool IsCacheOutdated(UnityVersion.Type type = UnityVersion.Type.Undefined, CancellationToken cancellation = default)
    {
        var lastUpdate = Versions.GetLastUpdate(type);
        var time = DateTime.Now - lastUpdate;
        Logger.LogDebug($"Cache was last updated at {lastUpdate} or {time} ago for type {type}");
        return time > TimeSpan.FromSeconds(Configuration.cacheLifetime);
    }

    /// <summary>
    /// Update the Unity versions cache.
    /// </summary>
    /// <param name="type">Undefined = update latest, others = update archive of type and higher types</param>
    /// <returns>Task returning the newly discovered versions</returns>
    public async Task<IEnumerable<VersionMetadata>> UpdateCache(UnityVersion.Type type = UnityVersion.Type.Undefined, CancellationToken cancellation = default)
    {
        var added = new List<VersionMetadata>();
        if (type == UnityVersion.Type.Undefined) {
            Logger.LogDebug("Loading UnityHub JSON with latest Unity versions...");
            var newVersions = await Scraper.LoadLatest(cancellation);
            Logger.LogInformation($"Loaded {newVersions.Count()} versions from UnityHub JSON");
            
            Versions.Add(newVersions, added);
            Versions.SetLastUpdate(type, DateTime.Now);
        } else {
            foreach (var t in UnityVersion.EnumerateMoreStableTypes(type)) {
                if (t != UnityVersion.Type.Final && t != UnityVersion.Type.Beta)
                    continue;

                Logger.LogDebug($"Updating {type} Unity Versions...");
                var newVersions = await Scraper.Load(t, cancellation);
                Logger.LogInformation($"Scraped {newVersions.Count()} versions of type {type}");

                Versions.Add(newVersions, added);
                Versions.SetLastUpdate(type, DateTime.Now);
            }
        }
        Versions.Save();

        added.Sort((m1, m2) => m2.version.CompareTo(m1.version));
        return added;
    }

    /// <summary>
    /// Get the default package IDs for the given version.
    /// </summary>
    /// <param name="metadata">Unity version</param>
    public IEnumerable<string> GetDefaultPackages(VersionMetadata metadata)
    {
        if (metadata.packages == null) throw new ArgumentException("Unity version contains no packages.");
        return metadata.packages.Where(p => p.install).Select(p => p.name);
    }

    /// <summary>
    /// Resolve package patterns to package metadata.
    /// This method also adds package dependencies.
    /// </summary>
    public IEnumerable<PackageMetadata> ResolvePackages(
        VersionMetadata metadata, 
        IEnumerable<string> packages, 
        IList<string> notFound = null,
        bool addDependencies = true
    ) {
        var metas = new List<PackageMetadata>();
        foreach (var id in packages) {
            PackageMetadata resolved = default;
            if (id.StartsWith('~')) {
                // Contains lookup
                var str = id.Substring(1);
                foreach (var package in metadata.packages) {
                    if (package.name.Contains(str, StringComparison.OrdinalIgnoreCase)) {
                        Logger.LogDebug($"Fuzzy lookup '{id}' matched package '{package.name}'");
                        resolved = package;
                        break;
                    }
                }
            } else {
                // Exact lookup
                resolved = metadata.GetPackage(id);
            }

            if (resolved.name != null) {
                if (addDependencies) {
                    foreach (var package in metadata.packages) {
                        if (package.sync == resolved.name) {
                            Logger.LogInformation($"Adding '{package.name}' which '{resolved.name}' is synced with");
                            metas.Add(package);
                        }
                    }
                }

                metas.Add(resolved);

            } else if (notFound != null) {
                notFound.Add(id);
            }
        }
        return metas;
    }

    /// <summary>
    /// Create a download and install queue from the given version and packages.
    /// </summary>
    /// <param name="metadata">The Unity version</param>
    /// <param name="downloadPath">Location of the downloaded the packages</param>
    /// <param name="packageIds">Packages to download and/or install</param>
    /// <returns>The queue list with the created queue items</returns>
    public Queue CreateQueue(VersionMetadata metadata, string downloadPath, IEnumerable<PackageMetadata> packages)
    {
        if (!metadata.version.IsFullVersion)
            throw new ArgumentException("VersionMetadata.version needs to contain a full Unity version", nameof(metadata));
        
        if (metadata.packages == null || metadata.packages.Length == 0)
            throw new ArgumentException("VersionMetadata.packages cannot be null or empty", nameof(metadata));
        
        var items = new List<QueueItem>();
        foreach (var package in packages) {
            var fullUrl = package.url;
            if (metadata.iniUrl != null && !fullUrl.StartsWith("http")) {
                fullUrl = metadata.iniUrl + package.url;
            }

            var fileName = package.GetFileName();
            Logger.LogDebug($"{package.name}: Using file name '{fileName}' for url '{fullUrl}'");
            var outputPath = Path.Combine(downloadPath, fileName);

            items.Add(new QueueItem() {
                package = package,
                downloadUrl = new Uri(fullUrl),
                filePath = outputPath,
            });
        }

        return new Queue() {
            metadata = metadata,
            items = items
        };
    }

    /// <summary>
    /// Process a download and install queue.
    /// </summary>
    /// <param name="steps">Which steps to perform.</param>
    /// <param name="queue">The queue to process</param>
    /// <param name="cancellation">Cancellation token</param>
    public async Task Process(InstallStep steps, Queue queue, CancellationToken cancellation = default)
    {
        if (queue == null) throw new ArgumentNullException(nameof(queue));

        if ((steps & InstallStep.DownloadAndInstall) == InstallStep.None)
            throw new ArgumentException("Install steps cannot be None", nameof(steps));

        var download = (steps & InstallStep.Download) > 0;
        var install = (steps & InstallStep.Install) > 0;
        Logger.LogDebug($"download = {download}, install = {install}");

        foreach (var item in queue.items) {
            if (!download) {
                if (!File.Exists(item.filePath))
                    throw new InvalidOperationException($"File for package {item.package.name} not found at path: {item.filePath}");

                item.currentState = QueueItem.State.WaitingForInstall;

            } else {
                // Some packages (Vistual Studio, Mono) have wrong size and no hash
                if (item.package.md5 == null && File.Exists(item.filePath)) {
                    Logger.LogWarning($"File exists but cannot be checked for completeness: {item.filePath}");
                    item.currentState = install ? QueueItem.State.WaitingForInstall : QueueItem.State.Complete;
                } else {
                    item.downloader = new Downloader();
                    item.downloader.Resume = Configuration.resumeDownloads;
                    item.downloader.Timeout = Configuration.requestTimeout;
                    item.downloader.Prepare(item.downloadUrl, item.filePath, item.package.size, item.package.md5);
                }
            }
        }

        if (install) {
            string installationPaths = null;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                installationPaths = Configuration.installPathMac;
            } else {
                throw new NotImplementedException("Installer does not currently support the platform: " + RuntimeInformation.OSDescription);
            }

            await Platform.PrepareInstall(queue, installationPaths, cancellation);
        }

        try {
            var editorItem = queue.items.FirstOrDefault(i => i.package.name == PackageMetadata.EDITOR_PACKAGE_NAME);
            while (!cancellation.IsCancellationRequested) {
                // Check completed and count active
                int downloading = 0, installing = 0, complete = 0;
                foreach (var item in queue.items) {
                    if (item.currentState == QueueItem.State.Hashing || item.currentState == QueueItem.State.Downloading) {
                        if (item.downloadTask.IsCompleted) {
                            if (item.downloadTask.IsFaulted) {
                                throw item.downloadTask.Exception;
                            }
                            item.currentState = install ? QueueItem.State.WaitingForInstall : QueueItem.State.Complete;
                            Logger.LogDebug($"{item.package.name} download complete: now {item.currentState}");
                        } else {
                            if (item.currentState == QueueItem.State.Hashing && item.downloader.CurrentState == Downloader.State.Downloading) {
                                item.currentState = QueueItem.State.Downloading;
                                Logger.LogDebug($"{item.package.name} hashed: now {item.currentState}");
                            }
                            downloading++;
                        }
                    } else if (item.currentState == QueueItem.State.Installing) {
                        if (item.installTask.IsCompleted) {
                            if (item.installTask.IsFaulted) {
                                throw item.installTask.Exception;
                            }
                            item.currentState = QueueItem.State.Complete;
                            Logger.LogDebug($"{item.package.name}: install complete");
                        } else {
                            installing++;
                        }
                    }

                    if (item.currentState == QueueItem.State.Complete)
                        complete++;
                }

                if (complete == queue.items.Count) {
                    Logger.LogDebug($"All packages processed");
                    break;
                }

                // Start new items
                foreach (var item in queue.items) {
                    if (downloading < Configuration.maxConcurrentDownloads && item.currentState == QueueItem.State.WaitingForDownload) {
                        Logger.LogDebug($"{item.package.name}: Starting download");
                        item.downloadTask = item.downloader.Start(cancellation);
                        item.currentState = QueueItem.State.Hashing;
                        downloading++;
                    } else if (installing < Configuration.maxConcurrentInstalls && item.currentState == QueueItem.State.WaitingForInstall) {
                        if (editorItem != null && item != editorItem && editorItem.currentState != QueueItem.State.Complete) {
                            // Wait for the editor to complete installation
                            continue;
                        }
                        Logger.LogDebug($"{item.package.name}: Starting install");
                        item.installTask = Platform.Install(queue, item, cancellation);
                        item.currentState = QueueItem.State.Installing;
                        installing++;
                    }
                }

                await Task.Delay(100);
            }
        } catch {
            if (install) {
                Logger.LogInformation("Cleaning up aborted installation");
                await Platform.CompleteInstall(true, cancellation);
            }
            throw;
        }

        if (install) {
            await Platform.CompleteInstall(false, cancellation);
        }
    }

    /// <summary>
    /// Delete downloaded data, checking to only delte expected files.
    /// </summary>
    /// <param name="downloadPath">Where downloads were stored.</param>
    /// <param name="metadata">The Unity version downloaded</param>
    /// <param name="packageIds">Downloaded packages.</param>
    public void CleanUpDownloads(VersionMetadata metadata, string downloadPath, IEnumerable<PackageMetadata> packages)
    {
        if (!Directory.Exists(downloadPath))
            return;
        
        foreach (var directory in Directory.GetDirectories(downloadPath)) {
            throw new Exception("Unexpected directory in downloads folder: " + directory);
        }

        var packageFileNames = packages
            .Select(p => p.GetFileName())
            .ToList();
        foreach (var path in Directory.GetFiles(downloadPath)) {
            var fileName = Path.GetFileName(path);
            if (fileName == ".DS_Store" || fileName == "thumbs.db" || fileName == "desktop.ini")
                continue;
            
            if (!packageFileNames.Contains(fileName)) {
                throw new Exception("Unexpected file in downloads folder: " + path);
            }
        }

        Directory.Delete(downloadPath, true);
    }
}

}