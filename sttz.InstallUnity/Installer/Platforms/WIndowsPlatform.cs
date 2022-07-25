using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace sttz.InstallUnity
{
    public class WIndowsPlatform : IInstallerPlatform
    {

        private string INSTALL_PATH => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor");

        string GetUserApplicationSupportDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                UnityInstaller.PRODUCT_NAME);
        }

        public Task<CachePlatform> GetCurrentPlatform()
        {
            return Task.FromResult(CachePlatform.Windows);
        }

        public Task<IEnumerable<CachePlatform>> GetInstallablePlatforms()
        {
            IEnumerable<CachePlatform> platforms = new CachePlatform[] { CachePlatform.Windows };
            return Task.FromResult(platforms);
        }

        public string GetCacheDirectory()
        {
            return GetUserApplicationSupportDirectory();
        }

        public string GetConfigurationDirectory()
        {
            return GetUserApplicationSupportDirectory();
        }

        public string GetDownloadDirectory()
        {
            return Path.Combine(Path.GetTempPath(), UnityInstaller.PRODUCT_NAME);
        }

        public async Task<bool> IsAdmin(CancellationToken cancellation = default)
        {
            return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        }

        public async Task<Installation> CompleteInstall(bool aborted, CancellationToken cancellation = default)
        {
            if (!installing.version.IsValid)
                throw new InvalidOperationException("Not installing any version to complete");

            if (!aborted)
            {
                var executable = Path.Combine(installationPaths, "Editor", "Unity.exe");
                if (executable == null) return default;

                var installation = new Installation()
                {
                    version = installing.version,
                    executable = executable,
                    path = installationPaths
                };

                installing = default;

                return installation;
            }
            else
            {
                return default;
            }
        }

        public async Task<IEnumerable<Installation>> FindInstallations(CancellationToken cancellation = default)
        {
            var hubInstallations = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor");
            var defaultUnityPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Editor");
            var installUnityPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "install-unity");
            var unityCandidates = new List<string>();
            if (Directory.Exists(hubInstallations))
                unityCandidates.AddRange(Directory.GetDirectories(hubInstallations));
            if (Directory.Exists(defaultUnityPath))
                unityCandidates.Add(defaultUnityPath);
            if (Directory.Exists(installUnityPath))
                unityCandidates.AddRange(Directory.GetDirectories(installUnityPath));
            var unityInstallations = new List<Installation>();
            foreach (var unityCandidate in unityCandidates)
            {
                var modulesJsonPath = Path.Combine(unityCandidate, "Editor", "Unity.exe");
                if (!File.Exists(modulesJsonPath))
                {
                    Logger.LogDebug($"No Unity.exe in {unityCandidate}\\Editor");
                    continue;
                }
                var versionInfo = FileVersionInfo.GetVersionInfo(modulesJsonPath);
                Logger.LogDebug($"Found version {versionInfo.ProductVersion}");
                unityInstallations.Add(new Installation {
                    executable = modulesJsonPath,
                    path = unityCandidate,
                    version = new UnityVersion(versionInfo.ProductVersion.Substring(0, versionInfo.ProductVersion.LastIndexOf("_"))) // Versions are on format 2020.3.34f1_9a4c9c70452b
                });
            }
            return unityInstallations;
        }

        public async Task Install(UnityInstaller.Queue queue, UnityInstaller.QueueItem item, CancellationToken cancellation = default)
        {
            if (item.package.name != PackageMetadata.EDITOR_PACKAGE_NAME && !installedEditor)
            {
                throw new InvalidOperationException("Cannot install package without installing editor first.");
            }

            var installPath = GetInstallationPath(installing.version, installationPaths);
            // TODO: start info runas
            var result = await RunAsAdmin(item.filePath, $"/S /D={installPath}");
            if (result.exitCode != 0)
            {
                throw new Exception($"Failed to install {item.filePath} output: {result.output} / {result.error}");
            }

            if (item.package.name == PackageMetadata.EDITOR_PACKAGE_NAME)
            {
                installedEditor = true;
            }
        }

        public async Task MoveInstallation(Installation installation, string newPath, CancellationToken cancellation = default)
        {
            // do nothing
        }

        public async Task PrepareInstall(UnityInstaller.Queue queue, string installationPaths, CancellationToken cancellation = default)
        {
            if (installing.version.IsValid)
                throw new InvalidOperationException($"Already installing another version: {installing.version}");

            installing = queue.metadata;
            this.installationPaths = installationPaths;
            installedEditor = false;

            // Check for upgrading installation
            if (!queue.items.Any(i => i.package.name == PackageMetadata.EDITOR_PACKAGE_NAME))
            {
                var installs = await FindInstallations(cancellation);
                var existingInstall = installs.Where(i => i.version == queue.metadata.version).FirstOrDefault();
                if (existingInstall == null)
                {
                    throw new InvalidOperationException($"Not installing editor but version {queue.metadata.version} not already installed.");
                }

                installedEditor = true;
            }
        }

        public async Task<bool> PromptForPasswordIfNecessary(CancellationToken cancellation = default)
        {
            // Don't care about password. The system will ask for elevated priviliges automatically
            return true;
        }

        public async Task Uninstall(Installation installation, CancellationToken cancellation = default)
        {
            var result = await RunAsAdmin(Path.Combine(installation.path, "Editor", "Uninstall.exe"), "/AllUsers /Q /S");
            if (result.exitCode != 0)
            {
                throw new Exception($"Could not uninstall Unity. output: {result.output}, error: {result.error}");
            }
        }

        // -------- Helpers --------

        ILogger Logger = UnityInstaller.CreateLogger<WIndowsPlatform>();

        VersionMetadata installing;
        string installationPaths;
        bool installedEditor;

        async Task<(int exitCode, string output, string error)> RunAsAdmin(string filename, string arguments)
        {
            var startInfo = new ProcessStartInfo();
            startInfo.FileName = filename;
            startInfo.Arguments = arguments;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.UseShellExecute = false;
            startInfo.WorkingDirectory = Environment.CurrentDirectory;
            startInfo.Verb = "runas";
            try
            {
                var p = Process.Start(startInfo);
                p.WaitForExit();
                return (p.ExitCode, p.StandardOutput.ReadToEnd(), p.StandardError.ReadToEnd());
            } catch (Exception)
            {
                Logger.LogError($"Execution of {filename} with {arguments} failed!");
                throw;
            }
        }

        string GetInstallationPath(UnityVersion version, string installationPaths)
        {
            string expanded = null;
            if (!string.IsNullOrEmpty(installationPaths))
            {
                var comparison = StringComparison.OrdinalIgnoreCase;
                var paths = installationPaths.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var path in paths)
                {
                    expanded = path.Trim();
                    expanded = Helpers.Replace(expanded, "{major}", version.major.ToString(), comparison);
                    expanded = Helpers.Replace(expanded, "{minor}", version.minor.ToString(), comparison);
                    expanded = Helpers.Replace(expanded, "{patch}", version.patch.ToString(), comparison);
                    expanded = Helpers.Replace(expanded, "{type}", ((char)version.type).ToString(), comparison);
                    expanded = Helpers.Replace(expanded, "{build}", version.build.ToString(), comparison);
                    expanded = Helpers.Replace(expanded, "{hash}", version.hash, comparison);

                    return expanded;
                }
            }

            if (expanded != null)
            {
                return Helpers.GenerateUniqueFileName(expanded);
            }
            else
            {
                return Helpers.GenerateUniqueFileName(INSTALL_PATH);
            }
        }

        public async Task Run(Installation installation, IEnumerable<string> arguments, bool child)
        {
            // child argument is ignored. We are always a child
            if (!arguments.Contains("-logFile"))
            {
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

            while (!cmd.HasExited)
            {
                await Task.Delay(100);
            }

            cmd.WaitForExit(); // Let stdout and stderr flush
            Logger.LogInformation($"Unity exited with code {cmd.ExitCode}");
            Environment.Exit(cmd.ExitCode);
        }
    }
}
