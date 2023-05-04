using IniParser.Parser;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using static sttz.InstallUnity.UnityReleaseAPIClient;

namespace sttz.InstallUnity
{

// Examples of JSON and ini files:
// https://public-cdn.cloud.unity3d.com/hub/prod/releases-darwin.json
// https://download.unity3d.com/download_unity/9fd71167a288/unity-2017.1.4f1-osx.ini
// http://download.unity3d.com/download_unity/787658998520/unity-2018.2.0f2-osx.ini
// https://beta.unity3d.com/download/48afb4a72b1a/unity-2018.2.1f1-linux.ini
// https://netstorage.unity3d.com/unity/1a9968d9f99c/unity-2018.2.1f1-win.ini

// https://netstorage.unity3d.com/unity and http://download.unity3d.com/download_unity seem to be interchangeable

/// <summary>
/// Discover available Unity versions.
/// </summary>
public class Scraper
{
    // -------- Version Indices --------

    /// <summary>
    /// Base URL of Unity homepage.
    /// </summary>
    const string UNITY_BASE_URL = "https://unity.com";

    /// <summary>
    /// HTML archive of Unity releases.
    /// </summary>
    const string UNITY_ARCHIVE = "https://unity.com/releases/editor/archive";

    /// <summary>
    /// Landing page for Unity beta releases.
    /// </summary>
    const string UNITY_BETA = "https://unity.com/releases/editor/beta";

    /// <summary>
    /// Landing page for Unity alpha releases.
    /// </summary>
    const string UNITY_ALPHA = "https://unity.com/releases/editor/alpha";

    // -------- Release Notes --------

    /// <summary>
    /// HTML release notes of final Unity releases (append a version without type or build number, e.g. 2018.2.1)
    /// </summary>
    const string UNITY_RELEASE_NOTES_FINAL = "https://unity.com/releases/editor/whats-new/";

    /// <summary>
    /// HTML release notes of alpha Unity releases (append a full alpha version string)
    /// </summary>
    const string UNITY_RELEASE_NOTES_ALPHA = "https://unity.com/releases/editor/alpha/";

    /// <summary>
    /// HTML release notes of beta Unity releases (append a full beta version string)
    /// </summary>
    const string UNITY_RELEASE_NOTES_BETA = "https://unity.com/releases/editor/beta/";

    // -------- INIs --------

    /// <summary>
    /// Base URL where INIs are located (append version hash).
    /// </summary>
    const string INI_BASE_URL = "https://download.unity3d.com/download_unity/";

    /// <summary>
    /// Base URL where INIs of beta/alpha versions are located (append version hash).
    /// </summary>
    const string INI_BETA_BASE_URL = "http://beta.unity3d.com/download/";

    /// <summary>
    /// Name of INI file with packages information (replace {0} with version and {1} with osx or win).
    /// </summary>
    const string UNITY_INI_FILENAME = "unity-{0}-{1}.ini";

    // -------- Regular Expressions --------

    /// <summary>
    /// Regex to extract version information from unityhub URL.
    /// </summary>
    static readonly Regex UNITYHUB_RE = new Regex(@"unityhub:\/\/(\d+\.\d+\.\d+\w\d+)\/([0-9a-f]{12})");

    /// <summary>
    /// Regex to extract version information from installer download URL.
    /// </summary>
    static readonly Regex UNITY_DOWNLOAD_RE = new Regex(@"https?:\/\/[\w.-]+unity3d\.com\/[\w\/.-]+\/([0-9a-f]{12})\/(?:[^\/]+\/)[\w\/.-]+-(\d+\.\d+\.\d+\w\d+)[\w\/.-]+");

    /// <summary>
    /// Regex to extract available prerelease versions from landing page.
    /// </summary>
    static readonly Regex UNITY_PRERELEASE_RE = new Regex(@"\/releases\/editor\/(alpha|beta)\/(\d+\.\d+\.\d+\w\d+)");

    // -------- Scraper --------

    static HttpClient client = new HttpClient();

    ILogger Logger = UnityInstaller.CreateLogger<Scraper>();

