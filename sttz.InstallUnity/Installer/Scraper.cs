using IniParser.Parser;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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
    const string UNITY_BASE_URL = "https://unity3d.com";

    /// <summary>
    /// Releases JSON used by Unity Hub ({0} should be either win32, darwin or linux).
    /// </summary>
    const string UNITY_HUB_RELEASES = "https://public-cdn.cloud.unity3d.com/hub/prod/releases-{0}.json";

    /// <summary>
    /// HTML archive of Unity releases.
    /// </summary>
    const string UNITY_ARCHIVE = "https://unity3d.com/get-unity/download/archive";

    /// <summary>
    /// Landing page for Unity prereleases.
    /// </summary>
    const string UNITY_PRERELEASES = "https://unity3d.com/unity/beta";

    // -------- Release Notes --------

    /// <summary>
    /// HTML release notes of final Unity releases (append a version without type or build number, e.g. 2018.2.1)
    /// </summary>
    const string UNITY_RELEASE_NOTES_FINAL = "https://unity3d.com/unity/whats-new/";

    /// <summary>
    /// HTML release notes of alpha Unity releases (append a full alpha version string)
    /// </summary>
    const string UNITY_RELEASE_NOTES_ALPHA = "https://unity3d.com/unity/alpha/";

    /// <summary>
    /// HTML release notes of beta Unity releases (append a full beta version string)
    /// </summary>
    const string UNITY_RELEASE_NOTES_BETA = "https://unity3d.com/unity/beta/";

    /// <summary>
    /// HTML release notes of patch Unity releases (append a full beta version string)
    /// </summary>
    const string UNITY_RELEASE_NOTES_PATCH = "https://unity3d.com/unity/qa/patch-releases/";

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
    /// /// Regex to extract available prerelease major versions from landing page.
    /// </summary>
    static readonly Regex UNITY_PRERELEASE_MAJOR_RE = new Regex(@"\/(alpha|beta)\/(\d{4}\.\d[a-f]?)");

    /// <summary>
    /// Regex to extract available prerelease major versions from landing page.
    /// </summary>
    static readonly Regex UNITY_PRERELEASE_RE = new Regex(@"\/unity\/(alpha|beta)\/(\d+\.\d+\.\d+\w\d+)");

    // -------- Scraper --------

    static HttpClient client = new HttpClient();

    ILogger Logger = UnityInstaller.CreateLogger<Scraper>();

    /// <summary>
    /// Load the latest Unity releases, using the same JSON as Unity Hub.
    /// </summary>
    /// <param name="cachePlatform">Name of platform to load the JSON for</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>Task returning the discovered versions</returns>
    public async Task<IEnumerable<VersionMetadata>> LoadLatest(CachePlatform cachePlatform, CancellationToken cancellation = default)
    {
        string platformName;
        switch (cachePlatform) {
            case CachePlatform.macOS:
                platformName = "darwin";
                break;
            case CachePlatform.Windows:
                platformName = "win32";
                break;
            case CachePlatform.Linux:
                platformName = "linux";
                break;
            default:
                throw new NotImplementedException("Invalid platform name: " + cachePlatform);
        }

        var url = string.Format(UNITY_HUB_RELEASES, platformName);
        Logger.LogInformation($"Loading latest releases for {platformName} from '{url}'");
        var response = await client.GetAsync(url, cancellation);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        Logger.LogDebug("Received response: {json}");
        var data = JsonConvert.DeserializeObject<Dictionary<string, HubUnityVersion[]>>(json);

        var result = new List<VersionMetadata>();
        if (!data.ContainsKey("official")) {
            Logger.LogWarning("Unity Hub JSON does not contain expected 'official' array.");
        } else {
            ParseVersions(cachePlatform, data["official"], result);
        }

        if (data.ContainsKey("beta")) {
            ParseVersions(cachePlatform, data["beta"], result);
        }

        return result;
    }

    void ParseVersions(CachePlatform cachePlatform, HubUnityVersion[] versions, List<VersionMetadata> results)
    {
        foreach (var version in versions) {
                var metadata = new VersionMetadata();
                metadata.version = new UnityVersion(version.version);

                var packages = new PackageMetadata[version.modules.Length + 1];
                packages[0] = new PackageMetadata() {
                    name = "Unity ",
                    title = "Unity " + version.version,
                    description = "Unity Editor",
                    url = version.downloadUrl,
                    install = true,
                    mandatory = false,
                    size = long.Parse(version.downloadSize),
                    installedsize = long.Parse(version.installedSize),
                    version = version.version,
                    md5 = version.checksum
                };

                var i = 1;
                foreach (var module in version.modules) {
                    packages[i++] = new PackageMetadata() {
                        name = module.id,
                        title = module.name,
                        description = module.description,
                        url = module.downloadUrl,
                        install = module.selected,
                        mandatory = false,
                        size = long.Parse(module.downloadSize),
                        installedsize = long.Parse(module.installedSize),
                        version = version.version,
                        md5 = module.checksum
                    };
                }

                Logger.LogDebug($"Found version {metadata.version} with {packages.Length} packages");
                metadata.SetPackages(cachePlatform, packages);
                results.Add(metadata);
            }
    }

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

        return ExtractFromHtml(html).Values;
    }

    /// <summary>
    /// Load the available beta and/or alpha versions.
    /// </summary>
    /// <param name="cancellation"></param>
    /// <returns>Task returning the discovered versions</returns>
    public async Task<IEnumerable<VersionMetadata>> LoadPrerelease(bool includeAlpha, IEnumerable<UnityVersion> knownVersions = null, int scrapeDelay = 50, CancellationToken cancellation = default)
    {
        // Load main prereleases page to discover which major versions are available as prerelease
        Logger.LogInformation($"Scraping latest prereleases with includeAlpha={includeAlpha} from '{UNITY_PRERELEASES}'");
        await Task.Delay(scrapeDelay);
        var response = await client.GetAsync(UNITY_PRERELEASES, cancellation);
        if (!response.IsSuccessStatusCode) {
            Logger.LogWarning($"Failed to scrape url '{UNITY_PRERELEASES}' ({response.StatusCode})");
            return Enumerable.Empty<VersionMetadata>();
        }

        var html = await response.Content.ReadAsStringAsync();
        Logger.LogTrace($"Got response: {html}");

        var majorMatches = UNITY_PRERELEASE_MAJOR_RE.Matches(html);
        var visitedMajorVersions = new HashSet<string>();
        var results = new Dictionary<UnityVersion, VersionMetadata>();
        foreach (Match majorMatch in majorMatches) {
            if (!visitedMajorVersions.Add(majorMatch.Groups[2].Value)) continue;

            var isAlpha = majorMatch.Groups[1].Value == "alpha";
            if (isAlpha && !includeAlpha) continue;

            // Load major version's individual prerelease page to get individual versions
            var archiveUrl = UNITY_BASE_URL + majorMatch.Value;
            Logger.LogInformation($"Scraping latest releases for {majorMatch.Groups[2].Value} from '{archiveUrl}'");
            await Task.Delay(scrapeDelay);
            response = await client.GetAsync(archiveUrl, cancellation);
            if (!response.IsSuccessStatusCode) {
                Logger.LogWarning($"Failed to scrape url '{archiveUrl}' ({response.StatusCode})");
                return Enumerable.Empty<VersionMetadata>();
            }

            html = await response.Content.ReadAsStringAsync();
            Logger.LogTrace($"Got response: {html}");

            var versionMatches = UNITY_PRERELEASE_RE.Matches(html);
            foreach (Match versionMatch in versionMatches) {
                var version = new UnityVersion(versionMatch.Groups[2].Value);
                if (results.ContainsKey(version)) continue;
                if (version.type == UnityVersion.Type.Alpha && !includeAlpha) continue;
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
                ExtractFromHtml(html, true, results);
            }
        }
        return results.Values;
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
    Dictionary<UnityVersion, VersionMetadata> ExtractFromHtml(string html, bool prerelease = false, Dictionary<UnityVersion, VersionMetadata> results = null)
    {
        var matches = UNITYHUB_RE.Matches(html);
        results = results ?? new Dictionary<UnityVersion, VersionMetadata>();
        foreach (Match match in matches) {
            var version = new UnityVersion(match.Groups[1].Value);
            version.hash = match.Groups[2].Value;

            VersionMetadata metadata = default;
            if (!results.TryGetValue(version, out metadata)) {
                metadata.version = version;
            }

            metadata.baseUrl = GetIniBaseUrl(version.type) + version.hash + "/";
            metadata.isPrerelease = prerelease;
            results[version] = metadata;
        }

        matches = UNITY_DOWNLOAD_RE.Matches(html);
        foreach (Match match in matches) {
            var version = new UnityVersion(match.Groups[2].Value);
            version.hash = match.Groups[1].Value;

            VersionMetadata metadata = default;
            if (!results.TryGetValue(version, out metadata)) {
                metadata.version = version;
            }

            metadata.baseUrl = GetIniBaseUrl(version.type) + version.hash + "/";
            metadata.isPrerelease = prerelease;
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

        var metadata = new VersionMetadata();
        metadata.version = version;
        metadata.baseUrl = GetIniBaseUrl(version.type) + version.hash + "/";

        return metadata;
    }

    /// <summary>
    /// Try to load the metadata from a version by guessing its release notes URL.
    /// </summary>
    /// <remarks>
    /// The version must include major, minor and patch components.
    /// For patch and beta releases, it must also contain the build component.
    /// If no type is set, final is assumes.
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
        
        var url = GetReleaseNotesUrl(version);
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
        return ExtractFromHtml(html).Values.FirstOrDefault();
    }

    /// <summary>
    /// Load the packages of a Unity version.
    /// The VersionMetadata must have iniUrl set.
    /// </summary>
    /// <param name="metadata">Version metadata with iniUrl.</param>
    /// <param name="cachePlatform">Name of platform to load the packages for</param>
    /// <returns>A Task returning the metadata with packages filled in.</returns>
    public async Task<VersionMetadata> LoadPackages(VersionMetadata metadata, CachePlatform cachePlatform, CancellationToken cancellation = default)
    {
        if (!metadata.version.IsFullVersion) {
            throw new ArgumentException("Unity version needs to be a full version", nameof(metadata));
        }

        string platformName;
        switch (cachePlatform) {
            case CachePlatform.macOS:
                platformName = "osx";
                break;
            case CachePlatform.Windows:
                platformName = "win";
                break;
            case CachePlatform.Linux:
                platformName = "linux";
                break;
            default:
                throw new NotImplementedException("Invalid platform name: " + cachePlatform);
        }

        if (string.IsNullOrEmpty(metadata.baseUrl)) {
            throw new ArgumentException("VersionMetadata.baseUrl is not set for " + metadata.version, nameof(metadata));
        }

        var url = metadata.baseUrl + string.Format(UNITY_INI_FILENAME, metadata.version.ToString(false), platformName);
        Logger.LogInformation($"Loading packages for {metadata.version} and {platformName} from '{url}'");
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

        var packages = new PackageMetadata[data.Sections.Count];
        var i = 0;
        foreach (var section in data.Sections) {
            var meta = new PackageMetadata();
            meta.name = section.SectionName;

            foreach (var pair in section.Keys) {
                switch (pair.KeyName) {
                    case "title":
                        meta.title = pair.Value;
                        break;
                    case "description":
                        meta.description = pair.Value;
                        break;
                    case "url":
                        meta.url = pair.Value;
                        break;
                    case "install":
                        meta.install = bool.Parse(pair.Value);
                        break;
                    case "mandatory":
                        meta.mandatory = bool.Parse(pair.Value);
                        break;
                    case "size":
                        meta.size = long.Parse(pair.Value);
                        break;
                    case "installedsize":
                        meta.installedsize = long.Parse(pair.Value);
                        break;
                    case "version":
                        meta.version = pair.Value;
                        break;
                    case "hidden":
                        meta.hidden = bool.Parse(pair.Value);
                        break;
                    case "extension":
                        meta.extension = pair.Value;
                        break;
                    case "sync":
                        meta.sync = pair.Value;
                        break;
                    case "md5":
                        meta.md5 = pair.Value;
                        break;
                    case "requires_unity":
                        meta.requires_unity = bool.Parse(pair.Value);
                        break;
                    case "appidentifier":
                        meta.appidentifier = pair.Value;
                        break;
                    case "eulamessage":
                        meta.eulamessage = pair.Value;
                        break;
                    case "eulalabel1":
                        meta.eulalabel1 = pair.Value;
                        break;
                    case "eulaurl1":
                        meta.eulaurl1 = pair.Value;
                        break;
                    case "eulalabel2":
                        meta.eulalabel2 = pair.Value;
                        break;
                    case "eulaurl2":
                        meta.eulaurl2 = pair.Value;
                        break;
                    default:
                        Logger.LogDebug($"Unknown ini field {pair.KeyName}: {pair.Value}");
                        break;
                }
            }

            packages[i++] = meta;
        }

        Logger.LogInformation($"Found {packages.Length} packages");
        metadata.SetPackages(cachePlatform, packages);

        return metadata;
    }

    /// <summary>
    /// Guess the release notes URL for a version.
    /// </summary>
    public string GetReleaseNotesUrl(UnityVersion version, bool isPrerelease = false)
    {
        // Release candidates have a final version but are still on the beta page
        if (version.type == UnityVersion.Type.Final && isPrerelease) {
            return UNITY_RELEASE_NOTES_BETA + version.ToString(false);
        }

        switch (version.type) {
            case UnityVersion.Type.Undefined:
            case UnityVersion.Type.Final:
                return UNITY_RELEASE_NOTES_FINAL + version.major + "." + version.minor + "." + version.patch;
            case UnityVersion.Type.Patch:
                return UNITY_RELEASE_NOTES_PATCH + version.ToString(false);
            case UnityVersion.Type.Beta:
                return UNITY_RELEASE_NOTES_BETA + version.ToString(false);
            case UnityVersion.Type.Alpha:
                return UNITY_RELEASE_NOTES_ALPHA + version.ToString(false);
            default:
                return null;
        }
    }

    // -------- Types --------

    // Disable never assigned warning, as the fields are 
    // set dynamically in the JSON deserializer
    #pragma warning disable CS0649

    struct HubUnityVersion
    {
        public string version;
        public bool lts;
        public string downloadUrl;
        public string downloadSize;
        public string installedSize;
        public string checksum;
        public HubUnityModule[] modules;
    }

    struct HubUnityModule
    {
        public string id;
        public string name;
        public string description;
        public string downloadUrl;
        public string destination;
        public string category;
        public string installedSize;
        public string downloadSize;
        public bool visible;
        public bool selected;
        public string checksum;
    }

    #pragma warning restore CS0649

}

}
