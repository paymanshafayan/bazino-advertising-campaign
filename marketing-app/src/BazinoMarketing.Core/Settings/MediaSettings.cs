namespace BazinoMarketing.Core.Settings;

/// <summary>Non-secret settings for local media downloads and editing.</summary>
public sealed class MediaSettings
{
    /// <summary>The owner-selected folder. Empty means the app's dedicated folder under Downloads.</summary>
    public string OutputFolder { get; set; } = "";

    /// <summary>Optional full path to ffmpeg.exe. Empty means search PATH.</summary>
    public string FfmpegPath { get; set; } = "";

    /// <summary>auto (Groq first, Cloudflare fallback) | groq | cloudflare</summary>
    public string TranscriptionProvider { get; set; } = "auto";

    /// <summary>Groq Whisper model ID (whisper-large-v3 or whisper-large-v3-turbo).</summary>
    public string GroqModel { get; set; } = "whisper-large-v3";

    /// <summary>Optional ISO-639-1 language hint (e.g. "fa", "en"). Empty means auto-detect.</summary>
    public string TranscriptionLanguage { get; set; } = "";
}
