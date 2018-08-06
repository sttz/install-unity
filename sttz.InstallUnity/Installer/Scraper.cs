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
    /// Releases JSON used by Unity Hub ({0} should be either win32, darwin or linux).
    /// </summary>
    const string UNITY_HUB_RELEASES = "https://public-cdn.cloud.unity3d.com/hub/prod/releases-{0}.json";

    /// <summary>
    /// HTML archive of Unity releases.
    /// </summary>
    const string UNITY_ARCHIVE = "https://unity3d.com/get-unity/download/archive";

    /// <summary>
    /// HTML archive of Unity beta releases.
    /// </summary>
    const string UNITY_BETA_ARCHIVE = "https://unity3d.com/unity/beta-download";

    // -------- Release Notes --------

    /// <summary>
    /// HTML release notes of final Unity releases (append a version without type or build number, e.g. 2018.2.1)
    /// </summary>
    const string UNITY_RELEASE_NOTES_FINAL = "https://unity3d.com/unity/whats-new/unity-";

    /// <summary>
    /// HTML release notes of beta Unity releases (append a full beta version string)
    /// </summary>
    const string UNITY_RELEASE_NOTES_BETA = "https://unity3d.com/unity/beta/unity";

    /// <summary>
    /// HTML release notes of patch Unity releases (append a full beta version string)
    /// </summary>
    const string UNITY_RELEASE_NOTES_PATCH = "https://unity3d.com/unity/qa/patch-releases/";

    // -------- INIs --------

    /// <summary>
    /// Name of INI file with packages information (replace {0} with version and {1} with osx or win).
    /// </summary>
    const string UNITY_INI_FILENAME = "unity-{0}-{1}.ini";

    // -------- Reular Expressions --------

    /// <summary>
    /// Regex to extract download links from HTML pages.
    /// </summary>
    static readonly Regex UNIT_DOWNLOADS_RE = new Regex(@"(https?:\/\/[\w.-]+unity3d\.com\/[\w\/.-]+\/([0-9a-f]{12})\/)[\w\/.-]+-(\d+\.\d+\.\d+\w\d+)[\w\/.-]+");

    /// <summary>
    /// Regex to extract beta release note pages from beta archive.
    /// </summary>
    static readonly Regex UNITY_BETA_RE = new Regex(@"/unity/beta/unity(\d+\.\d+\.\d+\w\d+)");

    // -------- Scraper --------

    static HttpClient client = new HttpClient();

    ILogger Logger = UnityInstaller.CreateLogger<Scraper>();

    /// <summary>
    /// Load the latest Unity releases, using the same JSON as Unity Hub.
    /// </summary>
    /// <param name="canellation"></param>
    /// <returns>Task returning the discovered versions</returns>
    public async Task<IEnumerable<VersionMetadata>> LoadLatest(CancellationToken cancellation = default)
    {
        string platformName;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            platformName = "darwin";
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            platformName = "win32";
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            platformName = "linux";
        } else {
            throw new NotImplementedException("Scraper.LoadLatest does not support platform: " + RuntimeInformation.OSDescription);
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
            foreach (var version in data["official"]) {
                var metadata = new VersionMetadata();
                metadata.version = new UnityVersion(version.version);

                metadata.packages = new PackageMetadata[version.modules.Length + 1];
                metadata.packages[0] = new PackageMetadata() {
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
                    metadata.packages[i++] = new PackageMetadata() {
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

                Logger.LogDebug($"Found version {metadata.version} with {metadata.packages.Length} packages");
                result.Add(metadata);
            }
        }

        // TODO: Betas

        return result;
    }

    /// <summary>
    /// Load the available versions of the given release type.
    /// </summary>
    /// <param name="canellation"></param>
    /// <returns>Task returning the discovered versions</returns>
    public async Task<IEnumerable<VersionMetadata>> Load(UnityVersion.Type type, CancellationToken cancellation = default)
    {
        string url;
        switch (type) {
            case UnityVersion.Type.Final:
                url = UNITY_ARCHIVE;
                break;
            case UnityVersion.Type.Beta:
                url = UNITY_BETA_ARCHIVE;
                break;
            default:
                throw new NotSupportedException("Discovering not supported for release type: " + type);
        }

        Logger.LogInformation($"Scraping latest releases for {type} from '{url}'");
        var response = await client.GetAsync(url, cancellation);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        Logger.LogDebug($"Got response: {html}");

        if (type == UnityVersion.Type.Final) {
            return ExtractFromHtml(html).Values;
        }

        var matches = UNITY_BETA_RE.Matches(html);
        var results = new Dictionary<UnityVersion, VersionMetadata>();
        foreach (Match match in matches) {
            var version = new UnityVersion(match.Groups[1].Value);
            if (results.ContainsKey(version)) continue;

            var betaUrl = UNITY_RELEASE_NOTES_BETA + version.ToString(false);
            Logger.LogInformation($"Scraping beta {version} from '{betaUrl}'");
            response = await client.GetAsync(betaUrl, cancellation);
            response.EnsureSuccessStatusCode();

            html = await response.Content.ReadAsStringAsync();
            Logger.LogDebug($"Got response: {html}");
            ExtractFromHtml(html, results);
        }
        return results.Values;
    }

    /// <summary>
    /// Extract the versions and the base URLs from the html string.
    /// </summary>
    Dictionary<UnityVersion, VersionMetadata> ExtractFromHtml(string html, Dictionary<UnityVersion, VersionMetadata> results = null)
    {
        var matches = UNIT_DOWNLOADS_RE.Matches(html);
        results = results ?? new Dictionary<UnityVersion, VersionMetadata>();
        foreach (Match match in matches) {
            var version = new UnityVersion(match.Groups[3].Value);
            version.hash = match.Groups[2].Value;
            if (results.ContainsKey(version)) continue;

            results[version] = new VersionMetadata() {
                version = version,
                iniUrl = match.Groups[1].Value
            };
        }
        return results;
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

        Logger.LogInformation($"Trying to scrape guessed release notes url: {url}");
        var response = await client.GetAsync(url, cancellation);
        if (!response.IsSuccessStatusCode) {
            return default;
        }

        var html = await response.Content.ReadAsStringAsync();
        Logger.LogDebug($"Got response: {html}");
        return ExtractFromHtml(html).Values.FirstOrDefault();
    }

    /// <summary>
    /// Load the packages of a Unity version.
    /// The VersionMetadata must have iniUrl set.
    /// </summary>
    /// <param name="metadata">Version metadata with iniUrl.</param>
    /// <returns>A Task returning the metadata with packages filled in.</returns>
    public async Task<VersionMetadata> LoadPackages(VersionMetadata metadata, CancellationToken cancellation = default)
    {
        string platformName;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            platformName = "osx";
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            platformName = "win";
        } else {
            throw new NotImplementedException("Scraper.LoadPackages does not support platform: " + RuntimeInformation.OSDescription);
        }

        if (!metadata.version.IsFullVersion) {
            throw new ArgumentException("Unity version needs to be a full version", nameof(metadata));
        }
        if (string.IsNullOrEmpty(metadata.iniUrl)) {
            throw new ArgumentException("VersionMetadata.iniUrl is not set", nameof(metadata));
        }

        var url = metadata.iniUrl + string.Format(UNITY_INI_FILENAME, metadata.version.ToString(false), platformName);
        Logger.LogInformation($"Loading packages for {metadata.version} and {platformName} from '{url}'");
        var response = await client.GetAsync(url, cancellation);
        response.EnsureSuccessStatusCode();

        var ini = await response.Content.ReadAsStringAsync();
        Logger.LogDebug($"Got response: {ini}");
        
        var parser = new IniDataParser();
        var data = parser.Parse(ini);

        var packages = new PackageMetadata[data.Sections.Count];
        var i = 0;
        foreach (var section in data.Sections) {
            packages[i++] = new PackageMetadata() {
                name = section.SectionName,
                title = section.Keys["title"],
                description = section.Keys["description"],
                url = section.Keys["url"],
                install = bool.Parse(section.Keys["install"]),
                mandatory = bool.Parse(section.Keys["mandatory"]),
                size = long.Parse(section.Keys["size"]),
                installedsize = long.Parse(section.Keys["installedsize"]),
                version = section.Keys["version"],
                hidden = section.Keys["hidden"] != null ? bool.Parse(section.Keys["hidden"]) : false,
                extension = section.Keys["extension"],
                sync = section.Keys["sync"],
                md5 = section.Keys["md5"]
            };
        }

        metadata.packages = packages;
        Logger.LogInformation($"Found {metadata.packages.Length} packages");
        return metadata;
    }

    /// <summary>
    /// Guess the release notes URL for a version.
    /// </summary>
    public string GetReleaseNotesUrl(UnityVersion version)
    {
        switch (version.type) {
            case UnityVersion.Type.Undefined:
            case UnityVersion.Type.Final:
                return UNITY_RELEASE_NOTES_FINAL + version.major + "." + version.minor + "." + version.patch;
            case UnityVersion.Type.Patch:
                return UNITY_RELEASE_NOTES_PATCH + version.ToString(false);
            case UnityVersion.Type.Beta:
                return UNITY_RELEASE_NOTES_BETA + version.ToString(false);
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