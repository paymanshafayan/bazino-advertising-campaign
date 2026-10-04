using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using BazinoMarketing.Core.Secrets;
using BazinoMarketing.Core.Settings;
using BazinoMarketing.Core.Tools;

namespace BazinoMarketing.Core.Media;

public sealed record MediaFileEntry(
    string RelativePath,
    string Name,
    string Extension,
    long SizeBytes,
    DateTimeOffset ModifiedUtc)
{
    public bool IsVideo => Extension is ".mp4" or ".mov" or ".m4v" or ".mkv" or ".webm" or ".avi" or ".mpg" or ".mpeg";
    public bool IsAudio => Extension is ".mp3" or ".wav" or ".m4a" or ".aac" or ".flac" or ".ogg";
    public bool IsMedia => IsVideo || IsAudio || Extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif";
}

public sealed record MediaProbeResult(double? DurationSeconds, int? Width, int? Height, string Format);

public sealed record MediaActionResult(
    bool Ok,
    string Code,
    string Message,
    string Folder,
    string? RelativePath = null,
    string? Url = null,
    IReadOnlyList<MediaFileEntry>? Files = null,
    long? DurationMs = null,
    string? Transcript = null,
    string? TranscriptLanguage = null,
    string? TranscriptProvider = null,
    string? TranscriptUrl = null);

public sealed record MediaTranscriptionResult(
    bool Ok,
    string Code,
    string Message,
    string Folder,
    string? RelativePath = null,
    string? Provider = null,
    string? Model = null,
    string? Language = null,
    double? DurationSeconds = null,
    string? Text = null,
    IReadOnlyList<TranscriptSegment>? Segments = null,
    string? TranscriptRelativePath = null,
    string? TextRelativePath = null,
    string? UploadedUrl = null,
    long? DurationMs = null);

