using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace sttz.InstallUnity
{

/// <summary>
/// Class representing an existing Unity instalation.
/// </summary>
public class Installation
{
    /// <summary>
    /// Path to the Unity installation root.
    /// </summary>
    public string path;

    /// <summary>
    /// Path to the Unity executable.
    /// </summary>
    public string executable;

    /// <summary>
    /// Version of the installation.
    /// </summary>
    public UnityVersion version;
}

/// <summary>
/// Interface for the platform-specific installer implementation.
/// </summary>
public interface IInstallerPlatform
{
    /// <summary>
    /// The path to the file where settings are stored.
    /// </summary>
    string GetConfigurationDirectory();

    /// <summary>
    /// The directory where cache files are stored.
    /// </summary>
    string GetCacheDirectory();

    /// <summary>
    /// The directory where downloaded files are temporarily stored.
    /// </summary>
    string GetDownloadDirectory();

    /// <summary>
    /// Check wether the user is admin when installing.
    /// </summary>
    Task<bool> IsAdmin(CancellationToken cancellation = default);

    /// <summary>
    /// Prompt for the admin password if it's necessary to install Unity.
    /// </summary>
    /// <returns>If the password was acquired successfully</returns>
    Task<bool> PromptForPasswordIfNecessary(CancellationToken cancellation = default);

    /// <summary>
    /// Find all existing Unity installations.
    /// </summary>
    Task<IEnumerable<Installation>> FindInstallations(CancellationToken cancellation = default);

    /// <summary>
    /// Move an existing Unity installation.
    /// </summary>
    Task MoveInstallation(Installation installation, string newPath, CancellationToken cancellation = default);

    /// <summary>
    /// Prepare to install the given version of Unity.
    /// </summary>
    /// <param name="queue">The installation queue to prepare</param>
    /// <param name="installationPaths">Paths to try to move the installation to (see <see cref="Configuration.installPathMac"/>)</param>
    /// <param name="cancellation">Cancellation token</param>
    Task PrepareInstall(UnityInstaller.Queue queue, string installationPaths, CancellationToken cancellation = default);

    /// <summary>
    /// Install a package (<see cref="PrepareInstall"/> has to be called first).
    /// </summary>
    /// <param name="queue">The installation queue</param>
    /// <param name="item">The queue item to install</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns></returns>
    Task Install(UnityInstaller.Queue queue, UnityInstaller.QueueItem item, CancellationToken cancellation = default); 

    /// <summary>
    /// Complete an installation after all packages have been installed.
    /// </summary>
    /// <param name="cancellation">Cancellation token</param>
    Task<Installation> CompleteInstall(bool aborted, CancellationToken cancellation = default);

    /// <summary>
    /// Uninstall a Unity installation.
    /// </summary>
    Task Uninstall(Installation instalation, CancellationToken cancellation = default);

    /// <summary>
    /// Run a Unity installation with the given arguments.
    /// </summary>
    /// <param name="installation">Unity installation to run</param>
    /// <param name="arguments">Arguments to pass to Unity</param>
    /// <param name="child">Wether to run Unity as a child process and forward its standard input/error and log</param>
    Task Run(Installation installation, IEnumerable<string> arguments, bool child);
}

}
