using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PowerArgs;
using sttz.ConsoleLogger;
using sttz.InstallUnity;

namespace sttz.InstallUnity
{

/// <summary>
/// Main command line program definition.
/// </summary>
public class InstallUnityProgram
{
    // -------- Global Arguments --------

    [HelpHook, ArgShortcut("?"), ArgShortcut("--help")]
    [ArgDescription("Show this help")]
    public bool Help { get; set; }

    [ArgShortcut("v"), ArgShortcut("--verbose")]
    [ArgDescription("Enable verbose mode")]
    public bool Verbose { get; set; }

    [ArgShortcut("--log-level")]
    [ArgDescription("Set the log level")]
    public string LogLevel { get; set; }

    [ArgPosition(1)]
    [ArgDescription("Pattern to select Unity version(s)")]
    public string MatchVersion { get; set; }

    [ArgShortcut("--data-path")]
    [ArgDescription("Store packages and their metadata in the given directory and don't delete them after installation")]
    public string DataPath { get; set; }

    [ArgShortcut("u"), ArgShortcut("--update")]
    [ArgDescription("Force an update of the versions cache")]
    public bool Update { get; set; }

    [ArgShortcut("--opt")]
    [ArgDescription("Set additional options (show them with '--options list')")]
    public string[] Options { get; set; }

    UnityInstaller installer;

    async Task<UnityVersion> Setup()
    {
        enableColors = Environment.GetEnvironmentVariable("CLICOLORS") != "0";

        var level = Microsoft.Extensions.Logging.LogLevel.Warning;
        if (LogLevel != null) {
            try {
                level = (LogLevel)Enum.Parse(typeof(LogLevel), LogLevel, true);
            } catch {
                throw new Exception("Invalid log level: " + LogLevel);
            }
        } else if (Verbose) {
            level = Microsoft.Extensions.Logging.LogLevel.Information;
        }

        // Create installer instance
        installer = new UnityInstaller(dataPath: DataPath, loggerFactory: new LoggerFactory().AddConsole(level, false));

        // Re-set colors based on loaded configuration
        enableColors = enableColors && installer.Configuration.enableColoredOutput;

        // Parse version argument (--unity-version or positional argument)
        var version = new UnityVersion(MatchVersion);
        if (version.type == UnityVersion.Type.Undefined) version.type = UnityVersion.Type.Final;

        // Set additional configuration (--opt NAME=VALUE)
        if (Options != null) {
            if (Options.Contains("list", StringComparer.OrdinalIgnoreCase)) {
                ListOptions(installer.Configuration);
                Environment.Exit(0);
            } else if (Options.Contains("save")) {
                var configPath = installer.DataPath ?? installer.Platform.GetConfigurationDirectory();
                configPath = Path.Combine(configPath, UnityInstaller.CONFIG_FILENAME);
                installer.Configuration.Save(configPath);
                Console.WriteLine("Configuration file saved to:");
                Console.WriteLine(configPath);
                Environment.Exit(0);
            }

            foreach (var option in Options) {
                var parts = option.Split('=');
                if (parts.Length != 2) {
                    throw new Exception($"Option needs to be in the format 'NAME=VALUE' (got '{option}')");
                }

                installer.Configuration.Set(parts[0], parts[1]);
            }
        }

        // Update cache if needed or requested (--update)
        IEnumerable<VersionMetadata> newVersions;
        if (Update || installer.IsCacheOutdated(version.type)) {
            WriteTitle("Updating Cache...");
            newVersions = await installer.UpdateCache(version.type);

            var total = newVersions.Count();
            if (total == 0) {
                Console.WriteLine("No new Unity versions");
            } else if (total == 1) {
                Console.WriteLine("New Unity version: " + newVersions.First().version);
            } else if (total > 0) {
                Console.WriteLine("New Unity versions:");
                foreach (var newVersion in newVersions.Take(5)) {
                    Console.WriteLine($"- {newVersion.version} ({installer.Scraper.GetReleaseNotesUrl(newVersion.version)})");
                }
                if (total - 5 > 0) {
                    Console.WriteLine($"And {total - 5} more...");
                }
            }
        }

        return version;
    }

