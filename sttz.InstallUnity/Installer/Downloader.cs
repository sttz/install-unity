using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace sttz.InstallUnity
{

/// <summary>
/// Helper class to download files.
/// </summary>
public class Downloader
{
    // -------- Settings --------

    /// <summary>
    /// How to handle existing files.
    /// </summary>
    public enum ExistingFile
    {
        /// <summary>
        /// Undefined behaviour, will default to Resume.
        /// </summary>
        Undefined,

        /// <summary>
        /// Always redownload, overwriting existing files.
        /// </summary>
        Redownload,
        /// <summary>
        /// Try to hash and/or resume existing file,
        /// will fall back to redownloading and overwriting.
        /// </summary>
        Resume,
        /// <summary>
        /// Do not hash or touch existing files and complete immediately.
        /// </summary>
        Skip
    }

    /// <summary>
    /// Url of the file to download.
    /// </summary>
    public Uri Url { get; protected set; }

    /// <summary>
    /// Path to download the file to.
    /// </summary>
    public string TargetPath { get; protected set; }

    /// <summary>
    /// Expected size of the file.
    /// </summary>
    public long ExpectedSize { get; protected set; }

    /// <summary>
    /// Expected hash of the file (in WRC SRI format).
    /// </summary>
    public string ExpectedHash { get; protected set; }

    /// <summary>
    /// How to handle existing files.
    /// </summary>
    public ExistingFile Existing = ExistingFile.Resume;

    /// <summary>
    /// Buffer size used when downloading.
    /// </summary>
    public int BufferSize = 524288;

    /// <summary>
    /// How many blocks (of BufferSize or smaller) are used to calculate the transfer speed.
    /// Set to 0 to disable calculating speed.
    /// </summary>
    public int SpeedWindowBlocks = 5000;

    /// <summary>
    /// Time out used for requests (in seconds).
    /// Can only be set before a Downloader instance's first request is made.
    /// </summary>
    public int Timeout = 30;

    // -------- State --------

    /// <summary>
    /// Describing the download's state.
    /// </summary>
    public enum State
    {
        /// <summary>
        /// Call Prepare to initialize instance.
        /// </summary>
        Uninitialized,

        /// <summary>
        /// Waiting to start download.
        /// </summary>
        Idle,

        /// <summary>
        /// Hashing existing file.
        /// </summary>
        Hashing,

        /// <summary>
        /// Downloading file.
        /// </summary>
        Downloading,

        /// <summary>
        /// Error occurred while downloading.
        /// </summary>
        Error,

        /// <summary>
        /// Download complete.
        /// </summary>
        Complete
    }

    /// <summary>
    /// Current state of the downloader.
    /// </summary>
    public State CurrentState { get; protected set; }

    /// <summary>
    /// Bytes processed so far (hashed and/or downloaded).
    /// </summary>
    public long BytesProcessed { get; protected set; }

    /// <summary>
    /// Total number of bytes of file being downloaded.
    /// </summary>
    public long BytesTotal { get; protected set; }

    /// <summary>
    /// Current hashing or download speed.
    /// </summary>
    public double BytesPerSecond { get; protected set; }

    /// <summary>
    /// The hash after the file has been downloaded.
    /// </summary>
    public byte[] Hash { get; protected set; }

    /// <summary>
    /// Event called for every <see cref="BufferSize"/> of data processed.
    /// </summary>
    public event Action<Downloader> OnProgress;

    HttpClient client = new HttpClient();

    ILogger Logger = UnityInstaller.CreateLogger<Downloader>();

    Queue<KeyValuePair<long, int>> blocks;
    System.Diagnostics.Stopwatch watch;

    // -------- Methods --------

    /// <summary>
    /// Prepare a new download.
    /// </summary>
    public void Prepare(Uri url, string path, long expectedSize = -1, string expectedHash = null)
    {
        if (CurrentState == State.Hashing || CurrentState == State.Downloading)
            throw new InvalidOperationException($"A download is already in progress.");

        this.Url = url;
        this.TargetPath = path;
        this.ExpectedSize = expectedSize;
        this.ExpectedHash = expectedHash;

        Reset();
    }

    /// <summary>
    /// Make the Downloader reusable with the same settings.
    /// </summary>
    public void Reset()
    {
        if (CurrentState == State.Hashing || CurrentState == State.Downloading)
            throw new InvalidOperationException($"Cannot call reset when a download is in progress.");

        CurrentState = State.Idle;
        BytesProcessed = 0;
        BytesTotal = ExpectedSize;
        Hash = null;

        if (SpeedWindowBlocks > 0) {
            blocks = new Queue<KeyValuePair<long, int>>(SpeedWindowBlocks);
            watch = new System.Diagnostics.Stopwatch();
        } else {
            blocks = null;
            watch = null;
        }

        if (Existing == ExistingFile.Undefined) {
            Existing = ExistingFile.Resume;
        }
    }

    /// <summary>
    /// Check if <see cref="Hash"/> matches <see cref="ExpectedHash"/>.
    /// </summary>
    public bool CheckHash()
    {
        if (Hash == null) throw new InvalidOperationException("No Hash set.");
        if (string.IsNullOrEmpty(ExpectedHash)) throw new InvalidOperationException("No ExpectedHash set.");

        var hash = SplitSRIHash(ExpectedHash);

        var base64Hash = Convert.ToBase64String(Hash);
        if (string.Equals(hash.value, base64Hash, StringComparison.OrdinalIgnoreCase))
            return true;

        // Unity generates their hashes in a non-standard way
        // W3C SRI specifies the hash to be base64 encoded form the raw hash bytes
        // but Unity takes the hex-encoded string of the hash and base64-encodes that
        var hexBase64Hash = Convert.ToBase64String(Encoding.UTF8.GetBytes(Helpers.ToHexString(Hash)));
        if (string.Equals(hash.value, hexBase64Hash, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Check if an existing file's hash is valid (does not download any data).
    /// </summary>
    public async Task AssertExistingFileHash(CancellationToken cancellation = default)
    {
        if (string.IsNullOrEmpty(ExpectedHash)) throw new InvalidOperationException("No ExpectedHash set.");
        if (!File.Exists(TargetPath)) return;

        var hash = SplitSRIHash(ExpectedHash);
        HashAlgorithm hasher = null;
        if (hash.algorithm != null) {
            hasher = CreateHashAlgorithm(hash.algorithm);
        }

        using (var input = File.Open(TargetPath, FileMode.Open, FileAccess.Read)) {
            CurrentState = State.Hashing;
            await CopyToAsync(input, Stream.Null, hasher, cancellation);
        }
        hasher.TransformFinalBlock(new byte[0], 0, 0);
        Hash = hasher.Hash;

        if (!CheckHash()) {
            throw new Exception($"Existing file '{TargetPath}' does not match expected hash (got {Convert.ToBase64String(Hash)}, expected {hash.value}).");
        }
    }

    /// <summary>
    /// Start the download.
    /// </summary>
    public async Task Start(CancellationToken cancellation = default)
    {
        if (CurrentState != State.Idle)
            throw new InvalidOperationException("A download already in progress or instance not prepared.");

        try {
            HashAlgorithm hasher = null;
            if (!string.IsNullOrEmpty(ExpectedHash)) {
                var hash = SplitSRIHash(ExpectedHash);
                if (hash.algorithm != null) {
                    hasher = CreateHashAlgorithm(hash.algorithm);
                }
            }

            var filename = Path.GetFileName(TargetPath);

            // Check existing file
            var mode = FileMode.Create;
            var startOffset = 0L;
            if (File.Exists(TargetPath)) {
                // Handle existing file from a previous download
                var existing = await HandleExistingFile(hasher, cancellation);
                if (existing.complete) {
                    CurrentState = State.Complete;
                    return;
                } else if (existing.startOffset > 0) {
                    startOffset = existing.startOffset;
                    mode = FileMode.Append;
                } else {
                    startOffset = 0;
                    mode = FileMode.Create;
                }
            }

            // Load headers
            var request = new HttpRequestMessage(HttpMethod.Get, Url);
            if (startOffset != 0) {
                request.Headers.Range = new RangeHeaderValue(startOffset, null);
            }

            if (client.Timeout != TimeSpan.FromSeconds(Timeout))
                client.Timeout = TimeSpan.FromSeconds(Timeout);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) {
                // Disable resuming for next attempt
                Existing = ExistingFile.Redownload;
                throw new Exception($"Failed to resume, disabled resume for '{filename}' (HTTP Code 416)");
            }

            response.EnsureSuccessStatusCode();

            // Redownload whole file if resuming fails
            if (startOffset > 0 && response.StatusCode != HttpStatusCode.PartialContent) {
                Logger.LogInformation("Server does not support resuming download.");
                startOffset = 0;
                mode = FileMode.Create;
            }

            if (hasher != null) {
                hasher.Initialize();

                // When resuming, hash already downloaded data
                if (startOffset > 0) {
                    using (var input = File.Open(TargetPath, FileMode.Open, FileAccess.Read)) {
                        CurrentState = State.Hashing;
                        await CopyToAsync(input, Stream.Null, hasher, cancellation);
                    }
                }
            }

            if (response.Content.Headers.ContentLength != null) {
                BytesTotal = response.Content.Headers.ContentLength.Value + startOffset;
            }

            // Download
            Directory.CreateDirectory(Path.GetDirectoryName(TargetPath));
            using (Stream input = await response.Content.ReadAsStreamAsync(), output = File.Open(TargetPath, mode, FileAccess.Write)) {
                CurrentState = State.Downloading;
                BytesProcessed = startOffset;
                await CopyToAsync(input, output, hasher, cancellation);
            }

            if (hasher != null) {
                hasher.TransformFinalBlock(new byte[0], 0, 0);
                Hash = hasher.Hash;
            }

            if (Hash != null && !string.IsNullOrEmpty(ExpectedHash) && !CheckHash()) {
                if (string.IsNullOrEmpty(ExpectedHash)) {
                    Logger.LogInformation($"Downloaded file '{filename}' with hash {Convert.ToBase64String(Hash)}");
                    CurrentState = State.Complete;
                } else if (CheckHash()) {
                    Logger.LogInformation($"Downloaded file '{filename}' with expected hash {Convert.ToBase64String(Hash)}");
                    CurrentState = State.Complete;
                } else {
                    throw new Exception($"Downloaded file '{filename}' does not match expected hash (got {Convert.ToBase64String(Hash)} but expected {ExpectedHash})");
                }
            } else {
                Logger.LogInformation($"Downloaded file '{filename}'");
                CurrentState = State.Complete;
            }
        } catch {
            CurrentState = State.Error;
            throw;
        }
    }

    async Task<(bool complete, long startOffset)> HandleExistingFile(HashAlgorithm hasher, CancellationToken cancellation)
    {
        if (Existing == ExistingFile.Skip) {
            // Complete without checking or resuming
            return (true, -1);
        }

        var filename = Path.GetFileName(TargetPath);

        if (Existing == ExistingFile.Resume) {
            var hashChecked = false;
            if (!string.IsNullOrEmpty(ExpectedHash) && hasher != null) {
                // If we have a hash, always check against hash first
                using (var input = File.Open(TargetPath, FileMode.Open, FileAccess.Read)) {
                    CurrentState = State.Hashing;
                    await CopyToAsync(input, Stream.Null, hasher, cancellation);
                }
                hasher.TransformFinalBlock(new byte[0], 0, 0);
                Hash = hasher.Hash;

                if (CheckHash()) {
                    Logger.LogInformation($"Existing file '{filename}' has matching hash, skipping...");
                    return (true, -1);
                } else {
                    hashChecked = true;
                }
            }

            if (ExpectedSize > 0) {
                var fileInfo = new FileInfo(TargetPath);
                if (fileInfo.Length >= ExpectedSize && !hashChecked) {
                    // No hash and big enough, Assume file is good
                    Logger.LogInformation($"Existing file '{filename}' cannot be checked for integrity, assuming it's ok...");
                    return (true, -1);

                } else {
                    // File smaller than it should be, try resuming
                    Logger.LogInformation($"Resuming partial download of '{filename}' ({Helpers.FormatSize(fileInfo.Length)} already downloaded)...");
                    return (false, fileInfo.Length);
                }
            }
        }

        // Force redownload from start
        Logger.LogWarning($"Redownloading existing file '{filename}'");
        return (false, 0);
    }

    /// <summary>
    /// Helper method to copy the stream with a progress callback.
    /// </summary>
    async Task CopyToAsync(Stream input, Stream output, HashAlgorithm hasher, CancellationToken cancellation)
    {
        if (blocks != null)  {
            watch.Restart();
            blocks.Clear();
        }

        var buffer = new byte[BufferSize];
        long bytesInWindow = 0;
        while (true) {
            var read = await input.ReadAsync(buffer, 0, buffer.Length, cancellation);
            if (read == 0)
                break;

            await output.WriteAsync(buffer, 0, read, cancellation);
            BytesProcessed += read;

            if (blocks != null) {
                bytesInWindow += read;

                KeyValuePair<long, int> windowStart = default;
                if (blocks.Count > 0) {
                    windowStart = blocks.Peek();
                }

                var windowLength = watch.ElapsedMilliseconds - windowStart.Key;
                BytesPerSecond = bytesInWindow / (windowLength / 1000.0);

                blocks.Enqueue(new KeyValuePair<long, int>(watch.ElapsedMilliseconds, read));
                if (blocks.Count > SpeedWindowBlocks) {
                    bytesInWindow -= blocks.Dequeue().Value;
                }
            }

            if (OnProgress != null) OnProgress(this);
            if (hasher != null) hasher.TransformBlock(buffer, 0, read, null, 0);
        }

        if (blocks != null) {
            watch.Stop();
        }
    }

    /// <summary>
    /// Split a WRC SRI string into hash algorithm and hash value.
    /// </summary>
    (string algorithm, string value) SplitSRIHash(string hash)
    {
        if (string.IsNullOrEmpty(hash))
            return (null, null);

        var firstDash = hash.IndexOf('-');
        if (firstDash < 0) return (null, hash);

        var hashName = hash.Substring(0, firstDash).ToLowerInvariant();
        var hashValue = hash.Substring(firstDash + 1);

        return (hashName, hashValue);
    }

    /// <summary>
    /// Create a hash algorithm instance from a hash name.
    /// </summary>
    HashAlgorithm CreateHashAlgorithm(string hashName)
    {
        switch (hashName) {
            case "md5": return MD5.Create();
            case "sha256": return SHA256.Create();
            case "sha512": return SHA512.Create();
            case "sha384": return SHA384.Create();
        }

        Logger.LogError($"Unsupported hash algorithm: '{hashName}'");
        return null;
    }
}

}
