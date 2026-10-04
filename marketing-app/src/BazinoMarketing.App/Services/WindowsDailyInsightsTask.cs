using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace BazinoMarketing.App.Services;

/// <summary>
/// Installs an interactive-token Task Scheduler job for the 08:00 daily Instagram report; no password or elevation is
/// stored. The task XML is validated by <c>schtasks.exe</c> against the Task Scheduler schema, so the settings block
/// uses only schema elements in schema order — <c>DisallowStartIfOnBatteries</c> (the old file used the non-existent
/// <c>AllowStartIfOnBatteries</c>, which schtasks rejected with "unexpected node"). Two fallbacks keep a runnable task
/// in place even on the narrowest Windows builds, and the app's own start-up check covers any run the scheduler misses.
/// </summary>
internal static class WindowsDailyInsightsTask
{
    private const string TaskName = "\\Bazino Marketing Daily Instagram Insights";

    public static Task<(bool Succeeded, string Detail)> EnsureInstalledAsync(string? executablePath, CancellationToken ct = default) =>
        EnsureInstalledCoreAsync(executablePath, ct);

    private static async Task<(bool Succeeded, string Detail)> EnsureInstalledCoreAsync(string? executablePath, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return (false, "Windows Task Scheduler is unavailable on this operating system");
        var executable = executablePath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return (false, "Application executable path is unavailable");
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(sid)) return (false, "Current Windows user SID is unavailable");

        var existing = await QueryTaskXmlAsync(ct).ConfigureAwait(false);
        if (existing.Succeeded && existing.Xml is { Length: > 0 } xml &&
            (xml.Contains(executable, StringComparison.OrdinalIgnoreCase) || xml.Contains(Escape(executable), StringComparison.OrdinalIgnoreCase)) &&
            xml.Contains("DisallowStartIfOnBatteries", StringComparison.OrdinalIgnoreCase))
            return (true, "Existing daily task already points at this build and is kept unchanged so a missed 08:00 run can still catch up");

        var now = DateTime.Now;
        var start = now.Date.AddHours(DailyReportMarker.ProductionHour);
        if (start <= now) start = start.AddDays(1);
        var boundary = start.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var exe = Escape(executable);
        var workingDirectory = Escape(Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory);

        // Attempt 1: full definition. Attempt 2: same trigger/principal, no Settings block (some builds validate Settings
        // more strictly). Attempt 3: plain schtasks /SC DAILY defaults, no XML at all.
        var attempts = new List<(string Name, string Args)>
        {
            ("full-xml", FullXml(boundary, sid, exe, workingDirectory)),
            ("lean-xml", LeanXml(boundary, sid, exe, workingDirectory))
        };
        string? lastDetail = existing.Detail;
        foreach (var (name, xmlBody) in attempts)
        {
            var result = await RegisterWithXmlAsync(xmlBody, ct).ConfigureAwait(false);
            if (result.Succeeded)
                return (true, $"Daily task registered for {DailyReportMarker.ProductionHour:00}:00 local time ({name}); missed runs start when the user becomes available");
            lastDetail = result.Detail;
        }

        var plain = await RegisterPlainAsync(executable, ct).ConfigureAwait(false);
        if (plain.Succeeded)
            return (true, $"Daily task registered for {DailyReportMarker.ProductionHour:00}:00 local time (plain schtasks defaults); " +
                          "Windows may skip it on battery, so the app also catches up on every launch");

        return (false, string.Join(" | ", new[] { lastDetail, plain.Detail }.Where(d => !string.IsNullOrWhiteSpace(d))));
    }

    private static string FullXml(string boundary, string sid, string exe, string workingDirectory) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo><Description>Bazino Marketing daily Instagram insights at 08:00 local time.</Description></RegistrationInfo>
          <Triggers><CalendarTrigger><StartBoundary>{boundary}</StartBoundary><Enabled>true</Enabled><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger></Triggers>
          <Principals><Principal id="Author"><UserId>{Escape(sid)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>true</StartWhenAvailable>
            <WakeToRun>true</WakeToRun>
            <Enabled>true</Enabled>
            <ExecutionTimeLimit>PT30M</ExecutionTimeLimit>
            <Priority>7</Priority>
          </Settings>
          <Actions Context="Author"><Exec><Command>{exe}</Command><Arguments>--daily-insights</Arguments><WorkingDirectory>{workingDirectory}</WorkingDirectory></Exec></Actions>
        </Task>
        """;

    private static string LeanXml(string boundary, string sid, string exe, string workingDirectory) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo><Description>Bazino Marketing daily Instagram insights at 08:00 local time.</Description></RegistrationInfo>
          <Triggers><CalendarTrigger><StartBoundary>{boundary}</StartBoundary><Enabled>true</Enabled><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger></Triggers>
          <Principals><Principal id="Author"><UserId>{Escape(sid)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
          <Actions Context="Author"><Exec><Command>{exe}</Command><Arguments>--daily-insights</Arguments><WorkingDirectory>{workingDirectory}</WorkingDirectory></Exec></Actions>
        </Task>
        """;

    private static async Task<(bool Succeeded, string Detail)> RegisterWithXmlAsync(string xml, CancellationToken ct)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), "bazino-daily-insights-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            await File.WriteAllTextAsync(xmlPath, xml, new UnicodeEncoding(false, true), ct).ConfigureAwait(false);
            return await RunSchTasksAsync(ct, "/Create", "/TN", TaskName, "/XML", xmlPath, "/F").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, ex.Message);
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }
    }

    private static Task<(bool Succeeded, string Detail)> RegisterPlainAsync(string executable, CancellationToken ct) =>
        RunSchTasksAsync(ct, "/Create", "/TN", TaskName, "/TR", Quote(executable) + " --daily-insights",
            "/SC", "DAILY", "/ST", $"{DailyReportMarker.ProductionHour:00}:00", "/F");

    private static string Quote(string value) => value.Contains(' ') ? "\"" + value + "\"" : value;

    private static async Task<(bool Succeeded, string Detail)> RunSchTasksAsync(CancellationToken ct, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);
        if (process is null) return (false, "schtasks.exe could not be started");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (false, "Task Scheduler registration timed out");
        }
        var stdout = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        var detail = string.Join(" ", new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        return process.ExitCode == 0
            ? (true, string.IsNullOrWhiteSpace(detail) ? "ok" : detail)
            : (false, string.IsNullOrWhiteSpace(detail) ? $"schtasks.exe exited with code {process.ExitCode}" : detail);
    }

    private static async Task<(bool Succeeded, string Detail, string? Xml)> QueryTaskXmlAsync(CancellationToken ct)
    {
        try
        {
            var startInfo = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "/Query", "/TN", TaskName, "/XML" }) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo);
            if (process is null) return (false, "schtasks.exe could not be started", null);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch { } return (false, "task query timed out", null); }
            var xml = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            var error = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            return process.ExitCode == 0
                ? (true, "ok", xml)
                : (false, string.Join(" ", new[] { xml, error }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim(), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, ex.Message, null);
        }
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? "";
}