    async Task<VersionMetadata> SelectAndLoad(UnityVersion version)
    {
        // Locate version in cache or look it up
        var metadata = installer.Versions.Find(version);
        if (!metadata.version.IsValid) {
            try {
                metadata = await installer.Scraper.LoadExact(version);
                installer.Versions.Add(metadata);
                installer.Versions.Save();
                Console.WriteLine($"Guessed release notes URL to discover {metadata.version}");
            } catch {
                throw new Exception("Could not find version matching input: " + version);
            }
        }

        if (metadata.version != version) {
            Console.WriteLine();
            Console.Write("Selected ");
            SetColors(ConsoleColor.White, ConsoleColor.DarkGray);
            Console.Write(metadata.version);
            ResetColor();
            Console.WriteLine($" for input {version}");
        }

        // Load packages ini if needed
        if (metadata.packages == null) {
            metadata = await installer.Scraper.LoadPackages(metadata);
            installer.Versions.Add(metadata);
            installer.Versions.Save();
        }

        return metadata;
    }

    void ListOptions(Configuration config)
    {
        WriteBigTitle("Configuration Options");

        var options = Configuration.ListOptions();
        fieldWidth = options.Max(o => o.name.Length) + 1;
        foreach (var option in options.OrderBy(o => o.name)) {
            WriteField(option.name, $"{option.description} ({option.type.Name} = {config.Get(option.name)})");
        }

        Console.WriteLine();
        Console.WriteLine("To persist options, use '--opt save' and then edit the generated config file.");
    }

    // -------- List Versions --------

    const int ListVersionsColumnWidth = 15;

    [ArgActionMethod, ArgDescription("List available Unity versions")]
    public async Task List()
    {
        var version = await Setup();
        VersionsTable(installer, version);
    }

    public void VersionsTable(UnityInstaller installer, UnityVersion version)
    {
        if (version.type == UnityVersion.Type.Undefined) {
            version.type = UnityVersion.Type.Final;
        }

        // First sort versions into rows and columns based on major and minor version
        var majorRows = new List<List<List<VersionMetadata>>>();
        var currentRow = new List<List<VersionMetadata>>();
        var currentList = new List<VersionMetadata>();
        int lastMajor = -1, lastMinor = -1;
        foreach (var metadata in installer.Versions) {
            var other = metadata.version;
            if (!version.FuzzyMatches(other)) continue;

            if (lastMinor < 0) lastMinor = other.minor;
            else if (lastMinor != other.minor) {
                lastMinor = other.minor;
                currentRow.Add(currentList);
                currentList = new List<VersionMetadata>();
            }

            if (lastMajor < 0) lastMajor = other.major;
            else if (lastMajor != other.major) {
                lastMajor = other.major;
                majorRows.Add(currentRow);
                currentRow = new List<List<VersionMetadata>>();
            }

            currentList.Add(metadata);
        }
        if (currentList.Count > 0) currentRow.Add(currentList);
        if (currentRow.Count > 0) majorRows.Add(currentRow);

        if (currentRow.Count == 0) {
            Console.WriteLine("No versions found for input version: " + version);
            return;
        }

        // Write the generated columns line by line, wrapping to buffer size
        var maxColumns = Math.Max(Console.BufferWidth / ListVersionsColumnWidth, 1);
        foreach (var majorRow in majorRows) {
            // Major version seperator / title
            var major = majorRow[0][0].version.major;
            WriteBigTitle(major.ToString());
            
            var groupCount = (majorRow.Count - 1) / maxColumns + 1;
            for (var g = 0; g < groupCount; g++) {
                var maxCount = majorRow.Skip(g * maxColumns).Take(maxColumns).Max(l => l.Count);
                for (int r = -1; r < maxCount; r++) {
                    var columnOffset = g * maxColumns;
                    for (int c = columnOffset; c < columnOffset + maxColumns && c < majorRow.Count; c++) {
                        if (r == -1) {
                            // Minor version title
                            Console.SetCursorPosition((c - columnOffset) * ListVersionsColumnWidth, Console.CursorTop);

                            SetColors(ConsoleColor.White, ConsoleColor.DarkGray);
                            var minorVersion = majorRow[c][0].version;
                            var title = minorVersion.major + "." + minorVersion.minor;
                            Console.Write(title);

                            ResetColor();
                            continue;
                        }
                        if (r >= majorRow[c].Count) continue;
                        Console.SetCursorPosition((c - columnOffset) * ListVersionsColumnWidth, Console.CursorTop);
                        Console.Write(majorRow[c][r].version);
                    }
                    Console.WriteLine();
                }
                Console.WriteLine();
            }
        }
    }

