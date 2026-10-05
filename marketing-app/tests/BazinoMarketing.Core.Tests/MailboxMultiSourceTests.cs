using BazinoMarketing.Core.Mailbox;
using BazinoMarketing.Core.Settings;
using Xunit;

namespace BazinoMarketing.Core.Tests;

/// <summary>
/// Phase 8 of PLAN-006 (owner decision 2026-10-04): several repositories + branches as mailbox sources, added by pasting the
/// public URL of a branch, plus the owner-approved agent registry.
/// </summary>
public sealed class MailboxMultiSourceTests
{
    [Theory]
    // The exact shape the owner used as the example (URL-encoded slash in the branch name).
    [InlineData("https://github.com/paymanshafayan/bazino-gamenet-portal/tree/arena%2F01a10048-bazino-gamenet-portal",
        "paymanshafayan/bazino-gamenet-portal", "arena/01a10048-bazino-gamenet-portal")]
    [InlineData("https://github.com/owner/repo/tree/main", "owner/repo", "main")]
    [InlineData("https://github.com/owner/repo/tree/feature%2Fnested%2Fbranch", "owner/repo", "feature/nested/branch")]
    [InlineData("https://github.com/owner/repo", "owner/repo", "main")]
    [InlineData("https://github.com/owner/repo/", "owner/repo", "main")]
    [InlineData("github.com/owner/repo/tree/dev", "owner/repo", "dev")]
    [InlineData("owner/repo", "owner/repo", "main")]
    [InlineData("https://github.com/owner/repo.git", "owner/repo", "main")]
    [InlineData("https://github.com/owner/repo/tree/deep%2Fbranch/sub", "owner/repo", "deep/branch/sub")]
    public void Parses_branch_urls(string input, string expectedRepository, string expectedBranch)
    {
        Assert.True(MailboxSourceUrl.TryParse(input, out var repository, out var branch, out var error), error);
        Assert.Equal(expectedRepository, repository);
        Assert.Equal(expectedBranch, branch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://gitlab.com/owner/repo/tree/main")]
    [InlineData("owner")]
    public void Rejects_addresses_it_cannot_use(string input)
    {
        Assert.False(MailboxSourceUrl.TryParse(input, out _, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Source_key_is_stable_and_case_insensitive_on_the_repository()
    {
        var a = MailboxSourceUrl.KeyOf("Owner/Repo", "arena/branch", "marketing-app-mailbox");
        var b = MailboxSourceUrl.KeyOf("owner/repo", "arena/branch", "marketing-app-mailbox/");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Effective_sources_start_with_the_primary_branch_and_skip_duplicates()
    {
        var settings = new GitHubSettings { Repository = "owner/repo", Branch = "main", MailboxPath = "marketing-app-mailbox" };
        settings.Sources.Add(new MailboxSourceSettings { Repository = "owner/repo", Branch = "main" });          // duplicate
        settings.Sources.Add(new MailboxSourceSettings { Repository = "owner/repo", Branch = "arena/second" }); // new

        var sources = settings.EffectiveSources();

        Assert.Equal(2, sources.Count);
        Assert.Equal("main", sources[0].Branch);
        Assert.Equal("arena/second", sources[1].Branch);
    }

    [Fact]
    public void A_source_copies_the_github_settings_for_its_own_transport()
    {
        var settings = new GitHubSettings { Repository = "owner/primary", Branch = "main", PollSeconds = 7 };
        var source = new MailboxSourceSettings { Repository = "other/second", Branch = "arena/one" };

        var copy = settings.ForSource(source);

        Assert.Equal("other/second", copy.Repository);
        Assert.Equal("arena/one", copy.Branch);
        Assert.Equal(7, copy.PollSeconds);
    }

    [Fact]
    public void Public_url_rebuilds_the_owner_visible_address()
    {
        var source = new MailboxSourceSettings { Repository = "owner/repo", Branch = "arena/one" };
        Assert.Equal("https://github.com/owner/repo/tree/arena%2Fone", source.PublicUrl);
    }

    [Fact]
    public void A_fresh_fingerprint_is_pending_and_never_approved_by_itself()
    {
        var path = Path.Combine(Path.GetTempPath(), "bazino-agents-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var registry = new AgentRegistry(path);
            var record = registry.Observe("aaaa:bbbb:cccc:dddd", "Arena agent", "owner/repo@main#marketing-app-mailbox");

            Assert.Equal(AgentTrustState.Pending, record.State);
            Assert.False(registry.IsApproved(record.Fingerprint));

            Assert.True(registry.Approve(record.Fingerprint));
            Assert.True(registry.IsApproved(record.Fingerprint));

            Assert.True(registry.Disable(record.Fingerprint));
            Assert.False(registry.IsApproved(record.Fingerprint));
            Assert.Equal(AgentTrustState.Disabled, registry.Get(record.Fingerprint)!.State);

            // A disabled fingerprint that shows up again stays disabled: only the owner can change that.
            var again = registry.Observe(record.Fingerprint, "Arena agent", "owner/repo@main#marketing-app-mailbox");
            Assert.Equal(AgentTrustState.Disabled, again.State);
            Assert.NotNull(again.DisabledAt);

            Assert.True(registry.Enable(record.Fingerprint));
            Assert.True(registry.IsApproved(record.Fingerprint));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void The_registry_survives_a_restart_and_keeps_the_owner_decision()
    {
        var path = Path.Combine(Path.GetTempPath(), "bazino-agents-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var first = new AgentRegistry(path);
            first.Observe("1111:2222:3333:4444", "One", "owner/repo@main#mailbox");
            first.Approve("1111:2222:3333:4444");
            first.Observe("5555:6666:7777:8888", "Two", "owner/repo@arena/second#mailbox");

            var second = new AgentRegistry(path);

            Assert.True(second.IsApproved("1111:2222:3333:4444"));
            Assert.Equal(AgentTrustState.Pending, second.Get("5555:6666:7777:8888")!.State);
            Assert.Equal(2, second.All.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Window_state_is_a_known_command_so_the_agent_can_restore_the_app_window()
    {
        Assert.Contains(Commands.WindowState, Commands.All);
        Assert.Equal(Risk.Read, Commands.RequiredRisk(Commands.WindowState));
    }
}
