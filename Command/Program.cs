using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using sttz.NiceConsoleLogger;

namespace sttz.InstallUnity
{

/// <summary>
/// Command line interface to the sttz.InstallUnity library.
/// </summary>
public class InstallUnityCLI
{
    /// <summary>
    /// The program command to execute.
    /// </summary>
    public string action;

    // -------- Options --------

    /// <summary>
    /// Wether to show the help.
    /// </summary>
    public bool help;
    /// <summary>
    /// Wether to print the proram version.
    /// </summary>
    public bool version;
    /// <summary>
    /// Verbosity of the output (0 = default, 1 = verbose, 3 = extra verbose)
    /// </summary>
    public int verbose;
    /// <summary>
    /// Wether to force an update of the versions cache.
    /// </summary>
    public bool update;
    /// <summary>
    /// Path to store all data at.
    /// </summary>
    public string dataPath;
    /// <summary>
    /// Set generic program options.
    /// </summary>
    public List<string> options = new List<string>();

    /// <summary>
    /// Version expression, used for most commands.
    /// </summary>
    public string matchVersion;

    // -- List

    /// <summary>
    /// Wether to list installed versions for list command.
    /// </summary>
    public bool installed;

    // -- Install

    /// <summary>
    /// Packages to install for install command.
    /// </summary>
    public List<string> packages = new List<string>();
    /// <summary>
    /// Wether to only download packages with install command.
    /// </summary>
    public bool download;
    /// <summary>
    /// Wether to only install packages with install command.
    /// </summary>
    public bool install;

    // -- Uninstall

    /// <summary>
    /// Don't prompt for uninstall command.
    /// </summary>
    public bool yes;

    // -------- Arguments Defintion --------

    /// <summary>
    /// Convert the program back into a normalized arguments string.
    /// </summary>
    public override string ToString()
    {
        var cmd = action ?? "";
        if (help) cmd += " --help";
        if (verbose > 0) cmd += string.Concat(Enumerable.Repeat(" --verbose", verbose));
        if (update) cmd += " --update";
        if (dataPath != null) cmd += " --data-path " + dataPath;
        if (options.Count > 0) cmd += " --opt " + string.Join(" ", options);
        
        if (matchVersion != null) cmd += " " + matchVersion;

        if (installed) cmd += " --installed";

        if (packages.Count > 0) cmd += " --packages " + string.Join(" ", packages);
        if (download) cmd += " --download";
        if (install) cmd += " --install";

        return cmd;
    }

    /// <summary>
    /// Name of program used in output.
    /// </summary>
    public const string PROGRAM_NAME = "install-unity";

    /// <summary>
    /// The definition of the program's arguments.
    /// </summary>
    public static Arguments<InstallUnityCLI> ArgumentsDefinition {
        get {
            if (_arguments != null) return _arguments;

            _arguments = new Arguments<InstallUnityCLI>()
                .Option((InstallUnityCLI t, bool v) => t.help = v, "h", "?", "help")
                    .Description("Show this help")
                .Option((InstallUnityCLI t, bool v) => t.version = v, "version")
                    .Description("Print the version of this program")
                .Option((InstallUnityCLI t, bool v) => t.verbose++, "v", "verbose").Repeatable()
                    .Description("Increase verbosity of output, can be repeated")
                .Option((InstallUnityCLI t, bool v) => t.update = v, "u", "update")
                    .Description("Force an update of the versions cache")
                .Option((InstallUnityCLI t, string v) => t.dataPath = v, "d", "data-path", "datapath")
                    .ArgumentName("<path>")
                    .Description("Store all data at the given path, also don't delete packages after install")
                .Option((InstallUnityCLI t, IList<string> v) => t.options.AddRange(v), "opt").Repeatable()
                    .ArgumentName("<name>=<value>")
                    .Description("Set additional options. Use 'list' to show all options and their default value"
                    + " and 'save' to create an editable JSON config file.")

                .Action("list")
                    .Description("List Unity versions available to install or already installed")
                
                .Option((InstallUnityCLI t, string v) => t.matchVersion = v, 0)
                    .ArgumentName("<version>")
                    .Description("Pattern to match Unity version")
                .Option((InstallUnityCLI t, bool v) => t.installed = v, "i", "installed")
                    .Description("List installed versions of Unity")
                
                .Action("details")
                    .Description("Show details about a Unity version and its packages")
                
                .Option((InstallUnityCLI t, string v) => t.matchVersion = v, 0)
                    .ArgumentName("<version>")
                    .Description("Pattern to match Unity version")
                
                .Action("install")
                    .Description("Install a version of Unity")
                
                .Option((InstallUnityCLI t, string v) => t.matchVersion = v, 0)
                    .ArgumentName("<version>")
                    .Description("Pattern to match Unity version")
                .Option((InstallUnityCLI t, IList<string> v) => t.packages.AddRange(v), "p", "packages").Repeatable()
                    .ArgumentName("<name,name>")
                    .Description("Select pacakges to download and install ('all' selects all available, '~NAME' matches substrings)")
                .Option((InstallUnityCLI t, bool v) => t.download = v, "download")
                    .Description("Only download the packages (requires '--data-path')")
                .Option((InstallUnityCLI t, bool v) => t.install = v, "install")
                    .Description("Install previously downloaded packages (requires '--data-path')")
                
                .Action("uninstall")
                    .Description("Uninstall a version of Unity")
                
                .Option((InstallUnityCLI t, string v) => t.matchVersion = v, 0)
                    .ArgumentName("<versionORpath>")
                    .Description("Pattern to match Unity version or path to installation root")
                .Option((InstallUnityCLI t, bool v) => t.yes = v, "y", "yes")
                    .Description("Don't prompt for confirmation before uninstall (use with care)");
                
                return _arguments;
        }
    }
    static Arguments<InstallUnityCLI> _arguments;