    // -------- Version Details --------

    [ArgActionMethod, ArgDescription("Show information about a Unity version")]
    public async Task Details()
    {
        var version = await Setup();
        var metadata = await SelectAndLoad(version);
        ShowDetails(installer, metadata);
    }

    void ShowDetails(UnityInstaller installer, VersionMetadata metadata)
    {
        WriteBigTitle($"Details for Unity {metadata.version}");

        if (metadata.iniUrl != null) {
            Console.WriteLine("Base URL: " + metadata.iniUrl);
        }

        var releaseNotes = installer.Scraper.GetReleaseNotesUrl(metadata.version);
        if (releaseNotes != null) {
            Console.WriteLine("Release notes: " + releaseNotes);
        }

        Console.WriteLine();

        var list = string.Join(", ", metadata.packages
            .Select(p => p.name + (p.install ? "*" : ""))
            .ToArray()
        );
        Console.WriteLine(metadata.packages.Length + " Packages: " + list);
        Console.WriteLine("* = default package");
        Console.WriteLine();

        fieldWidth = 14;
        foreach (var package in metadata.packages.OrderBy(p => p.name)) {
            SetColors(ConsoleColor.DarkGray, ConsoleColor.DarkGray);
            Console.Write("--------------- ");
            SetForeground(ConsoleColor.White);
            Console.Write(package.name + (package.install ? "* " : " "));
            ResetColor();
            Console.WriteLine();

            WriteField("Title", package.title);
            WriteField("Description", package.description);
            WriteField("URL", package.url);
            WriteField("Mandatory", (package.mandatory ? "yes" : "no"));
            WriteField("Hidden", (package.hidden ? "yes" : "no"));
            WriteField("Size", $"{Helpers.FormatSize(package.size)} ({Helpers.FormatSize(package.installedsize)} installed)");
            WriteField("Install with", package.sync);
            WriteField("MD5", package.md5);

            Console.WriteLine();
        }
    }

    // -------- Install --------

    public class InstallArguments
    {
        [ArgShortcut("p"), ArgShortcut("--packages")]
        [ArgDescription("Select packages to install ('--packages all' installs all available packages)")]
        public string[] Packages { get; set; }

        [ArgShortcut("d"), ArgShortcut("--download")]
        [ArgDescription("Only download the packages (requires '--data-path')")]
        public bool Download { get; set; }

        [ArgShortcut("i"), ArgShortcut("--install")]
        [ArgDescription("Only install the packages (requires '--data-path')")]
        public bool Install { get; set; }
    }

