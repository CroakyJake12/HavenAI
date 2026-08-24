using System.Text.RegularExpressions;
using Haven.Core;

namespace Haven.Application;

public sealed partial class MeshCoordinator
{
    public async Task<MeshWorkCommandResult> ExecuteWorkCommandAsync(string command, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("A Work Mode command is required.", nameof(command));
        var beforeMessages = WorkMessages().Select(message => message.MessageId).ToHashSet();
        var beforeWork = WorkItems().Select(item => item.WorkItemId).ToHashSet();
        var text = command.Trim().Replace("’", "'", StringComparison.Ordinal);
        string message;

        var check = Regex.Match(text, "^check up on (?<name>.+?)[?.!]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var working = Regex.Match(text, "^what(?:'s| is) (?<name>.+?) working on[?.!]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (check.Success || working.Success)
        {
            var name = (check.Success ? check : working).Groups["name"].Value.Trim();
            message = (await CheckUpAsync(name, cancellationToken).ConfigureAwait(false)).Summary;
        }
        else if (Regex.IsMatch(text, "^(get |show )?(everyone's|everyones|team) status[?.!]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var status = await GetWorkModeAsync(cancellationToken).ConfigureAwait(false);
            message = string.Join(Environment.NewLine, status.Members.Select(member => member.Summary));
        }
        else if (text.StartsWith("ask everyone ", StringComparison.OrdinalIgnoreCase))
        {
            var prompt = text["ask everyone ".Length..].Trim();
            var replies = await PostSharedPoolAsync(prompt, null, cancellationToken).ConfigureAwait(false);
            message = string.Join(Environment.NewLine + Environment.NewLine, replies.Select(reply =>
                $"{ResolveWorkMember(reply.SenderWorkerId ?? reply.TargetWorkerId ?? Guid.Empty).Name}: {reply.Content}"));
        }
        else if (TryParseReviewCommand(text, out var reviewerName, out var subjectName))
        {
            var reviewer = ResolveWorkMember(reviewerName);
            var subject = ResolveWorkMember(subjectName);
            var latest = WorkMessages().Where(item => item.SenderWorkerId == subject.WorkerId && item.Status == MeshWorkMessageStatus.Succeeded)
                .OrderByDescending(item => item.CreatedAt).FirstOrDefault()
                ?? throw new InvalidOperationException($"{subject.Name} has no completed result to review yet.");
            var reply = await SendWorkMessageAsync(reviewer.WorkerId,
                $"Review {subject.Name}'s latest result. Identify correctness issues, omissions and concrete improvements.\n\n{latest.Content}",
                cancellationToken).ConfigureAwait(false);
            message = reply.Content;
        }
        else if (TryParseNamedInstruction(text, out var target, out var instruction))
        {
            var member = ResolveWorkMember(target);
            var reply = await SendWorkMessageAsync(member.WorkerId, instruction, cancellationToken).ConfigureAwait(false);
            message = reply.Content;
        }
        else
        {
            var run = await CoordinateWorkAsync(text, cancellationToken).ConfigureAwait(false);
            message = run.Summary;
        }

        var after = await GetWorkModeAsync(cancellationToken).ConfigureAwait(false);
        return new(message, after,
            WorkMessages().Where(item => !beforeMessages.Contains(item.MessageId)).OrderBy(item => item.CreatedAt).ToArray(),
            WorkItems().Where(item => !beforeWork.Contains(item.WorkItemId)).OrderBy(item => item.CreatedAt).ToArray());
    }

    private bool TryParseNamedInstruction(string text, out string target, out string instruction)
    {
        target = string.Empty;
        instruction = string.Empty;
        foreach (var member in WorkMembers().Where(member => member.IsEnabled).OrderByDescending(member => member.Name.Length))
        {
            foreach (var prefix in new[] { "ask ", "have ", "tell " })
            {
                var lead = prefix + member.Name;
                if (!text.StartsWith(lead, StringComparison.OrdinalIgnoreCase)) continue;
                var remainder = text[lead.Length..].TrimStart();
                if (remainder.StartsWith("to ", StringComparison.OrdinalIgnoreCase)) remainder = remainder[3..].Trim();
                else if (remainder.StartsWith("handle ", StringComparison.OrdinalIgnoreCase)) remainder = remainder[7..].Trim();
                else if (remainder.StartsWith("do ", StringComparison.OrdinalIgnoreCase)) remainder = remainder[3..].Trim();
                if (remainder.Length == 0) continue;
                target = member.Name;
                instruction = remainder;
                return true;
            }
        }
        return false;
    }

    private static bool TryParseReviewCommand(string text, out string reviewer, out string subject)
    {
        reviewer = string.Empty;
        subject = string.Empty;
        var match = Regex.Match(text, "^have (?<reviewer>.+?) review (?<subject>.+?)'s (?:latest )?result[?.!]*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        reviewer = match.Groups["reviewer"].Value.Trim();
        subject = match.Groups["subject"].Value.Trim();
        return reviewer.Length > 0 && subject.Length > 0;
    }
}