    /// <summary>
    /// Parses the arguments and returns the program with the fields set.
    /// </summary>
    public static InstallUnityCLI Parse(string[] args)
    {
        var parsed = new InstallUnityCLI();
        parsed.action = ArgumentsDefinition.Parse(parsed, args);
        return parsed;
    }

    /// <summary>
    /// Print the help for this program.
    /// </summary>
    public void PrintHelp()
    {
        Console.WriteLine(ArgumentsDefinition.Help(PROGRAM_NAME, null, null));
    }

    /// <summary>
    /// Return the version of this program.
    /// </summary>
    public Version GetVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
    }

    /// <summary>
    /// Print the program name and version to the console.
    /// </summary>
    public void PrintVersion()
    {
        Console.WriteLine($"{PROGRAM_NAME} v{GetVersion()}");
    }

    // -------- Global --------

    UnityInstaller installer;
    ILogger Logger;

    public async Task<UnityVersion> Setup()
    {
        enableColors = Environment.GetEnvironmentVariable("CLICOLORS") != "0";

        var level = LogLevel.Warning;
        if (verbose >= 3) {
            level = LogLevel.Trace;
        } else if (verbose == 2) {
            level = LogLevel.Debug;
        } else if (verbose == 1) {
            level = LogLevel.Information;
        }

        var factory = new LoggerFactory()
            .AddNiceConsole(level, false);
        Logger = factory.CreateLogger<InstallUnityCLI>();

        Logger.LogInformation($"{PROGRAM_NAME} v{GetVersion()}");
        if (level != LogLevel.Warning) Logger.LogInformation($"Log level set to {level}");

        // Create installer instance
        installer = new UnityInstaller(dataPath: dataPath, loggerFactory: factory);

        // Re-set colors based on loaded configuration
        enableColors = enableColors && installer.Configuration.enableColoredOutput;
        if (!enableColors) Logger.LogInformation("Console colors disabled");

        // Parse version argument (--unity-version or positional argument)
        var version = new UnityVersion(matchVersion);
        if (version.type == UnityVersion.Type.Undefined) version.type = UnityVersion.Type.Final;

        // Set additional configuration (--opt NAME=VALUE)
        if (options != null) {
            if (options.Contains("list", StringComparer.OrdinalIgnoreCase)) {
                ListOptions(installer.Configuration);
                Environment.Exit(0);
            } else if (options.Contains("save")) {
                var configPath = installer.DataPath ?? installer.Platform.GetConfigurationDirectory();
                configPath = Path.Combine(configPath, UnityInstaller.CONFIG_FILENAME);
                if (File.Exists(configPath)) {
                    Console.WriteLine($"Configuration file already exists:\n{configPath}");
                } else {
                    installer.Configuration.Save(configPath);
                    Console.WriteLine($"Configuration file saved to:\n{configPath}");
                }
                Environment.Exit(0);
            }

            foreach (var option in options) {
                var parts = option.Split('=');
                if (parts.Length != 2) {
                    throw new Exception($"Option needs to be in the format 'NAME=VALUE' (got '{option}')");
                }

                installer.Configuration.Set(parts[0], parts[1]);
            }
        }

        // Update cache if needed or requested (--update)
        IEnumerable<VersionMetadata> newVersions;
        if (update || installer.IsCacheOutdated(version.type)) {
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
                Logger.LogInformation("Version {version} not found in cache, trying exact lookup");
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
            ConsoleLogger.WriteLine($"Selected <white bg=darkgray>{metadata.version}</white> for input {version}");
        }

        // Load packages ini if needed
        if (metadata.packages == null) {
            Logger.LogInformation("Packages not yet loaded, loading ini now");
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
   const int ListVersionsWithHashColumnWith = 30;

    public async Task List()
    {
        var version = await Setup();

        if (installed) {
            var installs = await installer.Platform.FindInstallations();
            // Re-parse given version to get default undefined for type
            InstalledList(installer, new UnityVersion(matchVersion), installs);
        } else {
            VersionsTable(installer, version);
        }
    }

    public void InstalledList(UnityInstaller installer, UnityVersion version, IEnumerable<Installation> installations)
    {
        foreach (var install in installations) {
            if (!version.FuzzyMatches(install.version)) continue;
            Console.WriteLine($"{install.version}\t{install.path}");
        }
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
        var colWidth = (verbose > 0 ? ListVersionsWithHashColumnWith : ListVersionsColumnWidth);
        var maxColumns = Math.Max(Console.BufferWidth / colWidth, 1);
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
                            Console.SetCursorPosition((c - columnOffset) * colWidth, Console.CursorTop);

                            SetColors(ConsoleColor.White, ConsoleColor.DarkGray);
                            var minorVersion = majorRow[c][0].version;
                            var title = minorVersion.major + "." + minorVersion.minor;
                            Console.Write(title);

                            ResetColor();
                            continue;
                        }
                        if (r >= majorRow[c].Count) continue;
                        Console.SetCursorPosition((c - columnOffset) * colWidth, Console.CursorTop);
                        Console.Write(majorRow[c][r].version.ToString(verbose > 0));
                    }
                    Console.WriteLine();
                }
                Console.WriteLine();
            }
        }
    }

    // -------- Version Details --------

    public async Task Details()
    {
        var version = await Setup();
        var metadata = await SelectAndLoad(version);
        ShowDetails(installer, metadata);
    }

    void PackagesList(VersionMetadata metadata)
    {
        var list = string.Join(", ", metadata.packages
            .Select(p => p.name + (p.install ? "*" : ""))
            .ToArray()
        );
        Console.WriteLine(metadata.packages.Length + " Packages: " + list);
        Console.WriteLine("* = default package");
        Console.WriteLine();
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

        PackagesList(metadata);

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

    public async Task Install()
    {
        var version = await Setup();

        // Determine operation (based on --download and --install)
        var op = UnityInstaller.InstallStep.None;
        if (download) op |= UnityInstaller.InstallStep.Download;
        if (install)  op |= UnityInstaller.InstallStep.Install;

        if (op == UnityInstaller.InstallStep.None) {
            op = UnityInstaller.InstallStep.DownloadAndInstall;
        }

        Logger.LogInformation($"Install steps: {op}");

        if (op != UnityInstaller.InstallStep.DownloadAndInstall && dataPath != null) {
            throw new Exception("'--download' and '--install' require '--data-path' to be set.");
        }

        var metadata = await SelectAndLoad(version);

        // Determine packages to install (-p / --packages or defaultPackages option)
        IEnumerable<string> selection = packages;
        if (string.Equals(selection.FirstOrDefault(), "all", StringComparison.OrdinalIgnoreCase)) {
            Logger.LogInformation("Found 'all', selecting all available packages");
            selection = metadata.packages.Select(p => p.name);
        } else if (!selection.Any()) {
            Console.WriteLine();
            if (installer.Configuration.defaultPackages != null) {
                Console.WriteLine("Installing configured packages (select packages with '--packages', see available packages with 'details')");
                selection = installer.Configuration.defaultPackages;
            } else {
                Console.WriteLine("Installing default packages (select packages with '--packages', see available packages with 'details')");
                selection = installer.GetDefaultPackages(metadata);
            }
        }

        var notFound = new List<string>();
        var resolved = installer.ResolvePackages(metadata, selection, notFound: notFound);

        // Check version to be installed against installed
        var freshInstall = resolved.Any(p => p.name == PackageMetadata.EDITOR_PACKAGE_NAME);
        var installs = await installer.Platform.FindInstallations();
        var existing = installs.FirstOrDefault(i => i.version == metadata.version);
        if (!freshInstall && existing == null) {
            throw new Exception($"Installing additional packages but Unity {metadata.version} hasn't been installed yet (add the 'Unity' package to install it).");
        } else if (freshInstall && existing != null) {
            throw new Exception($"Unity {metadata.version} already installed at '{existing.path}' (remove the 'Unity' package to install additional packages).");
        }

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
            Logger.LogInformation("Failed password prompt too many times");
            Environment.Exit(1);
        }

        // Do the magic
        var downloadPath = installer.GetDownloadDirectory(metadata);
        Logger.LogInformation($"Downloading packages to '{downloadPath}'");

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
            Logger.LogInformation("Progress bar is disabled");
            await installer.Process(op, queue);
        }

        if (dataPath != null) {
            Logger.LogInformation("Cleaning up downloaded pacakges ('--data-path' not set)");
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

    // -------- Uninstall --------

    public async Task Uninstall()
    {
        await Setup();

        var installs = await installer.Platform.FindInstallations();
        if (!installs.Any()) {
            throw new Exception("Could not find any installed versions of Unity");
        }

        Installation uninstall = null;
        var version = new UnityVersion(matchVersion);
        if (!version.IsValid) {
            var fullPath = Path.GetFullPath(matchVersion);
            foreach (var install in installs) {
                var fullInstallPath = Path.GetFullPath(install.path);
                if (fullPath == fullInstallPath) {
                    uninstall = install;
                    break;
                }
            }
        } else {
            foreach (var install in installs) {
                if (version.FuzzyMatches(install.version)) {
                    if (uninstall != null) {
                        throw new Exception($"Version {version} is ambiguous between\n"
                            + $"{uninstall.version} at '{uninstall.path}' and\n"
                            + $"{install.version} at '{install.path}'\n"
                            + "(use exact version or path instead).");
                    }
                    uninstall = install;
                }
            }
        }

        if (uninstall == null) {
            throw new Exception("No matching version found to uninstall.");
        }

        if (!yes) {
            while (true) {
                Console.WriteLine();
                Console.Write($"Uninstall Unity {uninstall.version} at '{uninstall.path}'? [yN]: ");
                var key = Console.ReadKey();
                Console.WriteLine();
                if (key.Key == ConsoleKey.N || key.Key == ConsoleKey.Enter) {
                    Environment.Exit(1);
                } else if (key.Key == ConsoleKey.Y) {
                    break;
                }
            }
        }

        await installer.Platform.Uninstall(uninstall);
        Console.WriteLine($"Uninstalled Unity {uninstall.version} at path: {uninstall.path}");
    }

    // -------- Console --------

    public static bool enableColors;

    public static void WriteBigTitle(string title)
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

    public static void WriteTitle(string title)
    {
        Console.WriteLine();

        SetColors(ConsoleColor.White, ConsoleColor.DarkGray);
        Console.Write(title);

        ResetColor();
        Console.WriteLine();
    }

    public static int fieldWidth = -1;

    public static void WriteField(string title, string value, bool writeEmpty = false)
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

    public static void SetForeground(ConsoleColor color)
    {
        if (enableColors) {
            Console.ForegroundColor = color;
        }
    }

    public static void SetBackground(ConsoleColor color)
    {
        if (enableColors) {
            Console.BackgroundColor = color;
        }
    }

    public static void SetColors(ConsoleColor fg, ConsoleColor? bg = null)
    {
        if (enableColors) {
            Console.ForegroundColor = fg;
            Console.BackgroundColor = bg ?? fg;
        }
    }

    public static void ResetColor()
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
        InstallUnityCLI parsed = null;
        try {
            parsed = InstallUnityCLI.Parse(args);

            if (parsed.help) {
                parsed.PrintHelp();
                return 0;
            }

            if (parsed.version) {
                parsed.PrintVersion();
                return 0;
            }

            switch (parsed.action) {
                case null:
                    await parsed.Setup();
                    parsed.PrintHelp();
                    break;
                case "list":
                    await parsed.List();
                    break;
                case "details":
                    await parsed.Details();
                    break;
                case "install":
                    await parsed.Install();
                    break;
                case "uninstall":
                    await parsed.Uninstall();
                    break;
                default:
                    throw new Exception("Unknown action: " + parsed.action);
            }
            return 0;
        
        } catch (ArgumentsException e) {
            Arguments<InstallUnityCLI>.WriteArgumentsWithError(args, e);
            return 1;
        
        } catch (Exception e) {
            Console.WriteLine();
            WriteException(e, parsed == null || parsed.verbose > 0);
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
            InstallUnityCLI.SetForeground(ConsoleColor.Red);
            Console.WriteLine(e.Message);
            if (stackTrace) {
                InstallUnityCLI.SetForeground(ConsoleColor.Gray);
                Console.WriteLine(e.StackTrace);
            }
            InstallUnityCLI.ResetColor();
        }
    }
}

}