    /// <summary>
    /// Load the available final versions.
    /// </summary>
    /// <param name="cancellation"></param>
    /// <returns>Task returning the discovered versions</returns>
    public async Task<IEnumerable<VersionMetadata>> LoadFinal(CancellationToken cancellation = default)
    {
        Logger.LogInformation($"Scraping latest releases for {UnityVersion.Type.Final} from '{UNITY_ARCHIVE}'");
        var response = await client.GetAsync(UNITY_ARCHIVE, cancellation);
        if (!response.IsSuccessStatusCode) {
            Logger.LogWarning($"Failed to scrape url '{UNITY_ARCHIVE}' ({response.StatusCode})");
            return Enumerable.Empty<VersionMetadata>();
        }

        var html = await response.Content.ReadAsStringAsync();
        Logger.LogTrace($"Got response: {html}");

        return ExtractFromHtml(html, ReleaseStream.None).Values;
    }

    /// <summary>
    /// Load the available beta and/or alpha versions.
    /// </summary>
    /// <param name="cancellation"></param>
    /// <returns>Task returning the discovered versions</returns>
    public async Task<IEnumerable<VersionMetadata>> LoadPrerelease(bool includeAlpha, IEnumerable<UnityVersion> knownVersions = null, int scrapeDelay = 50, CancellationToken cancellation = default)
    {
        var results = new Dictionary<UnityVersion, VersionMetadata>();

        if (includeAlpha) {
            await LoadPrerelease(UNITY_ALPHA, ReleaseStream.Alpha, results, knownVersions, scrapeDelay, cancellation);
        }

        await LoadPrerelease(UNITY_BETA, ReleaseStream.Beta, results, knownVersions, scrapeDelay, cancellation);

        return results.Values;
    }

    /// <summary>
    /// Load the available prerelase versions from a alpha/beta landing page.
    /// </summary>
    async Task LoadPrerelease(string url, ReleaseStream stream, Dictionary<UnityVersion, VersionMetadata> results, IEnumerable<UnityVersion> knownVersions = null, int scrapeDelay = 50, CancellationToken cancellation = default)
    {
        // Load major version's individual prerelease page to get individual versions
        Logger.LogInformation($"Scraping latest prereleases from '{url}'");
        await Task.Delay(scrapeDelay);
        var response = await client.GetAsync(url, cancellation);
        if (!response.IsSuccessStatusCode) {
            Logger.LogWarning($"Failed to scrape url '{url}' ({response.StatusCode})");
            return;
        }

        var html = await response.Content.ReadAsStringAsync();
        Logger.LogTrace($"Got response: {html}");

        var versionMatches = UNITY_PRERELEASE_RE.Matches(html);
        foreach (Match versionMatch in versionMatches) {
            var version = new UnityVersion(versionMatch.Groups[2].Value);
            if (results.ContainsKey(version)) continue;
            if (knownVersions != null && knownVersions.Contains(version)) continue;

            // Load version's release notes to get download links
            var prereleaseUrl = UNITY_BASE_URL + versionMatch.Value;
            Logger.LogInformation($"Scraping {versionMatch.Groups[1].Value} {version} from '{prereleaseUrl}'");
            await Task.Delay(scrapeDelay);
            response = await client.GetAsync(prereleaseUrl, cancellation);
            if (!response.IsSuccessStatusCode) {
                Logger.LogWarning($"Could not load release notes at url '{prereleaseUrl}' ({response.StatusCode})");
                continue;
            }

            html = await response.Content.ReadAsStringAsync();
            Logger.LogTrace($"Got response: {html}");
            ExtractFromHtml(html, stream, results);
        }
    }

    /// <summary>
    /// Get the INI base URL for the given version type.
    /// </summary>
    string GetIniBaseUrl(UnityVersion.Type type)
    {
        if (type == UnityVersion.Type.Beta || type == UnityVersion.Type.Alpha) {
            return INI_BETA_BASE_URL;
        } else {
            return INI_BASE_URL;
        }
    }