    [ArgActionMethod, ArgDescription("Manage Unity installations")]
    public async Task Install(InstallArguments args)
    {
        var version = await Setup();

        // Determine operation (based on --download and --install)
        var op = UnityInstaller.InstallStep.None;
        if (args.Download) op |= UnityInstaller.InstallStep.Download;
        if (args.Install)  op |= UnityInstaller.InstallStep.Install;

        if (op == UnityInstaller.InstallStep.None) {
            op = UnityInstaller.InstallStep.DownloadAndInstall;
        }

        if (op != UnityInstaller.InstallStep.DownloadAndInstall && DataPath != null) {
            throw new Exception("'--download' and '--install' require '--data-path' to be set.");
        }

        var metadata = await SelectAndLoad(version);

        // Determine packages to install (-p / --packages or defaultPackages option)
        IEnumerable<string> packages = args.Packages;
        if (packages != null && string.Equals(packages.FirstOrDefault(), "all", StringComparison.OrdinalIgnoreCase)) {
            packages = metadata.packages.Select(p => p.name);
        } else if (packages == null) {
            Console.WriteLine();
            if (installer.Configuration.defaultPackages != null) {
                Console.WriteLine("Installing configured packages (select packages with --packages, see available packages with --details)");
                packages = installer.Configuration.defaultPackages;
            } else {
                Console.WriteLine("Installing default packages (select packages with --packages, see available packages with --details)");
                packages = installer.GetDefaultPackages(metadata);
            }
        }

        var notFound = new List<string>();
        var resolved = installer.ResolvePackages(metadata, packages, notFound: notFound);
        
        WriteTitle("Selected packages:");
        long totalSpace = 0, totalDownload = 0;
        foreach (var package in resolved) {
            totalSpace += package.installedsize;
            totalDownload += package.size;
            Console.WriteLine($"- {package.name} ({Helpers.FormatSize(package.size)})");
        }

        Console.WriteLine();

        if (op == UnityInstaller.InstallStep.Download) {
            Console.WriteLine($"Will download {Helpers.FormatSize(totalDownload)}");
        } else if (op == UnityInstaller.InstallStep.Install) {
            Console.WriteLine($"Will install {Helpers.FormatSize(totalSpace)}");
        } else {
            Console.WriteLine($"Will download {Helpers.FormatSize(totalDownload)} and install {Helpers.FormatSize(totalSpace)}");
        }

        if (notFound.Count > 0) {
            Console.WriteLine();
            Console.WriteLine("WARN: Following packages were not found: " + string.Join(", ", notFound.ToArray()));
        }

        // Request password before downoad so the download & installation can go on uninterrupted
        if ((op & UnityInstaller.InstallStep.Install) > 0 && !await installer.Platform.PromptForPasswordIfNecessary()) {
            Environment.Exit(1);
        }

        // Do the magic
        var downloadPath = installer.GetDownloadDirectory(metadata);
        var queue = installer.CreateQueue(metadata, downloadPath, resolved);
        if (installer.Configuration.progressBar) {
            var processTask = installer.Process(op, queue);

            var refreshInterval = installer.Configuration.progressRefreshInterval;
            var statusInterval = installer.Configuration.statusRefreshEvery;
            var updateCount = 0L;
            while (!processTask.IsCompleted) {
                WriteQueueStatus(queue, ++updateCount % statusInterval == 0);
                await Task.Delay(refreshInterval);
            }

            if (processTask.IsFaulted) {
                throw processTask.Exception;
            }
        } else {
            await installer.Process(op, queue);
        }

        if (DataPath != null) {
            installer.CleanUpDownloads(metadata, downloadPath, resolved);
        }

        if (op == UnityInstaller.InstallStep.Download) {
            WriteTitle($"Packages downloaded to '{downloadPath}'");
        } else {
            WriteTitle($"Installation complete");
        }
    }

    static readonly char[] SubProgress = new char[] { '▏', '▎', '▍', '▌', '▋', '▊', '▉', '█' };

    void WriteQueueStatus(UnityInstaller.Queue queue, bool updateStatus)
    {
        Console.WriteLine();

        var longestName = queue.items.Max(i => i.package.name.Length);
        foreach (var item in queue.items) {
            SetColors(ConsoleColor.White, ConsoleColor.DarkGray);
            switch (item.currentState) {
                case UnityInstaller.QueueItem.State.WaitingForDownload:
                    Console.Write("IDLE");
                    break;
                case UnityInstaller.QueueItem.State.Hashing:
                    Console.Write("HASH");
                    break;
                case UnityInstaller.QueueItem.State.Downloading:
                    Console.Write("DWNL");
                    break;
                case UnityInstaller.QueueItem.State.WaitingForInstall:
                    Console.Write("WAIT");
                    break;
                case UnityInstaller.QueueItem.State.Installing:
                    Console.Write("INST");
                    break;
                case UnityInstaller.QueueItem.State.Complete:
                    Console.Write("CMPL");
                    break;
            }
            ResetColor();

            Console.Write(" ");
            Console.Write(item.package.name);
            Console.Write(new string(' ', longestName - item.package.name.Length));
            Console.Write(" ");

            var progressWidth = Console.BufferWidth - longestName - 6; // 4 for status, 2 padding
            if (item.currentState == UnityInstaller.QueueItem.State.Hashing 
                    || item.currentState == UnityInstaller.QueueItem.State.Downloading) {
                if (updateStatus || item.status == null) {
                    var bytes = item.downloader.BytesProcessed;
                    var total = item.downloader.BytesTotal;
                    var speed = (long)item.downloader.BytesPerSecond;
                    item.status = $" {Helpers.FormatSize(bytes),9} of {Helpers.FormatSize(total),9} @ {Helpers.FormatSize(speed),9}/s";
                }

                var barLength = progressWidth - item.status.Length - 3;
                var progress = (float)item.downloader.BytesProcessed / item.downloader.BytesTotal;
                var fractionalWidth = progress * barLength;
                var subIndex = (int)((fractionalWidth % 1) * SubProgress.Length);
                Console.Write("║");
                Console.Write(new string('█', (int)fractionalWidth));
                Console.Write(SubProgress[subIndex]);
                Console.Write(new string('─', barLength - (int)fractionalWidth));
                Console.Write("║");
                Console.Write(item.status);
            } else {
                Console.Write(new string(' ', progressWidth));
            }
            Console.WriteLine();
        }

        Console.SetCursorPosition(0, Console.CursorTop - queue.items.Count - 1);
    }

