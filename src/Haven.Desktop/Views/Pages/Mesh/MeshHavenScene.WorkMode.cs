using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using Container = Haven.UI.Components.Container;

namespace Haven.Desktop.Views.Pages.Mesh;

internal sealed partial class MeshHavenScene
{
    private void BuildWorkModeContent(Container parent)
    {
        var intro = Card("Mesh.Work.Intro");
        intro.Add(Heading("Work Mode · your AI team room", TextLevel.H2));
        intro.Add(Muted("Give remote models or agents friendly names, talk to one directly, use a shared Team Room, or let a coordinator plan, delegate and review work across Mesh devices."));
        intro.Add(Muted("Examples: “check up on Mike”, “ask everyone for ideas”, “have Sarah review Mike's result”, or just describe a goal and let the coordinator allocate it."));
        parent.Add(intro);

        parent.Add(BuildWorkerCreator());

        var teamHeader = Row();
        teamHeader.Add(Heading("Team", TextLevel.H2));
        var count = Muted(_viewModel.Workers.Count == 0 ? "No named workers yet" : $"{_viewModel.Workers.Count} member{(_viewModel.Workers.Count == 1 ? string.Empty : "s")}");
        Set(count, HavenProperties.Column, 1);
        teamHeader.Add(count);
        parent.Add(teamHeader);
        if (_viewModel.Workers.Count == 0)
        {
            var empty = Card("Mesh.Work.EmptyTeam");
            empty.Add(Muted("Allow remote Models or Agents on a trusted device, refresh runtimes above, then give one a friendly name such as Mike or Sarah."));
            parent.Add(empty);
        }
        else
        {
            foreach (var worker in _viewModel.Workers) parent.Add(BuildWorkerCard(worker));
        }

        parent.Add(BuildConversationConsole());
        if (!string.IsNullOrWhiteSpace(_viewModel.Output))
        {
            var output = Card("Mesh.Work.Output");
            output.Add(Heading("Latest result"));
            output.Add(Muted(_viewModel.Output));
            parent.Add(output);
        }

        if (_viewModel.WorkItems.Count > 0)
        {
            parent.Add(Heading("Delegated work", TextLevel.H2));
            foreach (var work in _viewModel.WorkItems.OrderByDescending(item => item.UpdatedAt).Take(12)) parent.Add(BuildWorkCard(work));
        }

        var transcript = Card("Mesh.Work.Transcript");
        transcript.Add(Heading("Team Room transcript", TextLevel.H2));
        if (_viewModel.TeamMessages.Count == 0) transcript.Add(Muted("No Work Mode messages yet. Shared-pool replies will appear here and become context for later Team Room messages."));
        else
        {
            foreach (var message in _viewModel.TeamMessages.Where(message => message.Channel == MeshWorkChannelKind.SharedPool).TakeLast(30))
            {
                var speaker = message.Role == MeshWorkMessageRole.User ? "You" :
                    message.SenderWorkerId is { } sender ? _viewModel.Workers.FirstOrDefault(worker => worker.Member.WorkerId == sender)?.Member.Name ?? message.Role.ToString() : message.Role.ToString();
                transcript.Add(Heading(speaker, TextLevel.H4));
                transcript.Add(Muted(Truncate(message.Content, 1800)));
            }
        }
        parent.Add(transcript);
    }