    /// <summary>
    /// Extract the versions and the base URLs from the html string.
    /// </summary>
    Dictionary<UnityVersion, VersionMetadata> ExtractFromHtml(string html, ReleaseStream stream, Dictionary<UnityVersion, VersionMetadata> results = null)
    {
        var matches = UNITYHUB_RE.Matches(html);
        results = results ?? new Dictionary<UnityVersion, VersionMetadata>();
        foreach (Match match in matches) {
            var version = new UnityVersion(match.Groups[1].Value);
            version.hash = match.Groups[2].Value;

            VersionMetadata metadata = default;
            if (!results.TryGetValue(version, out metadata)) {
                if (stream == ReleaseStream.None)
                metadata = CreateEmptyVersion(version, stream);
            }

            metadata.baseUrl = GetIniBaseUrl(version.type) + version.hash + "/";
            results[version] = metadata;
        }

        matches = UNITY_DOWNLOAD_RE.Matches(html);
        foreach (Match match in matches) {
            var version = new UnityVersion(match.Groups[2].Value);
            version.hash = match.Groups[1].Value;

            VersionMetadata metadata = default;
            if (!results.TryGetValue(version, out metadata)) {
                metadata = CreateEmptyVersion(version, stream);
            }

            metadata.baseUrl = GetIniBaseUrl(version.type) + version.hash + "/";
            results[version] = metadata;
        }

        return results;
    }

    /// <summary>
    /// Convert a UnityHub URL to a VersionMetadata struct.
    /// </summary>
    /// <remarks>
    /// Conversion is completely offline, no http lookup is made.
    /// </remarks>
    public VersionMetadata UnityHubUrlToVersion(string url)
    {
        var match = UNITYHUB_RE.Match(url);
        if (!match.Success) return default(VersionMetadata);

        var version = new UnityVersion(match.Groups[1].Value);
        version.hash = match.Groups[2].Value;

        var metadata = CreateEmptyVersion(version, ReleaseStream.None);
        metadata.baseUrl = GetIniBaseUrl(version.type) + version.hash + "/";

        return metadata;
    }

    /// <summary>
    /// Try to load the metadata from a version by guessing its release notes URL.
    /// </summary>
    /// <remarks>
    /// The version must include major, minor and patch components.
    /// For beta and alpha releases, it must also contain the build component.
    /// If no type is set, final is assumed.
    /// </remarks>
    /// <param name="version">The version</param>
    /// <returns>The metadata or the default value if the version couldn't be found.</returns>
    public async Task<VersionMetadata> LoadExact(UnityVersion version, CancellationToken cancellation = default)
    {
        if (version.major < 0 || version.minor < 0 || version.patch < 0) {
            throw new ArgumentException("The Unity version is incomplete (major, minor or patch missing)", nameof(version));
        }
        if (version.type != UnityVersion.Type.Final && version.type != UnityVersion.Type.Undefined && version.build < 0) {
            throw new ArgumentException("The Unity version is incomplete (build missing)", nameof(version));
        }

        var stream = GuessStreamFromVersion(version);
        var url = GetReleaseNotesUrl(stream, version);
        if (url == null) {
            throw new ArgumentException("The Unity version type is not supported: " + version.type, nameof(version));
        }

        Logger.LogInformation($"Guessed release notes url for exact version {version}: {url}");
        return await LoadUrl(url, cancellation);
    }

    /// <summary>
    /// Try to load metadata from a version by scraping a custom URL.
    /// </summary>
    /// <param name="url">URL to a HTML page to look for Unity versions.</param>
    /// <returns>The first Unity version found at URL or the default value if none could be found.</returns>
    public async Task<VersionMetadata> LoadUrl(string url, CancellationToken cancellation = default)
    {
        Logger.LogInformation($"Trying to find Unity version at url: {url}");

        var response = await client.GetAsync(url, cancellation);
        if (!response.IsSuccessStatusCode) {
            return default;
        }

        var html = await response.Content.ReadAsStringAsync();
        Logger.LogTrace($"Got response: {html}");
        return ExtractFromHtml(html, ReleaseStream.None).Values.FirstOrDefault();
    }

