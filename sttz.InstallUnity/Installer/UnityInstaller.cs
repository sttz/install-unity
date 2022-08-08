using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using static sttz.InstallUnity.UnityReleaseAPIClient;

namespace sttz.InstallUnity
{

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

    /// <summary>
    /// Client for the Unity Release API.
    /// </summary>
    public UnityReleaseAPIClient Releases { get; protected set; }

    // -------- API --------

    /// <summary>
    /// Name used to refer to this library.
    /// </summary>
    public const string PRODUCT_NAME = "install-unity";

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
        public string downloadPath;
        public IList<QueueItem> items;
    }

    /// <summary>
    /// Single package to be installed as part of a queue.
    /// </summary>
    public class QueueItem
    {
        /// <summary>
        /// Description of the item's current state.
        /// </summary>
        public enum State
        {
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
        public Download package;
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
        /// Number of retries left.
        /// </summary>
        public int retries;
        /// <summary>
        /// Enforce delay when retrying download.
        /// </summary>
        public DateTime waitUntil;

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
            LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });
        }
        Logger = CreateLogger<UnityInstaller>();
        GlobalLogger = CreateLogger("Global");

        // Initialize platform-specific classes
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)) {
            Logger.LogDebug("Loading platform integration for macOS");
            Platform = new MacPlatform();
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            Logger.LogDebug("Loading platform integration for WIndows");
            Platform = new WindowsPlatform();
        } else {
            throw new NotImplementedException("Installer does not currently support the platform: " + System.Runtime.InteropServices.RuntimeInformation.OSDescription);
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
        Releases = new UnityReleaseAPIClient();
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
        return Path.Combine(downloadPath, string.Format(Configuration.downloadSubdirectory, metadata.Version));
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
    /// <param name="platform">Name of platform to update (only used for loading hub JSON)</param>
    /// <param name="type">Undefined = update latest, others = update archive of type and higher types</param>
    /// <returns>Task returning the newly discovered versions</returns>
    public async Task<IEnumerable<VersionMetadata>> UpdateCache(Platform platform, Architecture architecture, UnityVersion.Type type = UnityVersion.Type.Undefined, CancellationToken cancellation = default)
    {
        var added = new List<VersionMetadata>();

        var req = new UnityReleaseAPIClient.RequestParams();
        req.platform = platform;
        req.architecture = architecture;

        req.stream = ReleaseStream.Tech | ReleaseStream.LTS;
        if (type == UnityVersion.Type.Beta) req.stream |= ReleaseStream.Beta;
        if (type == UnityVersion.Type.Alpha) req.stream |= ReleaseStream.Beta | ReleaseStream.Alpha;

        var lastUpdate = Versions.GetLastUpdate(type);
        var updatePeriod = DateTime.Now - lastUpdate;

        var maxAge = TimeSpan.FromDays(Configuration.latestMaxAge);
        if (updatePeriod > maxAge) updatePeriod = maxAge;

        Logger.LogDebug($"Loading the latest Unity releases from the last {updatePeriod} days");

        var releases = await Releases.LoadLatest(req, updatePeriod, cancellation);
        Logger.LogInformation($"Loaded {releases.Count()} releases from the Unity Release API");

        var metaReleases = releases.Select(r => VersionMetadata.FromRelease(r));
        Versions.Add(metaReleases, added);

        Versions.SetLastUpdate(UnityVersion.Type.Final, DateTime.Now);
        if (type == UnityVersion.Type.Beta) {
            Versions.SetLastUpdate(UnityVersion.Type.Beta, DateTime.Now);
        }
        if (type == UnityVersion.Type.Beta || type == UnityVersion.Type.Alpha) {
            Versions.SetLastUpdate(UnityVersion.Type.Alpha, DateTime.Now);
        }

        Versions.Save();

        added.Sort((m1, m2) => m2.Version.CompareTo(m1.Version));
        return added;
    }

    /// <summary>
    /// Get the default package IDs for the given version.
    /// </summary>
    /// <param name="metadata">Unity version</param>
    /// <param name="cachePlatform">Name of platform</param>
    public IEnumerable<string> GetDefaultPackages(VersionMetadata metadata, Platform platform, Architecture architecture)
    {
        var editor = metadata.GetEditorDownload(platform, architecture);
        if (editor == null) throw new ArgumentException($"No Unity version in cache for {platform}-{architecture}: {metadata.Version}");
        return editor.modules.Where(p => p.preSelected).Select(p => p.id);
    }

    /// <summary>
    /// Resolve package patterns to package metadata.
    /// This method also adds package dependencies.
    /// </summary>
    public IEnumerable<Download> ResolvePackages(
        VersionMetadata metadata, 
        Platform platform, Architecture architecture,
        IEnumerable<string> packages, 
        IList<string> notFound = null
    ) {
        var editor = metadata.GetEditorDownload(platform, architecture);
        var metas = new List<Download>();
        foreach (var pattern in packages) {
            var id = pattern;
            if (id.Equals(EditorDownload.ModuleId, StringComparison.OrdinalIgnoreCase)) {
                metas.Add(editor);
                continue;
            }

            bool fuzzy = false, addDependencies = true;
            while (id.StartsWith("~") || id.StartsWith("=")) {
                if (id.StartsWith("~")) {
                    fuzzy = true;
                    id = id.Substring(1);
                } else if (id.StartsWith("=")) {
                    addDependencies = false;
                    id = id.Substring(1);
                }
            }

            Module resolved = null;
            if (fuzzy) {
                // Contains lookup
                foreach (var package in editor.AllModules.Values) {
                    if (package.id.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0) {
                        if (resolved == null) {
                            Logger.LogDebug($"Fuzzy lookup '{pattern}' matched package '{package.id}'");
                            resolved = package;
                        } else {
                            throw new Exception($"Fuzzy package match '{pattern}' is ambiguous between '{package.id}' and '{resolved.id}'");
                        }
                    }
                }
            } else {
                // Exact lookup
                editor.AllModules.TryGetValue(id, out resolved);
            }

            if (resolved != null) {
                AddPackageWithDependencies(editor, metas, resolved, addDependencies);
            } else if (notFound != null) {
                notFound.Add(id);
            }
        }
        return metas;
    }

    /// <summary>
    /// Recursive method to add package and dependencies.
    /// </summary>
    void AddPackageWithDependencies(
        EditorDownload editor, 
        List<Download> selected, 
        Module package, 
        bool addDependencies,
        bool isDependency = false
    ) {
        if (selected.Contains(package)) return;

        package.addedAutomatically = isDependency;
        selected.Add(package);

        if (!addDependencies) return;

        foreach (var subModule in package.subModules) {
            if (selected.Contains(subModule))
                continue;

            Logger.LogInformation($"Adding '{subModule.id}' which '{package.id}' depends on");
            AddPackageWithDependencies(editor, selected, subModule, addDependencies, true);
        }
    }

    /// <summary>
    /// Get the file name to use for the package.
    /// </summary>
    public string GetFileName(Download download)
    {
        string fileName;

        // Try to get file name from URL
        var uri = new Uri(download.url, UriKind.RelativeOrAbsolute);
        if (uri.IsAbsoluteUri) {
            fileName = uri.Segments.Last();
        } else {
            fileName = Path.GetFileName(download.url);
        }

        // Fallback to type-based extension
        if (Path.GetExtension(fileName) == "" && download.type != FileType.Undefined) {
            var typeExtension = download.type switch {
                FileType.TEXT   => ".txt",
                FileType.TAR_GZ => ".tar.gz",
                FileType.TAR_XZ => ".tar.xz",
                FileType.ZIP    => ".zip",
                FileType.PKG    => ".pkg",
                FileType.EXE    => ".exe",
                FileType.PO     => ".po",
                FileType.DMG    => ".dmg",
                FileType.LZMA   => ".lzma",
                FileType.LZ4    => ".lz4",
                FileType.PDF    => ".pdf",
                _ => throw new Exception($"Unhandled download type: {download.type}")
            };

            fileName = download.Id + typeExtension;
        }

        // Force an extension for older versions that have neither extension nor type
        if (Path.GetExtension(fileName) == "") {
            fileName = download.Id + ".pkg";
        }

        return fileName;
    }

    /// <summary>
    /// Create a download and install queue from the given version and packages.
    /// </summary>
    /// <param name="metadata">The Unity version</param>
    /// <param name="platform">Name of platform</param>
    /// <param name="downloadPath">Location of the downloaded the packages</param>
    /// <param name="packageIds">Packages to download and/or install</param>
    /// <returns>The queue list with the created queue items</returns>
    public Queue CreateQueue(VersionMetadata metadata, Platform platform, Architecture architecture, string downloadPath, IEnumerable<Download> packages)
    {
        if (!metadata.Version.IsFullVersion)
            throw new ArgumentException("VersionMetadata.version needs to contain a full Unity version", nameof(metadata));
        
        var editor = metadata.GetEditorDownload(platform, architecture);

        if (editor == null || !editor.modules.Any())
            throw new ArgumentException("VersionMetadata.release cannot be null or empty", nameof(metadata));

        var items = new List<QueueItem>();
        foreach (var package in packages) {
            var fullUrl = package.url;
            if (metadata.baseUrl != null && !fullUrl.StartsWith("http")) {
                fullUrl = metadata.baseUrl + package.url;
            }

            var fileName = GetFileName(package);
            Logger.LogDebug($"{package.Id}: Using file name '{fileName}' for url '{fullUrl}'");
            var outputPath = Path.Combine(downloadPath, fileName);

            items.Add(new QueueItem() {
                package = package,
                downloadUrl = new Uri(fullUrl),
                filePath = outputPath,
                retries = Configuration.retryCount
            });
        }

        return new Queue() {
            metadata = metadata,
            downloadPath = downloadPath,
            items = items
        };
    }

    /// <summary>
    /// Process a download and install queue.
    /// </summary>
    /// <param name="steps">Which steps to perform.</param>
    /// <param name="queue">The queue to process</param>
    /// <param name="cancellation">Cancellation token</param>
    public async Task<Installation> Process(InstallStep steps, Queue queue, Downloader.ExistingFile existingFile = Downloader.ExistingFile.Undefined, CancellationToken cancellation = default)
    {
        if (queue == null) throw new ArgumentNullException(nameof(queue));

        if ((steps & InstallStep.DownloadAndInstall) == InstallStep.None)
            throw new ArgumentException("Install steps cannot be None", nameof(steps));

        var download = (steps & InstallStep.Download) > 0;
        var install = (steps & InstallStep.Install) > 0;
        Logger.LogDebug($"download = {download}, install = {install}");

        foreach (var item in queue.items) {
            var size = item.package.downloadSize.GetBytes();
            var hash = item.package.integrity;

            if (!download) {
                if (!File.Exists(item.filePath))
                    throw new InvalidOperationException($"File for package {item.package.Id} not found at path: {item.filePath}");

                if (hash == null) {
                    Logger.LogWarning($"File exists but cannot be checked for completeness: {item.filePath}");
                    item.currentState = QueueItem.State.WaitingForInstall;
                } else {
                    item.downloader = new Downloader();
                    item.downloader.Prepare(null, item.filePath, size, hash);
                    item.currentState = QueueItem.State.WaitingForDownload;
                }

            } else {
                // Some packages (Visual Studio, Mono) have wrong size and no hash
                if (hash == null && File.Exists(item.filePath)) {
                    Logger.LogWarning($"File exists but cannot be checked for completeness: {item.filePath}");
                    item.currentState = install ? QueueItem.State.WaitingForInstall : QueueItem.State.Complete;
                } else {
                    item.downloader = new Downloader();
                    if (existingFile != Downloader.ExistingFile.Undefined) {
                        item.downloader.Existing = existingFile;
                    } else {
                        item.downloader.Existing = Configuration.resumeDownloads
                            ? Downloader.ExistingFile.Resume
                            : Downloader.ExistingFile.Redownload;
                    }
                    item.downloader.Timeout = Configuration.requestTimeout;
                    item.downloader.Prepare(item.downloadUrl, item.filePath, size, hash);
                }
            }
        }

        if (install) {
            string installationPaths = null;
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)) {
                installationPaths = Configuration.installPathMac;
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                installationPaths = Configuration.installPathWindows;
            } else {
                throw new NotImplementedException("Installer does not currently support the platform: " + System.Runtime.InteropServices.RuntimeInformation.OSDescription);
            }

            await Platform.PrepareInstall(queue, installationPaths, cancellation);
        }

        try {
            var editorItem = queue.items.FirstOrDefault(i => i.package is EditorDownload);
            while (!cancellation.IsCancellationRequested) {
                // Check completed and count active
                int downloading = 0, installing = 0, complete = 0;
                foreach (var item in queue.items) {
                    if (item.currentState == QueueItem.State.Hashing || item.currentState == QueueItem.State.Downloading) {
                        if (item.downloadTask.IsCompleted) {
                            if (item.downloadTask.IsFaulted) {
                                if (!download || item.retries <= 0) {
                                    throw item.downloadTask.Exception;
                                } else {
                                    item.retries--;
                                    Logger.LogError(item.downloadTask.Exception.InnerException.Message 
                                        + $" (retrying in {Configuration.retryDelay}s, {item.retries} retries remaining)");
                                    Logger.LogInformation(item.downloadTask.Exception.InnerException.StackTrace);
                                    item.waitUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Configuration.retryDelay);
                                    item.downloader.Reset();
                                    item.currentState = QueueItem.State.WaitingForDownload;
                                }
                            } else {
                                item.currentState = install ? QueueItem.State.WaitingForInstall : QueueItem.State.Complete;
                                Logger.LogDebug($"{item.package.Id} download complete: now {item.currentState}");
                            }
                        } else {
                            if (item.currentState == QueueItem.State.Hashing && item.downloader.CurrentState == Downloader.State.Downloading) {
                                item.currentState = QueueItem.State.Downloading;
                                Logger.LogDebug($"{item.package.Id} hashed: now {item.currentState}");
                            }
                            downloading++;
                        }
                    
                    } else if (item.currentState == QueueItem.State.Installing) {
                        if (item.installTask.IsCompleted) {
                            if (item.installTask.IsFaulted) {
                                throw item.installTask.Exception;
                            }
                            item.currentState = QueueItem.State.Complete;
                            Logger.LogDebug($"{item.package.Id}: install complete");
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
                        if (item.waitUntil > DateTime.UtcNow) {
                            continue;
                        }
                        Logger.LogDebug($"{item.package.Id}: Starting download");
                        if (download) {
                            item.downloadTask = item.downloader.Start(cancellation);
                        } else {
                            item.downloadTask = item.downloader.AssertExistingFileHash(cancellation);
                        }
                        item.currentState = QueueItem.State.Hashing;
                        downloading++;
                    
                    } else if (installing < Configuration.maxConcurrentInstalls && item.currentState == QueueItem.State.WaitingForInstall) {
                        if (editorItem != null && item != editorItem && editorItem.currentState != QueueItem.State.Complete) {
                            // Wait for the editor to complete installation
                            continue;
                        }
                        Logger.LogDebug($"{item.package.Id}: Starting install");
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
            return await Platform.CompleteInstall(false, cancellation);
        } else {
            return null;
        }
    }

    /// <summary>
    /// Delete downloaded data, checking to only delete expected files.
    /// </summary>
    /// <param name="downloadPath">Where downloads were stored.</param>
    /// <param name="metadata">The Unity version downloaded</param>
    /// <param name="packageIds">Downloaded packages.</param>
    public void CleanUpDownloads(Queue queue)
    {
        if (!Directory.Exists(queue.downloadPath))
            return;
        
        foreach (var directory in Directory.GetDirectories(queue.downloadPath)) {
            throw new Exception("Unexpected directory in downloads folder: " + directory);
        }

        var packageFilePaths = queue.items
            .Select(p => Path.GetFullPath(p.filePath))
            .ToList();
        foreach (var path in Directory.GetFiles(queue.downloadPath)) {
            var fileName = Path.GetFileName(path);
            if (fileName == ".DS_Store" || fileName == "thumbs.db" || fileName == "desktop.ini")
                continue;
            
            if (!packageFileNames.Contains(path)) {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    // Don't throw on unexcpeted files in Windows Download folder
                    Logger.LogWarning("Unexpected file in downloads folder: " + path);
                } else {
                    throw new Exception("Unexpected file in downloads folder: " + path);
                }
            }
        }

        Directory.Delete(queue.downloadPath, true);
    }
}

}