    private Container BuildWorkerCreator()
    {
        var card = Card("Mesh.Work.Creator");
        var header = Row();
        header.Add(Heading("Add a team member", TextLevel.H2));
        var refresh = Button("Mesh.Work.RefreshRuntimes", "Refresh models & agents", ButtonVariant.Secondary);
        Set(refresh, HavenProperties.Column, 1);
        refresh.Invoked += async (_, _) => await RunAndRenderAsync(token => _viewModel.RefreshRuntimeChoicesAsync(token));
        header.Add(refresh);
        card.Add(header);
        card.Add(Muted("Only runtimes explicitly permitted by the target device appear here. Naming a runtime does not change its underlying model or agent identity."));

        var runtime = new Select { Name = "Mesh.Work.Creator.Runtime", Items = _viewModel.RuntimeChoices.Select(choice => choice.Label).ToArray(), SelectedIndex = _viewModel.SelectedRuntimeIndex };
        runtime.Accessibility.AccessibleName = "Remote model or agent";
        Set(runtime, HavenProperties.Width, HavenLength.Percent(100));
        runtime.SelectionChanged += (_, _) => _viewModel.SelectedRuntimeIndex = runtime.SelectedIndex;
        card.Add(runtime);

        var names = Row("1fr 1fr");
        var name = InputField("Mesh.Work.Creator.Name", "Friendly name — e.g. Mike"); name.Text = _viewModel.WorkerName; names.Add(name);
        var role = InputField("Mesh.Work.Creator.Role", "Role — e.g. coding, research"); role.Text = _viewModel.WorkerRole; Set(role, HavenProperties.Column, 1); names.Add(role);
        card.Add(names);
        var specialties = InputField("Mesh.Work.Creator.Specialties", "Specialties, comma separated — e.g. C#, tests, review");
        specialties.Text = _viewModel.WorkerSpecialties; card.Add(specialties);

        var coordinatorRow = Row();
        var coordinatorLabel = new Container { Layout = HavenLayout.Vertical };
        coordinatorLabel.Add(Heading("Coordinator", TextLevel.H4));
        coordinatorLabel.Add(Muted("Make this the one team member that plans, delegates, checks and synthesises multi-worker jobs."));
        coordinatorRow.Add(coordinatorLabel);
        var coordinatorToggle = new Toggle { Name = "Mesh.Work.Creator.Coordinator", IsChecked = _viewModel.NewWorkerIsCoordinator };
        coordinatorToggle.Accessibility.AccessibleName = "Make this worker the coordinator";
        Set(coordinatorToggle, HavenProperties.Column, 1);
        coordinatorRow.Add(coordinatorToggle);
        card.Add(coordinatorRow);

        var add = Button("Mesh.Work.Creator.Add", "Add to team", ButtonVariant.Primary);
        add.Invoked += async (_, _) =>
        {
            _viewModel.WorkerName = name.Text; _viewModel.WorkerRole = role.Text; _viewModel.WorkerSpecialties = specialties.Text;
            _viewModel.SelectedRuntimeIndex = runtime.SelectedIndex; _viewModel.NewWorkerIsCoordinator = coordinatorToggle.IsChecked;
            await RunAndRenderAsync(token => _viewModel.CreateWorkerAsync(token));
        };
        card.Add(add);
        return card;
    }

    private Container BuildWorkerCard(MeshWorkMemberStatus status)
    {
        var member = status.Member;
        var card = Card("Mesh.Work.Member." + member.WorkerId.ToString("N"));
        var title = Row(); title.Add(Heading(member.Name));
        var badge = Muted(member.IsCoordinator ? $"Coordinator · {status.Presence}" : status.Presence.ToString()); Set(badge, HavenProperties.Column, 1); title.Add(badge); card.Add(title);
        card.Add(Muted($"{member.RuntimeDisplayName} · {member.RuntimeKind} · device {member.DeviceId.ToString("N")[..8]}"));
        if (!string.IsNullOrWhiteSpace(member.Role)) card.Add(Muted("Role: " + member.Role));
        if (member.Specialties.Count > 0) card.Add(Muted("Specialties: " + string.Join(", ", member.Specialties)));
        card.Add(Muted(status.Summary));

        var actions = new Container { Layout = HavenLayout.Wrap }; Set(actions, HavenProperties.Gap, HavenLength.Px(8));
        var chat = Button("Mesh.Work.Member.Chat." + member.WorkerId.ToString("N"), "Message directly", ButtonVariant.Secondary);
        chat.Invoked += (_, _) => { _viewModel.SelectDirectWorker(member.WorkerId); Render(); }; actions.Add(chat);
        if (!member.IsCoordinator)
        {
            var makeCoordinator = Button("Mesh.Work.Member.Coordinator." + member.WorkerId.ToString("N"), "Make coordinator", ButtonVariant.Ghost);
            makeCoordinator.Invoked += async (_, _) => await RunAndRenderAsync(token => _viewModel.MakeCoordinatorAsync(member.WorkerId, token)); actions.Add(makeCoordinator);
        }
        var remove = Button("Mesh.Work.Member.Remove." + member.WorkerId.ToString("N"), "Remove from team", ButtonVariant.Danger);
        remove.Invoked += async (_, _) => await RunAndRenderAsync(token => _viewModel.RemoveWorkerAsync(member.WorkerId, token)); actions.Add(remove);
        card.Add(actions);
        return card;
    }