    /// <summary>
    /// Load the packages of a Unity version.
    /// The VersionMetadata must have iniUrl set.
    /// </summary>
    /// <param name="metadata">Version metadata with iniUrl.</param>
    /// <param name="platform">Name of platform to load the packages for</param>
    /// <returns>A Task returning the metadata with packages filled in.</returns>
    public async Task<VersionMetadata> LoadPackages(VersionMetadata metadata, Platform platform, Architecture architecture, CancellationToken cancellation = default)
    {
        if (!metadata.Version.IsFullVersion) {
            throw new ArgumentException("Unity version needs to be a full version", nameof(metadata));
        }

        if (platform == Platform.Mac_OS && architecture == Architecture.ARM64 && metadata.Version < new UnityVersion(2021, 2)) {
            throw new ArgumentException("Apple Silicon builds are only available from Unity 2021.2", nameof(metadata));
        }

        string platformName = platform switch {
            Platform.Mac_OS  => "osx",
            Platform.Windows => "win",
            Platform.Linux   => "linux",
            _ => throw new NotImplementedException("Invalid platform name: " + platform)
        };

        if (string.IsNullOrEmpty(metadata.baseUrl)) {
            throw new ArgumentException("VersionMetadata.baseUrl is not set for " + metadata.Version, nameof(metadata));
        }

        var url = metadata.baseUrl + string.Format(UNITY_INI_FILENAME, metadata.Version.ToString(false), platformName);
        Logger.LogInformation($"Loading packages for {metadata.Version} and {platformName} from '{url}'");
        var response = await client.GetAsync(url, cancellation);
        response.EnsureSuccessStatusCode();

        var ini = await response.Content.ReadAsStringAsync();
        Logger.LogTrace($"Got response: {ini}");

        var parser = new IniDataParser();
        IniParser.Model.IniData data = null;
        try {
            data = parser.Parse(ini);
        } catch (Exception e) {
            Logger.LogWarning($"Error parsing ini file, trying again with skipping invalid lines... ({e.Message})");
            parser.Configuration.SkipInvalidLines = true;
            data = parser.Parse(ini);
        }

        var editorDownload = new EditorDownload();
        editorDownload.platform = platform;
        editorDownload.architecture = architecture;
        editorDownload.modules = new List<Module>();

        // Create modules from all entries
        var allModules = new Dictionary<string, Module>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in data.Sections) {
            if (section.SectionName.Equals(EditorDownload.ModuleId, StringComparison.OrdinalIgnoreCase)) {
                SetDownloadKeys(editorDownload, section);
                continue;
            }

            var module = new Module();
            module.id = section.SectionName;

            SetDownloadKeys(module, section);
            SetModuleKeys(module, section);

            allModules.Add(module.id, module);
        }

        // Add virtual packages
        foreach (var virutal in VirtualPackages.GeneratePackages(metadata.Version, editorDownload)) {
            allModules.Add(virutal.id, virutal);
        }

        // Register sub-modules with their parents
        foreach (var module in allModules.Values) {
            if (module.parentModuleId == null) continue;

            if (!allModules.TryGetValue(module.parentModuleId, out var parentModule))
                throw new Exception($"Missing parent module '{module.parentModuleId}' for modules '{module.id}'");

            if (parentModule.subModules == null)
                parentModule.subModules = new List<Module>();
            
            parentModule.subModules.Add(module);
            module.parentModule = parentModule;
        }

        // Register remaining root modules with main editor download
        foreach (var possibleRoot in allModules.Values) {
            if (possibleRoot.parentModule != null)
                continue;
            
            editorDownload.modules.Add(possibleRoot);
        }

        Logger.LogInformation($"Found {allModules.Count} packages");

        // Patch editor URL to point to Apple Silicon editor
        // The old ini system probably won't be updated to include Apple Silicon variants
        if (platform == Platform.Mac_OS && architecture == Architecture.ARM64) {
            // Change e.g.
            // https://download.unity3d.com/download_unity/e50cafbb4399/MacEditorInstaller/Unity.pkg
            // to
            // https://download.unity3d.com/download_unity/e50cafbb4399/MacEditorInstallerArm64/Unity.pkg
            var editorUrl = editorDownload.url;
            if (!editorUrl.StartsWith("MacEditorInstaller/")) {
                throw new Exception($"Cannot convert to Apple Silicon editor URL: Does not start with 'MacEditorInstaller' (got '{editorUrl}')");
            }
            editorUrl = editorUrl.Replace("MacEditorInstaller/", "MacEditorInstallerArm64/");
            editorDownload.url = editorUrl;

            // Clear fields that are now invalid
            editorDownload.integrity = null;
        }

