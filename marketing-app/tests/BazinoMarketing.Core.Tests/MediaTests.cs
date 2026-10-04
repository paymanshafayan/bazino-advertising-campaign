using System.IO;
using BazinoMarketing.Core.Mailbox;
using BazinoMarketing.Core.Media;
using BazinoMarketing.Core.Secrets;
using BazinoMarketing.Core.Settings;
using BazinoMarketing.Core.Tools;
using Xunit;

namespace BazinoMarketing.Core.Tests;

public sealed class InstagramPostUrlTests
{
    [Theory]
    [InlineData("https://www.instagram.com/p/DdtwsY0Kit1/", "DdtwsY0Kit1")]
    [InlineData("https://instagram.com/reel/AbC_123-x/?igsh=abc", "AbC_123-x")]
    [InlineData("https://www.instagram.com/tv/ZXcv987/", "ZXcv987")]
    [InlineData("https://www.instagram.com/fartash_v/p/DdtwsY0Kit1/", "DdtwsY0Kit1")]
    public void Reads_Exactly_One_Post_Shortcode(string url, string expected)
    {
        Assert.True(InstagramPostUrl.TryGetShortcode(url, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("https://www.instagram.com/fartash_v/")]
    [InlineData("https://www.instagram.com/stories/fartash_v/123")]
    [InlineData("https://notinstagram.com/p/DdtwsY0Kit1/")]
    [InlineData("file:///p/DdtwsY0Kit1")]
    [InlineData("https://www.instagram.com/p/ab/")]
    [InlineData("")]
    public void Rejects_Account_Pages_And_Non_Post_Urls(string url)
    {
        Assert.False(InstagramPostUrl.TryGetShortcode(url, out var shortcode));
        Assert.Equal("", shortcode);
    }
}

public sealed class GitHubDirectUploadTests
{
    [Fact]
    public void MaxDirectUploadBytes_Is_50_MiB()
    {
        Assert.Equal(50L * 1024 * 1024, GitHubClient.MaxDirectUploadBytes);
    }

    [Fact]
    public async Task UploadContentAsync_Rejects_Empty_Content_Before_Network_Access()
    {
        var settings = new GitHubSettings
        {
            Repository = "owner/repo",
            Branch = "arena/test"
        };
        await Assert.ThrowsAsync<ArgumentException>(() => GitHubClient.UploadContentAsync(
            settings, "safe-token", "agent-media-inbox/empty.mp4", Array.Empty<byte>(), CancellationToken.None));
    }
}

public sealed class MediaFolderTests
{
    [Fact]
    public void ConfirmFolder_Stores_A_Folder_After_ReadWrite_Check()
    {
        var root = TempFolder();
        try
        {
            var settings = new AppSettings();
            var media = new MediaService(() => settings, new InMemorySecretStore(), root);
            var selected = Path.Combine(root, "chosen");
            Assert.Equal(Path.GetFullPath(selected), media.ConfirmFolder(selected));
            Assert.Equal(Path.GetFullPath(selected), settings.Media.OutputFolder);
            Assert.Empty(Directory.EnumerateFiles(selected));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void ListFiles_Includes_Files_In_The_Confirmed_Folder_And_Uses_Relative_Paths()
    {
        var root = TempFolder();
        try
        {
            var settings = new AppSettings();
            settings.Media.OutputFolder = root;
            Directory.CreateDirectory(Path.Combine(root, "instagram", "edited"));
            File.WriteAllBytes(Path.Combine(root, "instagram", "post.mp4"), new byte[] { 1, 2, 3 });
            File.WriteAllText(Path.Combine(root, "instagram", "caption.txt"), "not media");
            File.WriteAllBytes(Path.Combine(root, "instagram", "edited", "still.jpg"), new byte[] { 4, 5 });

            var media = new MediaService(() => settings, new InMemorySecretStore(), root);
            var files = media.ListFiles();

            Assert.Equal(3, files.Count);
            Assert.Contains(files, f => f.RelativePath == "instagram/post.mp4" && f.IsVideo);
            Assert.Contains(files, f => f.RelativePath == "instagram/edited/still.jpg" && f.IsMedia);
            Assert.Contains(files, f => f.RelativePath == "instagram/caption.txt" && !f.IsMedia);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Upload_Allows_An_Owner_Selected_Non_Media_File_In_The_Folder()
    {
        var root = TempFolder();
        try
        {
            var settings = new AppSettings();
            settings.Media.OutputFolder = root;
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "docs", "notes.txt"), "selected by owner");
            var media = new MediaService(() => settings, new InMemorySecretStore(), root);
            var result = await media.UploadToGitHubAsync("docs/notes.txt");
            Assert.False(result.Ok);
            Assert.Equal("github_not_connected", result.Code);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Upload_Rejects_A_Path_Outside_The_Selected_Folder()
    {
        var root = TempFolder();
        try
        {
            var settings = new AppSettings();
            settings.Media.OutputFolder = root;
            var media = new MediaService(() => settings, new InMemorySecretStore(), root);
            var result = await media.UploadToGitHubAsync("../private.mp4");
            Assert.False(result.Ok);
            Assert.Equal("file_not_found", result.Code);
        }
        finally { TryDelete(root); }
    }

    private static string TempFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "BazinoMediaTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}

public sealed class MediaCommandTests
{
    [Fact]
    public void Media_Commands_Are_Registered_With_The_Right_Risk()
    {
        Assert.Contains(Commands.InstagramDownload, Commands.All);
        Assert.Contains(Commands.MediaList, Commands.All);
        Assert.Contains(Commands.MediaUpload, Commands.All);
        Assert.Contains(Commands.MediaFfmpeg, Commands.All);
        Assert.Contains(Commands.MediaTranscribe, Commands.All);
        Assert.Contains(Commands.SecretSet, Commands.All);
        Assert.Equal(Risk.Write, Commands.RequiredRisk(Commands.InstagramDownload));
        Assert.Equal(Risk.Read, Commands.RequiredRisk(Commands.MediaList));
        Assert.Equal(Risk.Write, Commands.RequiredRisk(Commands.MediaUpload));
        Assert.Equal(Risk.Write, Commands.RequiredRisk(Commands.MediaFfmpeg));
        Assert.Equal(Risk.Write, Commands.RequiredRisk(Commands.MediaTranscribe));
        Assert.Equal(Risk.Write, Commands.RequiredRisk(Commands.SecretSet));
    }

    [Fact]
    public void SpeechTranscriber_Parses_Groq_Verbose_Json_And_Segments()
    {
        var json = """
        {
          "text": "سلام به بازینو خوش آمدید",
          "language": "fa",
          "duration": 4.25,
          "segments": [
            { "start": 0.0, "end": 2.1, "text": "سلام به بازینو" },
            { "start": 2.1, "end": 4.25, "text": " خوش آمدید" }
          ]
        }
        """;

        var res = SpeechTranscriber.ParseGroqResponse(json, "whisper-large-v3", "", 320);
        Assert.True(res.Ok);
        Assert.Equal("groq", res.Provider);
        Assert.Equal("whisper-large-v3", res.Model);
        Assert.Equal("fa", res.Language);
        Assert.Equal(4.25, res.DurationSeconds);
        Assert.Equal("سلام به بازینو خوش آمدید", res.Text);
        Assert.Equal(2, res.Segments.Count);
        Assert.Equal("سلام به بازینو", res.Segments[0].Text);
        Assert.Contains("[00:00.0 -> 00:02.1]", SpeechTranscriber.FormatReadableTranscript(res, "video.mp4"));
    }

    [Fact]
    public void SpeechTranscriber_Parses_Cloudflare_Whisper_Response()
    {
        var json = """
        {
          "result": {
            "transcription_info": {
              "language": "en",
              "duration": 8.31275
            },
            "text": "Oh",
            "segments": [
              { "start": 0.0, "end": 0.6, "text": " Oh" }
            ],
            "vtt": "WEBVTT\n\n00:00.000 --> 00:00.600\n Oh\n\n"
          },
          "success": true
        }
        """;

        var res = SpeechTranscriber.ParseCloudflareResponse(json, "", 850);
        Assert.True(res.Ok);
        Assert.Equal("cloudflare", res.Provider);
        Assert.Equal(SpeechTranscriber.CloudflareWhisperModel, res.Model);
        Assert.Equal("en", res.Language);
        Assert.Equal("Oh", res.Text);
        Assert.Single(res.Segments);
        Assert.Contains("WEBVTT", res.Vtt);
    }

    [Fact]
    public void SpeechTranscriber_Resolves_Groq_Key_From_Direct_Secret_Or_Custom_Card()
    {
        var settings = new AppSettings();
        var secrets = new InMemorySecretStore();
        secrets.Set(SecretKeys.GroqApiKey, "gsk_direct_123");

        var (key1, _, base1) = SpeechTranscriber.ResolveGroqCredentials(settings, secrets);
        Assert.Equal("gsk_direct_123", key1);
        Assert.Equal(SpeechTranscriber.GroqBaseUrl, base1);

        secrets.Remove(SecretKeys.GroqApiKey);
        settings.CustomCards.Add(new CustomCard { Id = "groq-whisper", Name = "Groq", BaseUrl = "https://api.groq.com/openai/v1/" });
        secrets.Set(SecretKeys.ForCustomCredential("groq-whisper"), "gsk_card_456");

        var (key2, _, base2) = SpeechTranscriber.ResolveGroqCredentials(settings, secrets);
        Assert.Equal("gsk_card_456", key2);
        Assert.Equal("https://api.groq.com/openai/v1", base2);
    }

    [Fact]
    public async Task TranscribeAsync_Rejects_Non_Media_File()
    {
        var root = Path.Combine(Path.GetTempPath(), "BazinoTranscribeTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = new AppSettings();
            settings.Media.OutputFolder = root;
            File.WriteAllText(Path.Combine(root, "notes.txt"), "text file");
            var media = new MediaService(() => settings, new InMemorySecretStore(), root);
            var res = await media.TranscribeAsync("notes.txt", uploadToGitHub: false);
            Assert.False(res.Ok);
            Assert.Equal("audio_or_video_required", res.Code);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task GitHub_Content_Upload_Rejects_Path_Traversal_Before_Network_Access()
    {
        var settings = new GitHubSettings
        {
            Repository = "owner/repo",
            Branch = "arena/test"
        };
        await Assert.ThrowsAsync<ArgumentException>(() => GitHubClient.UploadContentAsync(
            settings, "safe-token", "../secret.txt", new byte[] { 1 }, CancellationToken.None));
    }
}