    private Container BuildConversationConsole()
    {
        var shell = Card("Mesh.Work.Console");
        shell.Add(Heading("Team communications", TextLevel.H2));

        var direct = new Container { Layout = HavenLayout.Vertical }; Set(direct, HavenProperties.Gap, HavenLength.Px(7));
        direct.Add(Heading("Direct · " + _viewModel.DirectWorkerName, TextLevel.H3));
        var directInput = InputField("Mesh.Work.Direct.Input", "Message the selected model or agent directly", multiline: true); directInput.Text = _viewModel.DirectMessage; direct.Add(directInput);
        var directSend = Button("Mesh.Work.Direct.Send", "Send directly", ButtonVariant.Secondary);
        directSend.Invoked += async (_, _) => { _viewModel.DirectMessage = directInput.Text; await RunAndRenderAsync(token => _viewModel.SendDirectAsync(token)); }; direct.Add(directSend);
        shell.Add(direct);

        var team = new Container { Layout = HavenLayout.Vertical }; Set(team, HavenProperties.Gap, HavenLength.Px(7));
        team.Add(Heading("Team Room", TextLevel.H3)); team.Add(Muted("Everyone receives the message. Replies are kept in one shared transcript and become context for later Team Room turns."));
        var teamInput = InputField("Mesh.Work.Team.Input", "Ask everyone for ideas…", multiline: true); teamInput.Text = _viewModel.TeamMessage; team.Add(teamInput);
        var teamSend = Button("Mesh.Work.Team.Send", "Send to everyone", ButtonVariant.Primary);
        teamSend.Invoked += async (_, _) => { _viewModel.TeamMessage = teamInput.Text; await RunAndRenderAsync(token => _viewModel.SendTeamMessageAsync(token)); }; team.Add(teamSend); shell.Add(team);

        var coordinator = new Container { Layout = HavenLayout.Vertical }; Set(coordinator, HavenProperties.Gap, HavenLength.Px(7));
        coordinator.Add(Heading("Coordinator console", TextLevel.H3)); coordinator.Add(Muted("Natural commands can inspect named workers, message them, ask the whole team, request reviews, or turn a broad goal into a delegated plan."));
        var command = InputField("Mesh.Work.Coordinator.Input", "e.g. check up on Mike, or finish this using whoever is free", multiline: true); command.Text = _viewModel.CoordinatorCommand; coordinator.Add(command);
        var run = Button("Mesh.Work.Coordinator.Run", "Run with Work Mode", ButtonVariant.Primary);
        run.Invoked += async (_, _) => { _viewModel.CoordinatorCommand = command.Text; await RunAndRenderAsync(token => _viewModel.RunCoordinatorCommandAsync(token)); }; coordinator.Add(run); shell.Add(coordinator);
        return shell;
    }

    private Container BuildWorkCard(MeshWorkItem item)
    {
        var worker = _viewModel.Workers.FirstOrDefault(entry => entry.Member.WorkerId == item.AssignedWorkerId)?.Member.Name ?? "Unknown worker";
        var reviewer = item.ReviewerWorkerId is { } reviewerId ? _viewModel.Workers.FirstOrDefault(entry => entry.Member.WorkerId == reviewerId)?.Member.Name : null;
        var card = Card("Mesh.Work.Item." + item.WorkItemId.ToString("N"));
        var title = Row(); title.Add(Heading(item.Goal)); var state = Muted(item.Status.ToString()); Set(state, HavenProperties.Column, 1); title.Add(state); card.Add(title);
        card.Add(Muted($"Assigned to {worker}{(reviewer is null ? string.Empty : " · review by " + reviewer)} · updated {item.UpdatedAt.LocalDateTime:t}"));
        if (!string.IsNullOrWhiteSpace(item.Result)) card.Add(Muted("Result: " + Truncate(item.Result, 900)));
        if (!string.IsNullOrWhiteSpace(item.Review)) card.Add(Muted("Review: " + Truncate(item.Review, 700)));
        if (!string.IsNullOrWhiteSpace(item.Error)) card.Add(Muted("Error: " + item.Error));
        return card;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