    // -------- Console --------

    bool enableColors;

    void WriteBigTitle(string title)
    {
        var padding = (Console.BufferWidth - title.Length - 4) / 2;
        var paddingStr = new string('—', Math.Max(padding, 0));

        Console.WriteLine();

        SetColors(ConsoleColor.Black);
        Console.Write(paddingStr);

        SetColors(ConsoleColor.White, ConsoleColor.DarkGray);
        Console.Write("  " + title + "  ");

        SetColors(ConsoleColor.Black);
        Console.Write(paddingStr);

        ResetColor();
        Console.WriteLine();
        Console.WriteLine();
    }

    void WriteTitle(string title)
    {
        Console.WriteLine();

        SetColors(ConsoleColor.White, ConsoleColor.DarkGray);
        Console.Write(title);

        ResetColor();
        Console.WriteLine();
    }

    int fieldWidth = -1;

    void WriteField(string title, string value, bool writeEmpty = false)
    {
        if (!writeEmpty && string.IsNullOrEmpty(value)) return;

        if (fieldWidth > 0) {
            var padding = fieldWidth - title.Length;
            if (padding > 0) {
                Console.Write(new string(' ', padding));
            }
        }

        SetForeground(ConsoleColor.DarkGray);
        Console.Write(title);
        Console.Write(": ");
        ResetColor();

        Console.Write(value);
        Console.WriteLine();
    }

    void SetForeground(ConsoleColor color)
    {
        if (enableColors) {
            Console.ForegroundColor = color;
        }
    }

    void SetBackground(ConsoleColor color)
    {
        if (enableColors) {
            Console.BackgroundColor = color;
        }
    }

    void SetColors(ConsoleColor fg, ConsoleColor? bg = null)
    {
        if (enableColors) {
            Console.ForegroundColor = fg;
            Console.BackgroundColor = bg ?? fg;
        }
    }

    void ResetColor()
    {
        if (enableColors) {
            Console.ResetColor();
        }
    }
}

class Program
{
    static async Task<int> Main(string[] args)
    {
        try {
            await Args.InvokeActionAsync<InstallUnityProgram>(args);
            return 0;
        } catch (ArgException e) {
            e.Message.ToRed().WriteLine();
            ArgUsage.GenerateUsageFromTemplate(Args.GetAmbientDefinition()).Write();
            return 1;
        } catch (Exception e) {
            WriteException(e, true);
            return 2;
        }
    }

    static void WriteException(Exception e, bool stackTrace = false)
    {
        var agg = e as AggregateException;
        if (agg != null) {
            if (agg.InnerExceptions.Count == 1) {
                WriteException(e.InnerException, true);
            } else {
                WriteException(e, false);
                foreach (var inner in agg.InnerExceptions) {
                    WriteException(inner, true);
                }
            }

        } else {
            e.Message.ToRed().WriteLine();
            if (stackTrace) {
                e.StackTrace.ToGray().WriteLine();
            }
        }
    }
}

}
