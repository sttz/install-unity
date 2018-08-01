using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// Expected hash of the file (computed with <see cref="HashAlgorithm"/>).
    /// </summary>
    public string ExpectedHash { get; protected set; }

    /// <summary>
    /// Try to resume download of partially downloaded files.
    /// </summary>
    public bool Resume = true;

    /// <summary>
    /// Hash algorithm used to compute hash (null = don't compute hash).
    /// </summary>
    public Type HashAlgorithm = typeof(MD5CryptoServiceProvider);

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
    public string Hash { get; protected set; }

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
    }

    /// <summary>
    /// Check if <see cref="Hash"/> matches <see cref="ExpectedHash"/>.
    /// </summary>
    public bool CheckHash()
    {
        if (Hash == null) throw new InvalidOperationException("No Hash set.");
        if (ExpectedHash == null) throw new InvalidOperationException("No ExpectedHash set.");
        return string.Equals(ExpectedHash, Hash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Start the download.
    /// </summary>
    public async Task Start(CancellationToken cancellation = default)
    {
        if (CurrentState != State.Idle)
            throw new InvalidOperationException("A download already in progress or instance not prepared.");

        HashAlgorithm hasher = null;
        if (HashAlgorithm != null) {
            hasher = (HashAlgorithm)Activator.CreateInstance(HashAlgorithm);
        }

        var filename = Path.GetFileName(TargetPath);

        // Check existing file
        var mode = FileMode.Create;
        var startOffset = 0L;
        if (File.Exists(TargetPath) && Resume) {
            // Try to resume existing file
            var fileInfo = new FileInfo(TargetPath);
            if (ExpectedSize > 0 && fileInfo.Length >= ExpectedSize) {
                if (hasher != null) {
                    using (var input = File.Open(TargetPath, FileMode.Open, FileAccess.Read)) {
                        CurrentState = State.Hashing;
                        await CopyToAsync(input, Stream.Null, hasher, cancellation);
                    }
                    hasher.TransformFinalBlock(new byte[0], 0, 0);
                    Hash = Helpers.ToHexString(hasher.Hash);
                    if (ExpectedHash != null) {
                        if (CheckHash()) {
                            Logger.LogInformation($"Existing file '{filename}' has matching hash, skipping...");
                            CurrentState = State.Complete;
                            return;
                        } else {
                            // Hash mismatch, force redownload
                            Logger.LogWarning($"Existing file '{filename}' has different hash: Got {Hash} but expected {ExpectedHash}. Will redownload...");
                            startOffset = 0;
                            mode = FileMode.Create;
                        }
                    } else {
                        Logger.LogInformation($"Existing file '{filename}' has hash {Hash} but we have nothing to check against, assuming it's ok...");
                    }
                } else {
                    // Assume file is good
                    Logger.LogInformation($"Existing file '{filename}' cannot be checked for integrity, assuming it's ok...");
                    CurrentState = State.Complete;
                    return;
                }
            
            } else {
                Logger.LogInformation($"Resuming partial download of '{filename}' ({Helpers.FormatSize(fileInfo.Length)} already downloaded)...");
                startOffset = fileInfo.Length;
                mode = FileMode.Append;
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
            Hash = Helpers.ToHexString(hasher.Hash);
        }

        if (Hash != null && ExpectedHash != null && !CheckHash()) {
            if (ExpectedHash == null) {
                Logger.LogInformation($"Downloaded file '{filename}' with hash {Hash}");
                CurrentState = State.Complete;
            } else if (CheckHash()) {
                Logger.LogInformation($"Downloaded file '{filename}' with expected hash {Hash}");
                CurrentState = State.Complete;
            } else {
                throw new Exception($"Downloaded file '{filename}' does not match expected hash (got {Hash} but expected {ExpectedHash})");
            }
        } else {
            Logger.LogInformation($"Downloaded file '{filename}'");
            CurrentState = State.Complete;
        }
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

                KeyValuePair<long, int> windowStart;
                if (!blocks.TryPeek(out windowStart)) {
                    windowStart = new KeyValuePair<long, int>();
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
}

}