/// <summary>
/// Local media operations used by the WPF studio and the app mailbox. The only downloaded Instagram content is a single
/// post URL supplied by the owner/agent. Files and FFmpeg output stay under the selected media folder.
/// </summary>
public sealed class MediaService
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".mkv", ".webm", ".avi", ".mpg", ".mpeg",
        ".jpg", ".jpeg", ".png", ".webp", ".gif",
        ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg"
    };

    private readonly Func<AppSettings> _getSettings;
    private readonly ISecretStore _secrets;
    private readonly string _fallbackFolder;

    public MediaService(Func<AppSettings> getSettings, ISecretStore secrets, string fallbackFolder)
    {
        _getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _fallbackFolder = Path.GetFullPath(fallbackFolder ?? throw new ArgumentNullException(nameof(fallbackFolder)));
    }

    public string CurrentFolder
    {
        get
        {
            var configured = _getSettings().Media?.OutputFolder;
            return !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
                ? Path.GetFullPath(configured)
                : _fallbackFolder;
        }
    }

    public string FfmpegPath => FindFfmpeg() ?? "";

    /// <summary>Stores the selected folder only after proving the app can read and write it.</summary>
    public string ConfirmFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("پوشه‌ای انتخاب نشده است.", nameof(path));
        var fullPath = Path.GetFullPath(path.Trim());
        Directory.CreateDirectory(fullPath);
        var testPath = Path.Combine(fullPath, ".bazino-access-check-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            _ = Directory.EnumerateFileSystemEntries(fullPath).Take(1).ToArray();
            using (var stream = new FileStream(testPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                stream.WriteByte(1);
                stream.Flush();
                stream.Position = 0;
                _ = stream.ReadByte();
            }
            File.Delete(testPath);
        }
        catch
        {
            try { if (File.Exists(testPath)) File.Delete(testPath); } catch { }
            throw new UnauthorizedAccessException("برنامه اجازهٔ خواندن و نوشتن در این پوشه را ندارد.");
        }

        _getSettings().Media.OutputFolder = fullPath;
        return fullPath;
    }

    public IReadOnlyList<MediaFileEntry> ListFiles(int maximum = 250)
    {
        var root = CurrentFolder;
        if (!Directory.Exists(root)) return Array.Empty<MediaFileEntry>();
        maximum = Math.Clamp(maximum, 1, 1000);
        var result = new List<MediaFileEntry>();
        var pending = new Stack<string>();
        pending.Push(root);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        while (pending.Count > 0 && result.Count < maximum)
        {
            var folder = pending.Pop();
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(folder))
                {
                    try
                    {
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                            pending.Push(directory);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }

                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    if (result.Count >= maximum) break;
                    try
                    {
                        var attributes = File.GetAttributes(file);
                        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) continue;
                        var extension = Path.GetExtension(file).ToLowerInvariant();
                        var info = new FileInfo(file);
                        var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                        if (relative == ".." || relative.StartsWith("../", comparison)) continue;
                        result.Add(new MediaFileEntry(relative, info.Name, extension, info.Length, info.LastWriteTimeUtc));
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return result.OrderByDescending(f => f.ModifiedUtc).ToArray();
    }

    public async Task<MediaActionResult> DownloadInstagramAsync(string? url, CancellationToken ct = default)
    {
        var root = CurrentFolder;
        if (!InstagramPostUrl.TryGetShortcode(url, out var shortcode))
            return new(false, "instagram_post_url_required", "نشانی باید به یک پست یا ریل مشخص Instagram اشاره کند؛ حساب یا صفحهٔ کامل پذیرفته نیست.", root);

        try
        {
            Directory.CreateDirectory(root);
            var jobFolderName = $"instagram_{shortcode}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
            var jobFolder = Path.Combine(root, jobFolderName);
            Directory.CreateDirectory(jobFolder);
            var run = await InstaloaderDownloader.DownloadAsync(shortcode, jobFolder, ct).ConfigureAwait(false);
            if (!run.Ok)
                return new(false, run.Code, run.Message, root, jobFolderName, Files: ListDirectory(jobFolder), DurationMs: run.DurationMs);

            var files = ListDirectory(jobFolder);
            if (files.Count == 0)
                return new(false, "instagram_no_media", "Instaloader پایان یافت اما فایل عکس یا ویدئوی کامل در پوشه پیدا نشد.", root, jobFolderName, Files: files, DurationMs: run.DurationMs);

            return new(true, "ok", $"یک پست دریافت شد: {files.Count} فایل رسانه‌ای.", root, jobFolderName, Files: files, DurationMs: run.DurationMs);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(false, "instagram_download_failed", "دریافت پست ممکن نشد: " + ex.Message, root);
        }
    }

    public async Task<MediaProbeResult?> ProbeAsync(string relativePath, CancellationToken ct = default)
    {
        var input = ResolveFile(relativePath);
        var ffprobe = FindFfprobe();
        if (ffprobe is null) return null;
        var run = await RunProcessAsync(ffprobe,
            new[] { "-v", "error", "-show_entries", "format=duration,format_name:stream=codec_type,width,height", "-of", "json", input },
            TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        if (run.TimedOut || run.ExitCode != 0) return null;

        try
        {
            using var doc = JsonDocument.Parse(run.Stdout);
            var root = doc.RootElement;
            double? duration = null;
            string format = "";
            int? width = null, height = null;
            if (root.TryGetProperty("format", out var formatNode))
            {
                if (formatNode.TryGetProperty("duration", out var d) && double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                    duration = seconds;
                if (formatNode.TryGetProperty("format_name", out var f)) format = f.GetString() ?? "";
            }
            if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
            {
                var video = streams.EnumerateArray().FirstOrDefault(s => s.TryGetProperty("codec_type", out var type) && type.GetString() == "video");
                if (video.ValueKind == JsonValueKind.Object)
                {
                    if (video.TryGetProperty("width", out var w) && w.TryGetInt32(out var wi)) width = wi;
                    if (video.TryGetProperty("height", out var h) && h.TryGetInt32(out var he)) height = he;
                }
            }
            return new(duration, width, height, format);
        }
        catch (JsonException) { return null; }
    }

    public async Task<MediaActionResult> CreatePreviewAsync(string relativePath, double seconds = 1, CancellationToken ct = default)
    {
        var root = CurrentFolder;
        var input = ResolveFile(relativePath);
        if (!IsVideoPath(input)) return new(false, "video_required", "پیش‌نمایش فقط برای فایل ویدئویی ساخته می‌شود.", root);
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > 86400)
            return new(false, "invalid_time", "زمان پیش‌نمایش معتبر نیست.", root);
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null) return MissingFfmpeg(root);

        var previewFolder = Path.Combine(root, "previews");
        Directory.CreateDirectory(previewFolder);
        var outputName = ShortStem(input) + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8] + ".jpg";
        var output = Path.Combine(previewFolder, outputName);
        var watch = Stopwatch.StartNew();
        var run = await RunProcessAsync(ffmpeg,
            new[] { "-hide_banner", "-loglevel", "error", "-y", "-ss", F(seconds), "-i", input, "-frames:v", "1", "-q:v", "3", output },
            TimeSpan.FromMinutes(2), ct).ConfigureAwait(false);
        watch.Stop();
        if (!run.Ok || !File.Exists(output) || new FileInfo(output).Length == 0)
            return new(false, run.TimedOut ? "ffmpeg_timeout" : "ffmpeg_failed", "ساخت پیش‌نمایش ممکن نشد. مسیر FFmpeg و فایل انتخاب‌شده را بررسی کنید.", root, DurationMs: watch.ElapsedMilliseconds);
        return new(true, "ok", "یک تصویر پیش‌نمایش ساخته شد.", root, Path.GetRelativePath(root, output).Replace(Path.DirectorySeparatorChar, '/'), DurationMs: watch.ElapsedMilliseconds);
    }

    public async Task<MediaActionResult> TrimAsync(string relativePath, double startSeconds, double endSeconds, CancellationToken ct = default)
    {
        var root = CurrentFolder;
        var input = ResolveFile(relativePath);
        if (!IsVideoPath(input)) return new(false, "video_required", "برش فقط برای فایل ویدئویی انجام می‌شود.", root);
        if (!double.IsFinite(startSeconds) || !double.IsFinite(endSeconds) || startSeconds < 0 || endSeconds <= startSeconds || endSeconds > 86400)
            return new(false, "invalid_range", "بازهٔ برش باید از صفر شروع شود و زمان پایان از زمان آغاز بیشتر باشد.", root);
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null) return MissingFfmpeg(root);

        var output = NewEditedPath(input, ".mp4", "trimmed");
        var duration = endSeconds - startSeconds;
        var watch = Stopwatch.StartNew();
        var args = new[]
        {
            "-hide_banner", "-loglevel", "error", "-y", "-ss", F(startSeconds), "-i", input, "-t", F(duration),
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-c:a", "aac", "-movflags", "+faststart", output
        };
        var run = await RunProcessAsync(ffmpeg, args, TimeSpan.FromMinutes(20), ct).ConfigureAwait(false);
        watch.Stop();
        if (!run.Ok || !File.Exists(output) || new FileInfo(output).Length == 0)
            return new(false, run.TimedOut ? "ffmpeg_timeout" : "ffmpeg_failed", "برش ویدئو انجام نشد. FFmpeg ممکن است نوع فایل را پشتیبانی نکند.", root, DurationMs: watch.ElapsedMilliseconds);
        var relative = Path.GetRelativePath(root, output).Replace(Path.DirectorySeparatorChar, '/');
        return new(true, "ok", "نسخهٔ برش‌خورده ساخته شد.", root, relative, Files: ListDirectory(Path.GetDirectoryName(output)!), DurationMs: watch.ElapsedMilliseconds);
    }

    public async Task<MediaActionResult> ConvertAsync(string relativePath, string format, CancellationToken ct = default)
    {
        var root = CurrentFolder;
        var input = ResolveFile(relativePath);
        if (!MediaExtensions.Contains(Path.GetExtension(input)))
            return new(false, "media_required", "قالب خروجی فقط برای فایل رسانه‌ای پشتیبانی‌شده است.", root);
        format = (format ?? "").Trim().TrimStart('.').ToLowerInvariant();
        if (format is not ("mp4" or "webm" or "mp3"))
            return new(false, "unsupported_format", "قالب خروجی باید mp4، webm یا mp3 باشد.", root);
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null) return MissingFfmpeg(root);

        var output = NewEditedPath(input, "." + format, "converted");
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", input };
        switch (format)
        {
            case "mp4": args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-c:a", "aac", "-movflags", "+faststart" }); break;
            case "webm": args.AddRange(new[] { "-c:v", "libvpx-vp9", "-deadline", "realtime", "-cpu-used", "5", "-c:a", "libopus" }); break;
            case "mp3": args.AddRange(new[] { "-vn", "-c:a", "libmp3lame", "-q:a", "2" }); break;
        }
        args.Add(output);
        var watch = Stopwatch.StartNew();
        var run = await RunProcessAsync(ffmpeg, args, TimeSpan.FromMinutes(20), ct).ConfigureAwait(false);
        watch.Stop();
        if (!run.Ok || !File.Exists(output) || new FileInfo(output).Length == 0)
            return new(false, run.TimedOut ? "ffmpeg_timeout" : "ffmpeg_failed", "تبدیل فایل انجام نشد. FFmpeg ممکن است نوع فایل را پشتیبانی نکند.", root, DurationMs: watch.ElapsedMilliseconds);
        var relative = Path.GetRelativePath(root, output).Replace(Path.DirectorySeparatorChar, '/');
        return new(true, "ok", "نسخهٔ تبدیل‌شده ساخته شد.", root, relative, Files: ListDirectory(Path.GetDirectoryName(output)!), DurationMs: watch.ElapsedMilliseconds);
    }

    public async Task<MediaActionResult> UploadToGitHubAsync(string relativePath, CancellationToken ct = default)
    {
        var root = CurrentFolder;
        string file;
        try { file = ResolveFile(relativePath); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new(false, "file_not_found", "فایل انتخاب‌شده در پوشهٔ رسانه پیدا نشد.", root);
        }

        var info = new FileInfo(file);
        if (info.Length == 0) return new(false, "empty_file", "فایل خالی است و ارسال نشد.", root);
        if (info.Length > GitHubClient.MaxDirectUploadBytes)
            return new(false, "file_too_large", "حجم فایل از سقف ۵۰ مگابایتی ارسال مستقیم به مخزن گیت‌هاب بیشتر است؛ ابتدا ویدئو را با بخش برش یا تبدیل کوچک‌تر کنید.", root);
        if (!_secrets.TryGet(SecretKeys.GitHubToken, out var token) || string.IsNullOrWhiteSpace(token))
            return new(false, "github_not_connected", "اتصال موجود GitHub در برنامه پیدا نشد.", root);

        try
        {
            var settings = _getSettings().GitHub;
            var shortName = ShortStem(file) + Path.GetExtension(file);
            var uniqueName = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" +
                             Guid.NewGuid().ToString("N")[..8] + "_" + shortName;
            var remotePath = "agent-media-inbox/" + uniqueName;
            var bytes = await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false);
            var uploaded = await GitHubClient.UploadContentAsync(settings, token, remotePath, bytes, ct).ConfigureAwait(false);

            if (IsVideoPath(file))
                await TryUploadVideoStoryboardAsync(settings, token, file, remotePath + "_frames.jpg", ct).ConfigureAwait(false);

            string? transcriptText = null, transcriptLang = null, transcriptProvider = null, transcriptUrl = null;
            if (IsVideoPath(file) || IsAudioPath(file))
            {
                try
                {
                    var rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                    var tr = await TranscribeAsync(rel, uploadToGitHub: true, remotePrefix: remotePath, ct: ct).ConfigureAwait(false);
                    if (tr.Ok)
                    {
                        transcriptText = tr.Text;
                        transcriptLang = tr.Language;
                        transcriptProvider = tr.Provider;
                        transcriptUrl = tr.UploadedUrl;
                    }
                }
                catch
                {
                    // Best-effort so video upload still succeeds even if speech transcription is unconfigured or offline.
                }
            }

            return new(true, "ok", "فایل به پوشهٔ بررسی گیت‌هاب فرستاده شد.", root,
                Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'), uploaded.HtmlUrl,
                DurationMs: uploaded.DurationMs,
                Transcript: transcriptText,
                TranscriptLanguage: transcriptLang,
                TranscriptProvider: transcriptProvider,
                TranscriptUrl: transcriptUrl);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            return new(false, "github_upload_timeout", "مهلت یا ارتباط ارسال فایل به GitHub تمام شد.", root);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or JsonException or ArgumentException or FormatException)
        {
            return new(false, "github_upload_failed", "ارسال فایل به گیت‌هاب انجام نشد: " + ex.Message, root);
        }
    }

    public async Task<MediaTranscriptionResult> TranscribeAsync(
        string? relativePath = null,
        string? language = null,
        string? prompt = null,
        bool translateToEnglish = false,
        bool uploadToGitHub = true,
        string? remotePrefix = null,
        CancellationToken ct = default)
    {
        var root = CurrentFolder;
        var watch = Stopwatch.StartNew();
        var targetRel = (relativePath ?? "").Trim();
        if (targetRel.Length == 0 || string.Equals(targetRel, "latest", StringComparison.OrdinalIgnoreCase))
        {
            var latest = ListFiles(250).FirstOrDefault(f => f.IsVideo || f.IsAudio);
            if (latest is null)
                return new(false, "no_media_file", "هیچ فایل ویدئویی یا صوتی در پوشهٔ رسانه پیدا نشد.", root);
            targetRel = latest.RelativePath;
        }

        string inputFile;
        try { inputFile = ResolveFile(targetRel); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new(false, "file_not_found", "فایل انتخاب‌شده در پوشهٔ رسانه پیدا نشد.", root, targetRel);
        }

        if (!IsVideoPath(inputFile) && !IsAudioPath(inputFile))
            return new(false, "audio_or_video_required", "پیاده‌سازی گفتار فقط برای فایل‌های ویدئویی یا صوتی انجام می‌شود.", root, targetRel);

        var tempMp3 = Path.Combine(Path.GetTempPath(), "bazino-stt-" + Guid.NewGuid().ToString("N") + ".mp3");
        byte[] audioBytes;
        string audioFileName;
        try
        {
            var ffmpeg = FindFfmpeg();
            if (ffmpeg is not null)
            {
                var args = new[]
                {
                    "-hide_banner", "-loglevel", "error", "-y", "-i", inputFile,
                    "-vn", "-ar", "16000", "-ac", "1", "-c:a", "libmp3lame", "-q:a", "5", tempMp3
                };
                var run = await RunProcessAsync(ffmpeg, args, TimeSpan.FromMinutes(3), ct).ConfigureAwait(false);
                if (!run.Ok || !File.Exists(tempMp3) || new FileInfo(tempMp3).Length == 0)
                    return new(false, run.TimedOut ? "ffmpeg_timeout" : "ffmpeg_audio_failed",
                        "استخراج صدای فایل با FFmpeg انجام نشد (ممکن است ویدئو فاقد کانال صوتی باشد).", root, targetRel, DurationMs: watch.ElapsedMilliseconds);
                audioBytes = await File.ReadAllBytesAsync(tempMp3, ct).ConfigureAwait(false);
                audioFileName = ShortStem(inputFile) + ".mp3";
            }
            else
            {
                var info = new FileInfo(inputFile);
                if (info.Length == 0 || info.Length > SpeechTranscriber.MaxAudioBytes)
                    return new(false, "ffmpeg_not_found", "FFmpeg برای فشرده‌سازی صدای این فایل پیدا نشد و حجم فایل بیش از سقف مستقیم است.", root, targetRel);
                audioBytes = await File.ReadAllBytesAsync(inputFile, ct).ConfigureAwait(false);
                audioFileName = ShortStem(inputFile) + Path.GetExtension(inputFile);
            }
        }
        finally
        {
            try { if (File.Exists(tempMp3)) File.Delete(tempMp3); } catch { }
        }

        var settings = _getSettings();
        var stt = await SpeechTranscriber.TranscribeAsync(settings, _secrets, audioBytes, audioFileName, language, prompt, translateToEnglish, ct).ConfigureAwait(false);
        if (!stt.Ok)
            return new(false, stt.Code, stt.Message, root, targetRel, stt.Provider, stt.Model, DurationMs: watch.ElapsedMilliseconds);

        var relNormalized = Path.GetRelativePath(root, inputFile).Replace(Path.DirectorySeparatorChar, '/');
        string? jsonRel = null, txtRel = null, uploadedUrl = null;
        try
        {
            var transcriptsDir = Path.Combine(root, "transcripts");
            Directory.CreateDirectory(transcriptsDir);
            var baseName = ShortStem(inputFile, 28) + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
            var jsonPath = Path.Combine(transcriptsDir, baseName + ".json");
            var txtPath = Path.Combine(transcriptsDir, baseName + ".txt");

            var docObj = new
            {
                file = relNormalized,
                provider = stt.Provider,
                model = stt.Model,
                language = stt.Language,
                durationSeconds = stt.DurationSeconds,
                translatedToEnglish = translateToEnglish,
                text = stt.Text,
                segments = stt.Segments,
                createdAtUtc = DateTimeOffset.UtcNow
            };
            var jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(docObj, JsonUtil.Options));
            await File.WriteAllBytesAsync(jsonPath, jsonBytes, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(txtPath, SpeechTranscriber.FormatReadableTranscript(stt, relNormalized), Encoding.UTF8, ct).ConfigureAwait(false);

            jsonRel = Path.GetRelativePath(root, jsonPath).Replace(Path.DirectorySeparatorChar, '/');
            txtRel = Path.GetRelativePath(root, txtPath).Replace(Path.DirectorySeparatorChar, '/');

            if (uploadToGitHub && _secrets.TryGet(SecretKeys.GitHubToken, out var ghToken) && !string.IsNullOrWhiteSpace(ghToken))
            {
                var remoteTranscriptPath = !string.IsNullOrWhiteSpace(remotePrefix)
                    ? remotePrefix + "_transcript.json"
                    : "agent-media-inbox/" + baseName + "_transcript.json";
                var uploaded = await GitHubClient.UploadContentAsync(settings.GitHub, ghToken, remoteTranscriptPath, jsonBytes, ct).ConfigureAwait(false);
                uploadedUrl = uploaded.HtmlUrl;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the transcription result even if saving/uploading the sidecar file encountered an error.
        }

        watch.Stop();
        return new(
            true,
            "ok",
            stt.Message,
            root,
            relNormalized,
            stt.Provider,
            stt.Model,
            stt.Language,
            stt.DurationSeconds,
            stt.Text,
            stt.Segments,
            jsonRel,
            txtRel,
            uploadedUrl,
            watch.ElapsedMilliseconds);
    }

    private async Task TryUploadVideoStoryboardAsync(
        GitHubSettings settings, string token, string videoPath, string storyboardRemotePath, CancellationToken ct)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null) return;

        var tempJpg = Path.Combine(Path.GetTempPath(), "bazino-storyboard-" + Guid.NewGuid().ToString("N") + ".jpg");
        try
        {
            var args = new[]
            {
                "-hide_banner", "-loglevel", "error", "-y", "-i", videoPath,
                "-vf", "fps=1/2,scale=360:-1,tile=4x1", "-frames:v", "1", "-update", "1", "-q:v", "3", tempJpg
            };
            var run = await RunProcessAsync(ffmpeg, args, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            if (!run.Ok || !File.Exists(tempJpg)) return;
            var jpgInfo = new FileInfo(tempJpg);
            if (jpgInfo.Length == 0 || jpgInfo.Length > GitHubClient.MaxDirectUploadBytes) return;
            var jpgBytes = await File.ReadAllBytesAsync(tempJpg, ct).ConfigureAwait(false);
            _ = await GitHubClient.UploadContentAsync(settings, token, storyboardRemotePath, jpgBytes, ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort helper so the agent always gets a visual contact sheet alongside uploaded videos.
        }
        finally
        {
            try { if (File.Exists(tempJpg)) File.Delete(tempJpg); } catch { }
        }
    }

    private IReadOnlyList<MediaFileEntry> ListDirectory(string folder)
    {
        if (!Directory.Exists(folder)) return Array.Empty<MediaFileEntry>();
        var root = CurrentFolder;
        var prefix = Path.GetFullPath(folder);
        var result = new List<MediaFileEntry>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(prefix, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (!MediaExtensions.Contains(ext)) continue;
                    var info = new FileInfo(file);
                    var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                    result.Add(new MediaFileEntry(relative, info.Name, ext, info.Length, info.LastWriteTimeUtc));
                }
                catch (IOException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return result.OrderByDescending(f => f.ModifiedUtc).Take(250).ToArray();
    }

    private string ResolveFile(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new ArgumentException("باید نام نسبی یک فایل در پوشهٔ رسانه انتخاب شود.", nameof(relativePath));
        var root = Path.GetFullPath(CurrentFolder);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rel = Path.GetRelativePath(root, candidate);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (rel == ".." || rel.StartsWith(".." + Path.DirectorySeparatorChar, comparison) || Path.IsPathRooted(rel))
            throw new ArgumentException("مسیر فایل باید داخل پوشهٔ رسانه باشد.", nameof(relativePath));
        if (!File.Exists(candidate)) throw new FileNotFoundException("فایل پیدا نشد.", candidate);
        if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("فایل میان‌بر یا پیوندی پذیرفته نمی‌شود.");
        return candidate;
    }

    private static bool IsVideoPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mov" or ".m4v" or ".mkv" or ".webm" or ".avi" or ".mpg" or ".mpeg";

    private static bool IsAudioPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".mp3" or ".wav" or ".m4a" or ".aac" or ".flac" or ".ogg";

    private static string ShortStem(string path, int maxChars = 32)
    {
        var stem = Path.GetFileNameWithoutExtension(path) ?? "media";
        if (stem.Length <= maxChars) return stem;
        return stem[..maxChars];
    }

    private static string NewEditedPath(string input, string extension, string label)
    {
        var folder = Path.Combine(Path.GetDirectoryName(input) ?? "", "edited");
        Directory.CreateDirectory(folder);
        var stem = ShortStem(input);
        var suffix = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        var name = $"{stem}_{label}_{suffix}{extension}";
        return Path.Combine(folder, name);
    }

    private string? FindFfmpeg()
    {
        var configured = _getSettings().Media?.FfmpegPath?.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);
        return FindOnPath(OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
    }

    private string? FindFfprobe()
    {
        var ffmpeg = FindFfmpeg();
        if (!string.IsNullOrWhiteSpace(ffmpeg))
        {
            var sibling = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            if (File.Exists(sibling)) return sibling;
        }
        return FindOnPath(OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
    }

    private static string? FindOnPath(string executable)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), executable);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    private MediaActionResult MissingFfmpeg(string root) =>
        new(false, "ffmpeg_not_found", "FFmpeg پیدا نشد؛ مسیر ffmpeg.exe را در استودیو انتخاب کنید یا آن را به PATH ویندوز اضافه کنید.", root);

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static async Task<ProcessRun> RunProcessAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var watch = Stopwatch.StartNew();
        try
        {
            if (!process.Start()) return new(false, -1, "", "", false, 0);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return new(false, -1, "", "", false, watch.ElapsedMilliseconds);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan) timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            if (ct.IsCancellationRequested) throw;
            var outText = await stdoutTask.ConfigureAwait(false);
            var errText = await stderrTask.ConfigureAwait(false);
            watch.Stop();
            return new(false, -1, outText, errText, true, watch.ElapsedMilliseconds);
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        watch.Stop();
        return new(process.ExitCode == 0, process.ExitCode, stdout, stderr, false, watch.ElapsedMilliseconds);
    }

    private sealed record ProcessRun(bool Ok, int ExitCode, string Stdout, string Stderr, bool TimedOut, long DurationMs);

    private static class InstaloaderDownloader
    {
        public static async Task<MediaActionResult> DownloadAsync(string shortcode, string folder, CancellationToken ct)
        {
            var python = await FindPythonAsync(ct).ConfigureAwait(false);
            if (python is null)
                return new(false, "python_not_found", "Python نصب‌شده پیدا نشد؛ Instaloader رسمی برای دریافت همین پست به Python نیاز دارد.", folder);

            var version = await RunPythonAsync(python, new[] { "-m", "instaloader", "--version" }, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            if (!version.Ok)
            {
                var install = await RunPythonAsync(python,
                    new[] { "-m", "pip", "install", "--user", "--disable-pip-version-check", "instaloader" },
                    TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);
                if (!install.Ok)
                    return new(false, install.TimedOut ? "instaloader_install_timeout" : "instaloader_install_failed",
                        "نصب خودکار نسخهٔ رسمی Instaloader از Python Package Index ناموفق بود.", folder, DurationMs: install.DurationMs);
                version = await RunPythonAsync(python, new[] { "-m", "instaloader", "--version" }, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                if (!version.Ok)
                    return new(false, "instaloader_unavailable", "Instaloader پس از نصب خودکار اجرا نشد.", folder, DurationMs: version.DurationMs);
            }

            var watch = Stopwatch.StartNew();
            // The double hyphen is intentional: Instaloader documents -SHORTCODE as the single-post target.
            var result = await RunPythonAsync(python,
                new[] { "-m", "instaloader", "--dirname-pattern", folder, "--", "-" + shortcode },
                TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);
            watch.Stop();
            if (!result.Ok)
            {
                var message = result.TimedOut
                    ? "دریافت پست بیش از ده دقیقه طول کشید و متوقف شد."
                    : "Instaloader نتوانست پست انتخاب‌شده را دریافت کند؛ ممکن است Instagram دسترسی یا ورود را لازم بداند.";
                return new(false, result.TimedOut ? "instagram_download_timeout" : "instagram_download_failed", message, folder, DurationMs: watch.ElapsedMilliseconds);
            }
            return new(true, "ok", "Instaloader رسمی پایان یافت.", folder, DurationMs: watch.ElapsedMilliseconds);
        }

        private static async Task<PythonCommand?> FindPythonAsync(CancellationToken ct)
        {
            var candidates = OperatingSystem.IsWindows()
                ? new[] { new PythonCommand("python", Array.Empty<string>()), new PythonCommand("py", new[] { "-3" }), new PythonCommand("python3", Array.Empty<string>()) }
                : new[] { new PythonCommand("python3", Array.Empty<string>()), new PythonCommand("python", Array.Empty<string>()) };
            foreach (var candidate in candidates)
            {
                var result = await RunPythonAsync(candidate, new[] { "--version" }, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                if (result.Ok) return candidate;
            }
            return null;
        }

        private static Task<ProcessRun> RunPythonAsync(PythonCommand python, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken ct)
        {
            var all = python.Prefix.Concat(arguments).ToArray();
            return RunProcessAsync(python.Executable, all, timeout, ct);
        }

        private sealed record PythonCommand(string Executable, IReadOnlyList<string> Prefix);
    }
}