        metadata.SetEditorDownload(editorDownload);
        return metadata;
    }

    void SetDownloadKeys(Download download, IniParser.Model.SectionData section)
    {
        foreach (var pair in section.Keys) {
            switch (pair.KeyName) {
                case "url":
                    download.url = pair.Value;
                    break;
                case "extension":
                    download.type = pair.Value switch {
                        "txt" => FileType.TEXT,
                        "zip" => FileType.ZIP,
                        "pkg" => FileType.PKG,
                        "exe" => FileType.EXE,
                        "po"  => FileType.PO,
                        "dmg" => FileType.DMG,
                        _     => FileType.Undefined,
                    };
                    break;
                case "size":
                    download.downloadSize.value = long.Parse(pair.Value);
                    download.downloadSize.unit = "BYTE";
                    break;
                case "installedsize":
                    download.installedSize.value = long.Parse(pair.Value);
                    download.installedSize.unit = "BYTE";
                    break;
                case "md5":
                    download.integrity = $"md5-{pair.Value}";
                    break;
            }
        }
    }

    void SetModuleKeys(Module download, IniParser.Model.SectionData section)
    {
        var eulaUrl1 = section.Keys["eulaurl1"];
        if (eulaUrl1 != null) {
            var eulaMessage = section.Keys["eulamessage"];
            var eulaUrl2 = section.Keys["eulaurl2"];

            var eulaCount = (eulaUrl2 != null ? 2 : 1);
            download.eula = new Eula[eulaCount];

            download.eula[0] = new Eula() {
                message = eulaMessage,
                label = section.Keys["eulalabel1"],
                url = eulaUrl1
            };

            if (eulaCount > 1) {
                download.eula[1] = new Eula() {
                    message = eulaMessage,
                    label = section.Keys["eulalabel2"],
                    url = eulaUrl2
                };
            }
        }

        foreach (var pair in section.Keys) {
            switch (pair.KeyName) {
                case "title":
                    download.name = pair.Value;
                    break;
                case "description":
                    download.description = pair.Value;
                    break;
                case "install":
                    download.preSelected = bool.Parse(pair.Value);
                    break;
                case "mandatory":
                    download.required = bool.Parse(pair.Value);
                    break;
                case "hidden":
                    download.hidden = bool.Parse(pair.Value);
                    break;
                case "sync":
                    download.parentModuleId = pair.Value;
                    break;
            }
        }
    }

    /// <summary>
    /// Create a new empty version.
    /// </summary>
    static VersionMetadata CreateEmptyVersion(UnityVersion version, ReleaseStream stream)
    {
        var meta = new VersionMetadata();
        meta.release = new Release();
        meta.release.version = version;
        meta.release.shortRevision = version.hash;

        if (stream == ReleaseStream.None)
            stream = GuessStreamFromVersion(version);
        meta.release.stream = stream;

        return meta;
    }

    /// <summary>
    /// Guess the release stream based on the Unity version.
    /// </summary>
    public static ReleaseStream GuessStreamFromVersion(UnityVersion version)
    {
        if (version.type == UnityVersion.Type.Alpha) {
            return ReleaseStream.Alpha;
        } else if (version.type == UnityVersion.Type.Beta) {
            return ReleaseStream.Beta;
        } else if (version.major >= 2017 && version.major <= 2019 && version.minor == 4) {
            return ReleaseStream.LTS;
        } else if (version.major >= 2020 && version.minor == 3) {
            return ReleaseStream.LTS;
        } else {
            return ReleaseStream.Tech;
        }
    }

    /// <summary>
    /// Guess the release notes URL for a version.
    /// </summary>
    public static string GetReleaseNotesUrl(ReleaseStream stream, UnityVersion version)
    {
        switch (stream) {
            case ReleaseStream.Alpha:
                return UNITY_RELEASE_NOTES_ALPHA + version.ToString(false);
            case ReleaseStream.Beta:
                return UNITY_RELEASE_NOTES_BETA + version.ToString(false);
            default:
                return UNITY_RELEASE_NOTES_FINAL + $"{version.major}.{version.minor}.{version.patch}";
        }
    }
}

}
