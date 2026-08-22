/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Terminal/TerminalExecution.cs.
 * What: Shared Terminal permission, redaction, and command-activity contracts.
 * Why: Terminal and agent commands must share policy and never leak secrets into visible history.
 */
using System.Text.RegularExpressions;
using Haven.Core;
namespace Haven.Application;
public enum TerminalPermissionDecision { Allowed, RequiresApproval, Denied }
public enum TerminalExecutionState { Requested, PermissionRequired, Denied, Running, Succeeded, Failed, Cancelled }
public enum TerminalCommandOrigin { User, Agent }
public sealed record TerminalPermissionResult(TerminalPermissionDecision Decision, string Reason);
public static class TerminalCommandPolicy
{
    public static TerminalPermissionResult Evaluate(PermissionMode permission, bool approvedOnce = false)
    {
        if (RuntimeSafetyState.IsSafeMode) return new(TerminalPermissionDecision.Denied, "Haven recovery Safe Mode disables local command execution.");
        if (approvedOnce || permission == PermissionMode.FullAccess) return new(TerminalPermissionDecision.Allowed, "Command execution is allowed.");
        return permission switch
        {
            PermissionMode.Ask => new(TerminalPermissionDecision.RequiresApproval, "Command permission is Ask. Approve this command once to run it."),
            PermissionMode.AutoSafe => new(TerminalPermissionDecision.RequiresApproval, "Auto Safe does not automatically run arbitrary shell commands. Approve this command once to run it."),
            _ => new(TerminalPermissionDecision.Denied, "Command execution is not available.")
        };
    }
}
public static class SensitiveTextRedactor
{
    private static readonly Regex UrlPattern = new(@"https?://[^\s<>""']+", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex BearerPattern = new(@"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex JsonSecretPattern = new(@"(?i)(?<prefix>[""']?(?:api[-_]?key|access[-_]?token|refresh[-_]?token|token|secret|password|authorization|cookie|credential)[""']?\s*:\s*)(?<quote>[""'])(?<value>.*?)(?:\k<quote>)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SecretAssignmentPattern = new(@"(?i)(api[-_]?key|access[-_]?token|refresh[-_]?token|token|secret|password|authorization|cookie|credential)\s*[:=]\s*([^\s,;]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static string Redact(string? value, int maximumLength = 4_000)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (maximumLength < 1) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        var sanitized = value.Replace('\0', ' ');
        sanitized = UrlPattern.Replace(sanitized, match => RedactUrl(match.Value));
        sanitized = BearerPattern.Replace(sanitized, "Bearer <redacted>");
        sanitized = JsonSecretPattern.Replace(sanitized, "${prefix}${quote}<redacted>${quote}");
        sanitized = SecretAssignmentPattern.Replace(sanitized, "$1=<redacted>");
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile)) sanitized = sanitized.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        return sanitized.Length <= maximumLength ? sanitized : sanitized[..maximumLength] + "…";
    }
    private static string RedactUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return value;
        try { return new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty, UserName = string.Empty, Password = string.Empty }.Uri.AbsoluteUri; }
        catch (UriFormatException) { return uri.GetLeftPart(UriPartial.Path); }
    }
}
public sealed record TerminalCommandActivity(Guid Id, TerminalCommandOrigin Origin, TerminalExecutionState State, string Command, string WorkingDirectory, ProcessResult? Result, string? Error, DateTimeOffset Timestamp);
public sealed class TerminalCommandActivityHub
{
    public event EventHandler<TerminalCommandActivity>? ActivityPublished;
    public void Publish(TerminalCommandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ProcessResult? result = activity.Result is null ? null : activity.Result with { StandardOutput = SensitiveTextRedactor.Redact(activity.Result.StandardOutput, 120_000), StandardError = SensitiveTextRedactor.Redact(activity.Result.StandardError, 120_000) };
        ActivityPublished?.Invoke(this, activity with { Command = SensitiveTextRedactor.Redact(activity.Command, 8_000), WorkingDirectory = SensitiveTextRedactor.Redact(activity.WorkingDirectory, 8_000), Error = SensitiveTextRedactor.Redact(activity.Error, 8_000), Result = result });
    }
}
