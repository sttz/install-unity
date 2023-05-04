using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace sttz.InstallUnity
{

/// <summary>
/// Client for the official Unity Release API.
/// Providing the latest Unity editor releases and associated packages.
/// https://services.docs.unity.com/release/v1/index.html#tag/Release/operation/getUnityReleases
/// </summary>
public class UnityReleaseAPIClient
{
    // -------- Types --------

    /// <summary>
    /// Different Unity release streams.
    /// </summary>
    [Flags]
    public enum ReleaseStream
    {
        None = 0,

        Alpha = 1<<0,
        Beta  = 1<<1,
        Tech  = 1<<2,
        LTS   = 1<<3,

        PrereleaseMask = (Alpha | Beta),

        All = -1,
    }

    /// <summary>
    /// Platforms the Unity editor runs on.
    /// </summary>
    [Flags]
    public enum Platform
    {
        None,

        Mac_OS = 1<<0,
        Linux = 1<<1,
        Windows = 1<<2,

        All = -1,
    }

    /// <summary>
    /// CPU architectures the Unity editor supports (on some platforms).
    /// </summary>
    [Flags]
    public enum Architecture
    {
        None = 0,

        X86_64 = 1<<10,
        ARM64  = 1<<11,

        All = -1,
    }

    /// <summary>
    /// Different file types of downloads and links.
    /// </summary>
    public enum FileType
    {
        Undefined,

        TEXT,
        TAR_GZ,
        TAR_XZ,
        ZIP,
        PKG,
        EXE,
        PO,
        DMG,
        LZMA,
        LZ4,
        MD,
        PDF
    }

    /// <summary>
    /// Response from the Releases API.
    /// </summary>
    [JsonObject(MemberSerialization.Fields)]
    public class Response
    {
        /// <summary>
        /// Return wether the request was successful.
        /// </summary>
        public bool IsSuccess => ((int)status >= 200 && (int)status <= 299);

        // -------- Response Fields --------

        /// <summary>
        /// Start offset from the returned results.
        /// </summary>
        public int offset;
        /// <summary>
        /// Limit of results returned.
        /// </summary>
        public int limit;
        /// <summary>
        /// Total number of results.
        /// </summary>
        public int total;

        /// <summary>
        /// The release results.
        /// </summary>
        public Release[] results;

        // -------- Error fields --------

        /// <summary>
        /// Error code.
        /// </summary>
        public HttpStatusCode status;
        /// <summary>
        /// Title of the error.
        /// </summary>
        public string title;
        /// <summary>
        /// Error detail description.
        /// </summary>
        public string detail;
    }

    /// <summary>
    /// A specific release of the Unity editor.
    /// </summary>
    [JsonObject(MemberSerialization.Fields)]
    public class Release
    {
        /// <summary>
        /// Version of the editor.
        /// </summary>
        public UnityVersion version;
        /// <summary>
        /// The Git Short Revision of the Unity Release.
        /// </summary>
        public string shortRevision;

        /// <summary>
        /// Date and time of the release.
        /// </summary>
        public DateTime releaseDate;
        /// <summary>
        /// Link to the release notes.
        /// </summary>
        public ReleaseNotes releaseNotes;
        /// <summary>
        /// Stream this release is part of.
        /// </summary>
        public ReleaseStream stream;
        /// <summary>
        /// The SKU family of the Unity Release.
        /// Possible values: CLASSIC or DOTS
        /// </summary>
        public string skuFamily;
        /// <summary>
        /// The indicator for whether the Unity Release is the recommended LTS
        /// </summary>
        public bool recommended;
        /// <summary>
        /// Deep link to open this release in Unity Hub.
        /// </summary>
        public string unityHubDeepLink;

        /// <summary>
        /// Editor downloads of this release.
        /// </summary>
        public List<EditorDownload> downloads;

        /// <summary>
        /// The Third Party Notices of the Unity Release.
        /// </summary>
        public ThirdPartyNotice[] thirdPartyNotices;

        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            // Copy the short revision to the UnityVersion struct
            if (string.IsNullOrEmpty(version.hash) && !string.IsNullOrEmpty(shortRevision)) {
                version.hash = shortRevision;
            }
        }
    }

    /// <summary>
    /// Unity editor release notes.
    /// </summary>
    [JsonObject(MemberSerialization.Fields)]
    public struct ReleaseNotes
    {
        /// <summary>
        /// Url to the release notes.
        /// </summary>
        public string url;
        /// <summary>
        /// Type of the release notes.
        /// (Only seen "MD" so far.)
        /// </summary>
        public FileType type;
    }

    /// <summary>
    /// Third party notices associated with a Unity release.
    /// </summary>
    [JsonObject(MemberSerialization.Fields)]
    public struct ThirdPartyNotice
    {
        /// <summary>
        /// The original file name of the Unity Release Third Party Notice.
        /// </summary>
        public string originalFileName;
        /// <summary>
        /// The URL of the Unity Release Third Party Notice.
        /// </summary>
        public string url;
        /// <summary>
        /// Type of the release notes.
        /// </summary>
        public FileType type;
    }

    /// <summary>
    /// An Unity editor download, including available modules.
    /// </summary>
    [JsonObject(MemberSerialization.Fields)]
    public abstract class Download
    {
        /// <summary>
        /// Url to download.
        /// </summary>
        public string url;
        /// <summary>
        /// Integrity hash (hash prefixed by hash type plus dash, seen md5 and sha384).
        /// </summary>
        public string integrity;
        /// <summary>
        /// Type of download.
        /// (Only seen "DMG", "PKG", "ZIP" and "PO" so far)
        /// </summary>
        public FileType type;
        /// <summary>
        /// Size of the download.
        /// </summary>
        public FileSize downloadSize;
        /// <summary>
        /// Size required on disk.
        /// </summary>
        public FileSize installedSize;

        /// <summary>
        /// ID of the download.
        /// </summary>
        public abstract string Id { get; }
    }

    /// <summary>
    /// Main editor download.
    /// </summary>
    [JsonObject(MemberSerialization.Fields)]
    public class EditorDownload : Download
    {
        /// <summary>
        /// The Id of the editor download pseudo-module.
        /// </summary>
        public const string ModuleId = "unity";

        /// <summary>
        /// Platform of the editor.
        /// </summary>
        public Platform platform;
        /// <summary>
        /// Architecture of the editor.
        /// </summary>
        public Architecture architecture;
        /// <summary>
        /// Available modules for this editor version.
        /// </summary>
        public List<Module> modules;

        /// <summary>
        /// Editor downloads all have the fixed "Unity" ID.
        /// </summary>
        public override string Id => ModuleId;

        /// <summary>
        /// Dictionary of all modules, including sub-modules.
        /// </summary>
        public Dictionary<string, Module> AllModules { get {
            if (_allModules == null) {
                _allModules = new Dictionary<string, Module>(StringComparer.OrdinalIgnoreCase);
                if (modules != null) {
                    foreach (var module in modules) {
                        AddModulesRecursive(module);
                    }
                }
            }
            return _allModules;
        } }
        [NonSerialized] Dictionary<string, Module> _allModules;

        void AddModulesRecursive(Module module)
        {
            if (string.IsNullOrEmpty(module.id)) {
                throw new Exception($"EditorDownload.AllModules: Module is missing ID");
            }

            if (!_allModules.TryAdd(module.id, module)) {
                throw new Exception($"EditorDownload.AllModules: Multiple modules with id '{module.id}'");
            }

            if (module.subModules != null) {
                foreach (var subModule in module.subModules) {
                    if (subModule == null) continue;
                    AddModulesRecursive(subModule);
                }
            }
        }
    }

    /// <summary>
    /// Size description of a download or space required for install.
    /// </summary>
    [JsonObject(MemberSerialization.Fields)]
    public struct FileSize
    {
        /// <summary>
        /// Size value.
        /// </summary>
        public long value;
        /// <summary>
        /// Unit of the value.
        /// Possible vaues: BYTE, KILOBYTE, MEGABYTE, GIGABYTE
        /// (Only seen "BYTE" so far.)
        /// </summary>
        public string unit;

        /// <summary>
        /// Return the size in bytes, converting from the source unit when necessary.
        /// </summary>
        public long GetBytes()
        {
            switch (unit) {
                case "BYTE":
                    return value;
                case "KILOBYTE":
                    return value * 1024;
                case "MEGABYTE":
                    return value * 1024 * 1024;
                case "GIGABYTE":
                    return value * 1024 * 1024 * 1024;
                default:
                    throw new Exception($"FileSize: Unhandled size unit '{unit}'");
            }
        }

        /// <summary>
        /// Create a new instance with the given amount of bytes.
        /// </summary>
        public static FileSize FromBytes(long bytes)
            => new FileSize() { value = bytes, unit = "BYTE" };

        /// <summary>
        /// Create a new instance with the given amount of bytes.
        /// </summary>
        public static FileSize FromMegaBytes(long megaBytes)
            => new FileSize() { value = megaBytes, unit = "MEGABYTE" };
    }

    /// <summary>
    /// A module of an editor.
    /// </summary>
    [JsonObject(MemberSerialization.Fields)]
    public class Module : Download
    {
        /// <summary>
        /// Identifier of the module.
        /// </summary>
        public string id;
        /// <summary>
        /// Slug identifier of the module.
        /// </summary>
        public string slug;
        /// <summary>
        /// Name of the module.
        /// </summary>
        public string name;
        /// <summary>
        /// Description of the module.
        /// </summary>
        public string description;
        /// <summary>
        /// Category type of the module.
        /// </summary>
        public string category;
        /// <summary>
        /// Wether this module is required for its parent module.
        /// </summary>
        public bool required;
        /// <summary>
        /// Wether this module is hidden from the user.
        /// </summary>
        public bool hidden;
        /// <summary>
        /// Wether this module is installed by default.
        /// </summary>
        public bool preSelected;
        /// <summary>
        /// Where to install the module to (can contain the {UNITY_PATH} variable).
        /// </summary>
        public string destination;
        /// <summary>
        /// How to rename the installed directory.
        /// </summary>
        public PathRename extractedPathRename;
        /// <summary>
        /// EULAs the user should accept before installing.
        /// </summary>
        public Eula[] eula;
        /// <summary>
        /// Sub-Modules of this module.
        /// </summary>
        public List<Module> subModules;

        /// <summary>
        /// Modules return their dynamic id.
        /// </summary>
        public override string Id => id;
        /// <summary>
        /// Id of the parent module.
        /// </summary>
        [NonSerialized] public string parentModuleId;
        /// <summary>
        /// The parent module that lists this sub-module (null = part of main editor module).
        /// </summary>
        [NonSerialized] public Module parentModule;
        /// <summary>
        /// Used to track automatically added dependencies.
        /// </summary>
        [NonSerialized] public bool addedAutomatically;

        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            if (subModules != null) {
                // Set ourself as parent module on sub-modules
                foreach (var sub in subModules) {
                    if (sub == null) continue;
                    sub.parentModule = this;
                    sub.parentModuleId = id;
                }
            }
        }
    }

    /// <summary>
    /// EULA of a module.
    /// </summary>
    [JsonObject(MemberSerialization.Fields)]
    public struct Eula
    {
        /// <summary>
        /// URL to the EULA.
        /// </summary>
        public string url;
        /// <summary>
        /// Type of content at the url.
        /// (Only seen "TEXT" so far.)
        /// </summary>
        public FileType type;
        /// <summary>
        /// Label for this EULA.
        /// </summary>
        public string label;
        /// <summary>
        /// Explanation message for the user.
        /// </summary>
        public string message;
    }

    /// <summary>
    /// Path rename instruction.
    /// </summary>
    [JsonObject(MemberSerialization.Fields)]
    public struct PathRename
    {
        /// <summary>
        /// Path to rename from (can contain the {UNITY_PATH} variable).
        /// </summary>
        public string from;
        /// <summary>
        /// Path to rename to (can contain the {UNITY_PATH} variable).
        /// </summary>
        public string to;

        /// <summary>
        /// Wether both a from and to path are set.
        /// </summary>
        public bool IsSet => (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to));
    }

    // -------- API --------

    /// <summary>
    /// Order of the results returned by the API.
    /// </summary>
    [Flags]
    public enum ResultOrder
    {
        /// <summary>
        /// Default order (release date descending).
        /// </summary>
        Default = 0,

        // -------- Sorting Cireteria --------

        /// <summary>
        /// Order by release date.
        /// </summary>
        ReleaseDate = 1<<0,

        // -------- Sorting Order --------

        /// <summary>
        /// Return results in ascending order.
        /// </summary>
        Ascending = 1<<30,
        /// <summary>
        /// Return results in descending order.
        /// </summary>
        Descending = 1<<31,
    }

    /// <summary>
    /// Request parameters of the Unity releases API.
    /// </summary>
    public class RequestParams
    {
        /// <summary>
        /// Version filter, applied as full-text search on the version string.
        /// </summary>
        public string version = null;
        /// <summary>
        /// Unity release streams to load (can set multiple flags in bitmask).
        /// </summary>
        public ReleaseStream stream = ReleaseStream.All;
        /// <summary>
        /// Platforms to load (can set multiple flags in bitmask).
        /// </summary>
        public Platform platform = Platform.All;
        /// <summary>
        /// Architectures to load (can set multiple flags in bitmask).
        /// </summary>
        public Architecture architecture = Architecture.All;

        /// <summary>
        /// How many results to return (1-25).
        /// </summary>
        public int limit = 10;
        /// <summary>
        /// Offset of the first result returned
        /// </summary>
        public int offset = 0;
        /// <summary>
        /// Order of returned results.
        /// </summary>
        public ResultOrder order;
    }

    /// <summary>
    /// Maximum number of requests that can be made per second.
    /// </summary>
    public const int MaxRequestsPerSecond = 10;
    /// <summary>
    /// Maximum number of requests that can be made per 30 minutes.
    /// (Not currently tracked by the client.)
    /// </summary>
    public const int MaxRequestsPerHalfHour = 1000;

    /// <summary>
    /// Send a basic request to the Release API.
    /// </summary>
    public async Task<Response> Send(RequestParams request, CancellationToken cancellation = default)
    {
        var parameters = new List<KeyValuePair<string, string>>();
        parameters.Add(new (nameof(RequestParams.limit), request.limit.ToString("R")));
        parameters.Add(new (nameof(RequestParams.offset), request.offset.ToString("R")));

        if (!string.IsNullOrEmpty(request.version)) {
            parameters.Add(new (nameof(RequestParams.version), request.version));
        }
        if (request.stream != ReleaseStream.All) {
            AddArrayParameters(parameters, nameof(RequestParams.stream), StreamValues, request.stream);
        }
        if (request.platform != Platform.All) {
            AddArrayParameters(parameters, nameof(RequestParams.platform), PlatformValues, request.platform);
        }
        if (request.architecture != Architecture.All) {
            AddArrayParameters(parameters, nameof(RequestParams.architecture), ArchitectureValues, request.architecture);
        }
        if (request.order != ResultOrder.Default) {
            if (request.order.HasFlag(ResultOrder.ReleaseDate)) {
                var dir = (request.order.HasFlag(ResultOrder.Descending) ? "_DESC" : "_ASC");
                parameters.Add(new (nameof(RequestParams.order), "RELEASE_DATE" + dir));
            }
        }

        var query = await new FormUrlEncodedContent(parameters).ReadAsStringAsync(cancellation);
        Logger.LogDebug($"Sending request to Unity Releases API with query '{Endpoint + query}'");

        var timeSinceLastRequest = DateTime.Now - lastRequestTime;
        var minRequestInterval = TimeSpan.FromSeconds(1) / MaxRequestsPerSecond;
        if (timeSinceLastRequest < minRequestInterval) {
            // Delay request to not exceed max requests per second
            await Task.Delay(minRequestInterval - timeSinceLastRequest);
        }

        lastRequestTime = DateTime.Now;
        var response = await client.GetAsync(Endpoint + query, cancellation);

        var json = await response.Content.ReadAsStringAsync(cancellation);
        Logger.LogTrace($"Received response from Unity Releases API ({response.StatusCode}): {json}");
        if (string.IsNullOrEmpty(json)) {
            throw new Exception($"Got empty response from Unity Releases API (code {response.StatusCode})");
        }

        var parsedResponse = JsonConvert.DeserializeObject<Response>(json);
        if (parsedResponse.status == 0) {
            parsedResponse.status = response.StatusCode;
        }

        return parsedResponse;
    }

    /// <summary>
    /// Load all releases for the given request, making multiple
    /// paginated requests to the API.
    /// </summary>
    /// <param name="request">The request to send, the limit and offset fields will be modified</param>
    /// <param name="maxResults">Limit returned results to not make too many requests</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>The results returned from the API</returns>
    public async Task<Release[]> LoadAll(RequestParams request, int maxResults = 200, CancellationToken cancellation = default)
    {
        request.limit = 25;

        int maxTotal = 0, currentOffset = 0;
        Release[] releases = null;
        Response response = null;
        do {
            response = await Send(request, cancellation);
            if (!response.IsSuccess) {
                throw new Exception($"Unity Release API request failed: {response.title} - {response.detail}");
            }

            maxTotal = Math.Min(response.total, maxResults);
            if (releases == null) {
                releases = new Release[maxTotal];
            }

            Array.Copy(response.results, 0, releases, currentOffset, response.results.Length);
            currentOffset += response.results.Length;

            request.offset += response.results.Length;

        } while (currentOffset < maxTotal && response.results.Length > 0);

        return releases;
    }

    /// <summary>
    /// Load all latest releases from the given time period,
    /// making multiple paginated requests to the API.
    /// </summary>
    /// <param name="request">The request to send, the limit, offset and order fields will be modified</param>
    /// <param name="period">The period to load releases from</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>The results returned from the API, can contain releases older than the given period</returns>
    public async Task<IEnumerable<Release>> LoadLatest(RequestParams request, TimeSpan period, CancellationToken cancellation = default)
    {
        request.limit = 25;
        request.order = ResultOrder.ReleaseDate | ResultOrder.Descending;

        var releases = new List<Release>();
        var now = DateTime.Now;
        Response response = null;
        do {
            response = await Send(request, cancellation);
            if (!response.IsSuccess) {
                throw new Exception($"Unity Release API request failed: {response.title} - {response.detail}");
            } else if (response.results.Length == 0) {
                break;
            }

            releases.AddRange(response.results);
            request.offset += response.results.Length;

            var oldestReleaseDate = response.results[^1].releaseDate;
            var releasedSince = now - oldestReleaseDate;
            if (releasedSince > period) {
                break;
            }

        } while (true);

        return releases;
    }

    /// <summary>
    /// Try to find a release based on version string search.
    /// </summary>
    public async Task<Release> FindRelease(UnityVersion version, Platform platform, Architecture architecture, CancellationToken cancellation = default)
    {
        var req = new RequestParams();
        req.limit = 1;
        req.order = ResultOrder.ReleaseDate | ResultOrder.Descending;

        req.platform = platform;
        req.architecture = architecture;

        // Set release stream based on input version
        req.stream = ReleaseStream.Tech | ReleaseStream.LTS;
        if (version.type == UnityVersion.Type.Beta) req.stream |= ReleaseStream.Beta;
        if (version.type == UnityVersion.Type.Alpha) req.stream |= ReleaseStream.Beta | ReleaseStream.Alpha;

        // Only add version if not just release type
        if (version.major >= 0) {
            // Build up version for a sub-string search (e.g. 2022b won't return any results)
            var searchString = version.major.ToString();
            if (version.minor >= 0) {
                searchString += "." + version.minor;
                if (version.patch >= 0) {
                    searchString += "." + version.patch;
                    if (version.type != UnityVersion.Type.Undefined) {
                        searchString += (char)version.type;
                        if (version.build >= 0) {
                            searchString += version.build;
                        }
                    }
                }
            }
            req.version = searchString;
        }

        var result = await Send(req, cancellation);
        if (!result.IsSuccess) {
            throw new Exception($"Unity Release API request failed: {result.title} - {result.detail}");
        } else if (result.results.Length == 0) {
            return null;
        }

        return result.results[0];
    }

    // -------- Implementation --------

    ILogger Logger = UnityInstaller.CreateLogger<UnityReleaseAPIClient>();

    static HttpClient client = new HttpClient();
    static DateTime lastRequestTime = DateTime.MinValue;

    /// <summary>
    /// Endpoint of the releases API.
    /// </summary>
    const string Endpoint = "https://services.api.unity.com/unity/editor/release/v1/releases?";

    /// <summary>
    /// Query string values for streams.
    /// </summary>
    static readonly Dictionary<ReleaseStream, string> StreamValues = new() {
        { ReleaseStream.Alpha, "ALPHA" },
        { ReleaseStream.Beta,  "BETA" },
        { ReleaseStream.Tech,  "TECH" },
        { ReleaseStream.LTS,   "LTS" },
    };
    /// <summary>
    /// Query string values for platforms.
    /// </summary>
    static readonly Dictionary<Platform, string> PlatformValues = new() {
        { Platform.Mac_OS,   "MAC_OS" },
        { Platform.Linux,   "LINUX" },
        { Platform.Windows, "WINDOWS" },
    };
    /// <summary>
    /// Query string values for architectures.
    /// </summary>
    static readonly Dictionary<Architecture, string> ArchitectureValues = new() {
        { Architecture.X86_64, "X86_64" },
        { Architecture.ARM64,  "ARM64" },
    };

    /// <summary>
    /// Iterate all the single bits set in the given enum value.
    /// (This does not check if the set bit is defined in the enum.)
    /// </summary>
    static IEnumerable<T> IterateBits<T>(T value)
        where T : struct, System.Enum
    {
        var number = (int)(object)value;
        for (int i = 0; i < 32; i++) {
            var flag = 1 << i;
            if ((number & flag) != 0)
                yield return (T)(object)flag;
        }
    }

    /// <summary>
    /// Check the given bitmask enum for single set bits, look up those values
    /// in the given dictionary and then add them to the query.
    /// </summary>
    static void AddArrayParameters<T>(List<KeyValuePair<string, string>> query, string name, Dictionary<T, string> values, T bitmask)
        where T : struct, System.Enum
    {
        foreach (var flag in IterateBits(bitmask)) {
            if (!values.TryGetValue(flag, out var value)) {
                // ERROR: Value not found
                continue;
            }
            query.Add(new (name, value));
        }
    }
}

}
