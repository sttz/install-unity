using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace sttz.InstallUnity
{

/// <summary>
/// Platform-specific installer code for macOS.
/// </summary>
public class MacPlatform : IInstallerPlatform
{
    /// <summary>
    /// Bundle ID of Unity editors (applies for 5+, even 2018).
    /// </summary>
    const string BUNDLE_ID = "com.unity3d.UnityEditor5.x";

    /// <summary>
    /// Default installation path.
    /// </summary>
    const string INSTALL_PATH = "/Applications/Unity";

    /// <summary>
    /// Path used to temporarily move existing installation out of the way.
    /// </summary>
    const string INSTALL_PATH_TMP = "/Applications/Unity (Moved by " + UnityInstaller.PRODUCT_NAME + ")";

    /// <summary>
    /// Match the mount point from hdiutil's output, e.g.:
    /// /dev/disk4s2        	Apple_HFS                      	/private/tmp/dmg.0bDM7Q
    /// </summary>
    static Regex MOUNT_POINT_REGEX = new Regex(@"^(?:\/dev\/\w+)[\t ]+(?:\w+)[\t ]+(\/.*)$", RegexOptions.Multiline);

    // -------- IInstallerPlatform --------

    string GetUserLibraryDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        return Path.Combine(home, "Library");
    }

    string GetUserApplicationSupportDirectory()
    {
        return Path.Combine(Path.Combine(GetUserLibraryDirectory(), "Application Support"), UnityInstaller.PRODUCT_NAME);
    }

    public string GetConfigurationDirectory()
    {
        return GetUserApplicationSupportDirectory();
    }

    public string GetCacheDirectory()
    {
        return GetUserApplicationSupportDirectory();
    }

    public string GetDownloadDirectory()
    {
        return Path.Combine(Path.GetTempPath(), UnityInstaller.PRODUCT_NAME);
    }

    public Task<bool> IsAdmin(CancellationToken cancellation = default)
    {
        return CheckIsRoot(false, cancellation);
    }

    public async Task<bool> PromptForPasswordIfNecessary(CancellationToken cancellation = default)
    {
        if (await CheckIsRoot(false, cancellation)) return true;

        Console.WriteLine();

        var attempts = 3;
        while (true) {
            if (pwd == null) {
                Console.Write($"{UnityInstaller.PRODUCT_NAME} requires your admin password: ");
                pwd = Helpers.ReadPassword();
            }

            if (await CheckIsRoot(true, cancellation)) {
                return true;
            } else if (--attempts > 0) {
                Console.WriteLine("Sorry, try again.");
                pwd = null;
            } else {
                pwd = null;
                return false;
            }
        }
    }

    public async Task<IEnumerable<Installation>> FindInstallations(CancellationToken cancellation = default)
    {
        var findResult = await Command.Run("/usr/bin/mdfind", $"kMDItemCFBundleIdentifier = '{BUNDLE_ID}'", null, cancellation);
        if (findResult.exitCode != 0) {
            throw new Exception($"ERROR: failed to run mdfind: {findResult.error}");
        }

        var lines = findResult.output.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var installations = new List<Installation>(lines.Length);
        foreach (var appPath in lines) {
            if (!Directory.Exists(appPath)) {
                Logger.LogWarning($"Could not find Unity installation at path: {appPath}");
                continue;
            }

            var installRoot = Path.GetDirectoryName(appPath);

            // Cursory check if the user has moved the Unity.app somewhere else
            var bugReporterPath = Path.Combine(installRoot, "Unity Bug Reporter.app");
            if (!Directory.Exists(bugReporterPath)) {
                Logger.LogWarning("Unity.app appears to be in a non-standard Unity folder: " + appPath);
                continue;
            }

            var versionResult = await Command.Run("/usr/bin/defaults", $"read \"{appPath}/Contents/Info\" CFBundleVersion", null, cancellation);
            if (versionResult.exitCode != 0) {
                throw new Exception($"ERROR: failed to run defaults: {versionResult.error}");
            }

            var version = new UnityVersion(versionResult.output.Trim());
            if (!version.IsFullVersion) {
                Logger.LogWarning($"Could not determine Unity version at path '{appPath}': {versionResult.output.Trim()}");
                continue;
            }

            var hashResult = await Command.Run("/usr/bin/defaults", $"read \"{appPath}/Contents/Info\" UnityBuildNumber", null, cancellation);
            if (hashResult.exitCode != 0) {
                throw new Exception($"ERROR: failed to run defaults: {hashResult.error}");
            }

            version.hash = hashResult.output.Trim();

            var executable = ExecutableFromAppPath(appPath);
            if (executable == null) continue;

            Logger.LogDebug($"Found Unity {version} at path: {appPath}");
            installations.Add(new Installation() {
                path = installRoot,
                executable = executable,
                version = version
            });
        }

        return installations;
    }

    public async Task PrepareInstall(UnityInstaller.Queue queue, string installationPaths, CancellationToken cancellation = default)
    {
        if (installing.version.IsValid)
            throw new InvalidOperationException($"Already installing another version: {installing.version}");

        installing = queue.metadata;
        this.installationPaths = installationPaths;
        installedEditor = false;

        // Move existing installation out of the way
        movedExisting = false;
        if (Directory.Exists(INSTALL_PATH)) {
            if (Directory.Exists(INSTALL_PATH_TMP)) {
                throw new InvalidOperationException($"Fallback installation path '{INSTALL_PATH_TMP}' already exists.");
            }
            Logger.LogInformation("Temporarily moving existing installation at default install path: " + INSTALL_PATH);
            await Move(INSTALL_PATH, INSTALL_PATH_TMP, cancellation);
            movedExisting = true;
        }

        // Check for upgrading installation
        upgradeOriginalPath = null;
        if (!queue.items.Any(i => i.package.name == PackageMetadata.EDITOR_PACKAGE_NAME)) {
            var installs = await FindInstallations(cancellation);
            var existingInstall = installs.Where(i => i.version == queue.metadata.version).FirstOrDefault();
            if (existingInstall == null) {
                throw new InvalidOperationException($"Not installing editor but version {queue.metadata.version} not already installed.");
            }

            upgradeOriginalPath = existingInstall.path;

            Logger.LogInformation($"Temporarily moving installation to upgrade from '{existingInstall}' to default install path");
            await Move(existingInstall.path, INSTALL_PATH, cancellation);
        }
    }

    public async Task Install(UnityInstaller.Queue queue, UnityInstaller.QueueItem item, CancellationToken cancellation = default)
    {
        if (item.package.name != PackageMetadata.EDITOR_PACKAGE_NAME && !installedEditor && upgradeOriginalPath == null) {
            throw new InvalidOperationException("Cannot install package without installing editor first.");
        }

        var extentsion = Path.GetExtension(item.filePath).ToLower();
        if (extentsion == ".pkg") {
            await InstallPkg(item.package.name, item.filePath, "/", cancellation);
        } else if (extentsion == ".dmg") {
            await InstallDmg(item.package.name, item.filePath, "/", cancellation);
        } else {
            throw new Exception("Cannot install package of type: " + extentsion);
        }

        if (item.package.name == PackageMetadata.EDITOR_PACKAGE_NAME) {
            installedEditor = true;
        }
    }

    public async Task<Installation> CompleteInstall(bool aborted, CancellationToken cancellation = default)
    {
        if (!installing.version.IsValid)
            throw new InvalidOperationException("Not installing any version to complete");

        string destination = null;
        if (upgradeOriginalPath != null) {
            // Move back installation
            destination = upgradeOriginalPath;
            Logger.LogInformation("Moving back upgraded installation to: " + destination);
            await Move(INSTALL_PATH, destination, cancellation);
        } else if (!aborted) {
            // Move new installations to "Unity VERSION"
            destination = GetUniqueInstallationPath(installing.version, installationPaths);
            Logger.LogInformation("Moving newly installed version to: " + destination);
            await Move(INSTALL_PATH, destination, cancellation);
        } else if (aborted) {
            // Clean up partial installation
            Logger.LogInformation("Deleting aborted installation at path: " + INSTALL_PATH);
            await Delete(INSTALL_PATH, cancellation);
        }

        // Move back original Unity folder
        if (movedExisting) {
            Logger.LogInformation("Moving back installation that was at default installation path");
            await Move(INSTALL_PATH_TMP, INSTALL_PATH, cancellation);
        }

        if (!aborted) {
            var executable = ExecutableFromAppPath(Path.Combine(destination, "Unity.app"));
            if (executable == null) return default;

            var installation = new Installation() {
                version = installing.version,
                executable = executable,
                path = destination
            };

            installing = default;
            movedExisting = false;
            upgradeOriginalPath = null;

            return installation;
        } else {
            return default;
        }
    }

    public async Task MoveInstallation(Installation installation, string newPath, CancellationToken cancellation = default)
    {
        if (Directory.Exists(newPath) || File.Exists(newPath))
            throw new ArgumentException("Destination path already exists: " + newPath);

        await Move(installation.path, newPath, cancellation);
        installation.path = newPath;
    }

    public async Task Uninstall(Installation installation, CancellationToken cancellation = default)
    {
        await Delete(installation.path, cancellation);
    }

    public async Task Run(Installation installation, IEnumerable<string> arguments, bool child)
    {
        if (!child) {
            var cmd = new System.Diagnostics.Process();
            cmd.StartInfo.FileName = "/usr/bin/open";
            cmd.StartInfo.Arguments = $"-a \"{installation.executable}\" -n --args {string.Join(" ", arguments)}";
            Logger.LogInformation($"$ {cmd.StartInfo.FileName} {cmd.StartInfo.Arguments}");
            
            cmd.Start();
            
            while (!cmd.HasExited) {
                await Task.Delay(100);
            }

        } else {
            if (!arguments.Contains("-logFile")) {
                arguments = arguments.Append("-logFile").Append("-");
            }

            var cmd = new System.Diagnostics.Process();
            cmd.StartInfo.FileName = installation.executable;
            cmd.StartInfo.Arguments = string.Join(" ", arguments);
            cmd.StartInfo.UseShellExecute = false;

            cmd.StartInfo.RedirectStandardOutput = true;
            cmd.StartInfo.RedirectStandardError = true;
            cmd.EnableRaisingEvents = true;

            cmd.OutputDataReceived += (s, a) => {
                if (a.Data == null) return;
                Logger.LogInformation(a.Data);
            };
            cmd.ErrorDataReceived += (s, a) => {
                if (a.Data == null) return;
                Logger.LogError(a.Data);
            };

            cmd.Start();
            cmd.BeginOutputReadLine();
            cmd.BeginErrorReadLine();

            while (!cmd.HasExited) {
                await Task.Delay(100);
            }

            cmd.WaitForExit(); // Let stdout and stderr flush
            Logger.LogInformation($"Unity exited with code {cmd.ExitCode}");
            Environment.Exit(cmd.ExitCode);
        }
    }

    // -------- Helpers --------

    ILogger Logger = UnityInstaller.CreateLogger<MacPlatform>();

    bool? isRoot;
    string pwd;
    VersionMetadata installing;
    string installationPaths;
    string upgradeOriginalPath;
    bool movedExisting;
    bool installedEditor;

    /// <summary>
    /// Get the path to the Unity executable inside the App bundle.
    /// </summary>
    string ExecutableFromAppPath(string appPath)
    {
        var executable = Path.Combine(appPath, "Contents", "MacOS", "Unity");
        if (!File.Exists(executable)) {
            Logger.LogError("Could not find Unity executable at path: " + executable);
            return null;
        }
        return executable;
    }

    /// <summary>
    /// Install a PKG package using the `installer` command.
    /// </summary>
    async Task InstallPkg(string packageId, string packagePath, string target, CancellationToken cancellation = default)
    {
        var result = await Sudo("/usr/sbin/installer", $"-pkg \"{packagePath}\" -target \"{target}\" -verbose", cancellation);
        if (result.exitCode != 0) {
            throw new Exception($"ERROR: failed to run installer: {result.error}");
        }
    }

    /// <summary>
    /// Install a DMG package by mounting it and copying the app bundle.
    /// </summary>
    async Task InstallDmg(string packageId, string packagePath, string target, CancellationToken cancellation = default)
    {
        // Mount DMG
        var result = await Command.Run("/usr/bin/hdiutil", $"attach -nobrowse -mountrandom /tmp \"{packagePath}\"", cancellation: cancellation);
        if (result.exitCode != 0) {
            throw new Exception($"ERROR: failed to run hdiutil: {result.error}");
        }

        var matches = MOUNT_POINT_REGEX.Matches(result.output);
        if (matches.Count == 0) {
            throw new Exception($"Failed to find mountpoint in hdiutil output: {result.output} / {result.error}");
        } else if (matches.Count > 1) {
            throw new Exception("Ambiguous mount points in hdiutil output (DMG contains multiple volumes?)");
        }

        var mountPoint = matches[0].Groups[1].Value;
        if (!Directory.Exists(mountPoint)) {
            throw new Exception("Mount point does not exist: " + mountPoint);
        }

        try {
            // Find and copy app bundle
            var apps = Directory.GetDirectories(mountPoint, "*.app");
            if (apps.Length == 0) {
                throw new Exception("No app bundles found in DMG.");
            }
            
            var targetDir = Path.Combine(target, "Applications");
            foreach (var app in apps) {
                var dst = Path.Combine(targetDir, Path.GetFileName(app));
                if (Directory.Exists(dst) || File.Exists(dst)) {
                    await Delete(dst, cancellation);
                }
                await Copy(app, dst, cancellation);
            }
        } finally {
            // Unmount dmg
            result = await Command.Run("/usr/bin/hdiutil", $"detach \"{mountPoint}\"", cancellation: cancellation);
            if (result.exitCode != 0) {
                Logger.LogError($"Failed to run hdiutil: {result.error}");
            }
        }
    }

    /// <summary>
    /// Find a unique path for a new installation.
    /// Tries paths in installationPaths until one is unused, falls back to adding
    /// increasing numbers to the the last path in installationPaths or using the
    /// default installation path.
    /// </summary>
    /// <param name="version">Unity version being installed</param>
    /// <param name="installationPaths">Paths string (see <see cref="Configuration.installPathMac"/></param>
    string GetUniqueInstallationPath(UnityVersion version, string installationPaths)
    {
        string expanded = null;
        if (!string.IsNullOrEmpty(installationPaths)) {
            var comparison = StringComparison.OrdinalIgnoreCase;
            var paths = installationPaths.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var path in paths) {
                expanded = path.Trim();
                expanded = Helpers.Replace(expanded, "{major}", version.major.ToString(), comparison);
                expanded = Helpers.Replace(expanded, "{minor}", version.minor.ToString(), comparison);
                expanded = Helpers.Replace(expanded, "{patch}", version.patch.ToString(), comparison);
                expanded = Helpers.Replace(expanded, "{type}",  ((char)version.type).ToString(), comparison);
                expanded = Helpers.Replace(expanded, "{build}", version.build.ToString(), comparison);
                expanded = Helpers.Replace(expanded, "{hash}",  version.hash, comparison);
                
                if (!Directory.Exists(expanded)) {
                    return expanded;
                }
            }
        }

        if (expanded != null) {
            return Helpers.GenerateUniqueFileName(expanded);
        } else {
            return Helpers.GenerateUniqueFileName(INSTALL_PATH);
        }
    }

    /// <summary>
    /// Move a directory, first trying directly and falling back to `sudo mv` if that fails.
    /// </summary>
    async Task Move(string sourcePath, string newPath, CancellationToken cancellation)
    {
        var baseDst = Path.GetDirectoryName(newPath);

        try {
            if (!Directory.Exists(baseDst)) {
                Directory.CreateDirectory(baseDst);
            }
            Directory.Move(sourcePath, newPath);
            return;
        } catch (Exception e) {
            Logger.LogInformation($"Move as user failed, trying as root... ({e.Message})");
        }

        // Try again with admin privileges
        var result = await Sudo("/bin/mkdir", $"-p \"{baseDst}\"", cancellation);
        if (result.exitCode != 0) {
            throw new Exception($"ERROR: failed to run mkdir: {result.error}");
        }

        result = await Sudo("/bin/mv", $"\"{sourcePath}\" \"{newPath}\"", cancellation);
        if (result.exitCode != 0) {
            throw new Exception($"ERROR: failed to run mv: {result.error}");
        }
    }

    /// <summary>
    /// Copy a directory, first trying as the current user and using sudo if that fails.
    /// </summary>
    async Task Copy(string sourcePath, string newPath, CancellationToken cancellation)
    {
        var baseDst = Path.GetDirectoryName(newPath);

        (int exitCode, string output, string error) result;
        try {
            result = await Command.Run("/bin/mkdir", $"-p \"{baseDst}\"", cancellation: cancellation);
            if (result.exitCode != 0) {
                throw new Exception($"ERROR: failed to run mkdir: {result.error}");
            }

            result = await Command.Run("/bin/cp", $"-R \"{sourcePath}\" \"{newPath}\"", cancellation: cancellation);
            if (result.exitCode != 0) {
                throw new Exception($"ERROR: failed to run cp: {result.error}");
            }

            return;
        } catch (Exception e) {
            Logger.LogInformation($"Copy as user failed, trying as root... ({e.Message})");
        }

        // Try again with admin privileges
        result = await Sudo("/bin/mkdir", $"-p \"{baseDst}\"", cancellation);
        if (result.exitCode != 0) {
            throw new Exception($"ERROR: failed to run mkdir: {result.error}");
        }

        result = await Sudo("/bin/mv", $"\"{sourcePath}\" \"{newPath}\"", cancellation);
        if (result.exitCode != 0) {
            throw new Exception($"ERROR: failed to run mv: {result.error}");
        }
    }

    /// <summary>
    /// Delete a directory, first trying directly and falling back to `sudo rm` if that fails.
    /// </summary>
    async Task Delete(string deletePath, CancellationToken cancellation = default)
    {
        // First try deleting the installation directly
        try {
            Directory.Delete(deletePath, true);
            return;
        } catch (Exception e) {
            Logger.LogInformation($"Deleting as user failed, trying as root... ({e.Message})");
        }

        // Try again with admin privileges
        var result = await Sudo("/bin/rm", $"-rf \"{deletePath}\"", cancellation);
        if (result.exitCode != 0) {
            throw new Exception($"ERROR: failed to run rm: {result.error}");
        }
    }

    /// <summary>
    /// Check if the program is running as root.
    /// </summary>
    async Task<bool> CheckIsRoot(bool withSudo, CancellationToken cancellation)
    {
        var command = "/usr/bin/id";
        var arguments = "-u";
        (int exitCode, string output, string error) result;
        if (withSudo) {
            result = await Sudo(command, arguments, cancellation: cancellation);
            if (result.exitCode != 0) {
                if (result.exitCode == 1 && result.error.Contains("Sorry, try again.")) {
                    return false;
                } else {
                    throw new Exception($"ERROR: failed to run id: {result.error}");
                }
            }
        } else {
            result = await Command.Run(command, arguments, cancellation: cancellation);
            if (result.exitCode != 0) {
                throw new Exception($"ERROR: failed to run id: {result.error}");
            }
        }

        int id;
        if (!int.TryParse(result.output, out id)) {
            throw new Exception($"ERROR: failed to run id, cannot parse output: {result.output} / {result.error}");
        }

        return (id == 0);
    }

    /// <summary>
    /// Run a command as root using sudo.
    /// This will prompt the user for the password the first time it's run.
    /// If the user is already root, this is equivalent to calling the command directly.
    /// </summary>
    async Task<(int exitCode, string output, string error)> Sudo(string command, string arguments, CancellationToken cancellation)
    {
        if (isRoot == null) {
            isRoot = await CheckIsRoot(false, cancellation);
        }

        if (isRoot == true) {
            // Run the command directly if we already are root
            return await Command.Run(command, arguments, cancellation: cancellation);
        
        } else {
            if (pwd == null) {
                await PromptForPasswordIfNecessary(cancellation);
            }

            // Run command using sudo
            return await Command.Run("sudo", "-Sk " + command + " " + arguments, pwd + "\n", cancellation);
        }
    }
}

}
