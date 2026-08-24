using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class PermissionDecisionEngineTests
{
    [Fact]
    public void AlwaysAskPromptsForPermissionedAction()
    {
        var engine = new PermissionDecisionEngine();

        var decision = engine.Evaluate("workspace.run-command", CapabilityRiskClass.Restricted, true, "Run command");

        Assert.Equal(PermissionDecisionKind.Ask, decision.Kind);
    }

    [Fact]
    public void HighRiskPolicyAllowsLowRiskPermissionedAction()
    {
        var engine = new PermissionDecisionEngine();
        engine.SetPolicy(HavenPermissionPolicy.AskWithHighRisk);

        var decision = engine.Evaluate("calendar.create", CapabilityRiskClass.Low, true, "Create local item");

        Assert.Equal(PermissionDecisionKind.Allowed, decision.Kind);
    }

    [Fact]
    public void AlwaysAllowStillRequiresScopedGrantForConsequentialAction()
    {
        var engine = new PermissionDecisionEngine();
        engine.SetPolicy(HavenPermissionPolicy.AlwaysAllow);
        Assert.Equal(PermissionDecisionKind.Ask,
            engine.Evaluate("tasks.automation.create", CapabilityRiskClass.Consequential, true, "Create a scheduled action").Kind);
    }

    [Fact]
    public void ScopedGrantDoesNotGrantAnotherCapability()
    {
        var engine = new PermissionDecisionEngine();
        engine.Grant("workspace.run-command");

        Assert.Equal(PermissionDecisionKind.Allowed,
            engine.Evaluate("workspace.run-command", CapabilityRiskClass.Restricted, true, "Run command").Kind);
        Assert.Equal(PermissionDecisionKind.Ask,
            engine.Evaluate("workspace.write-file", CapabilityRiskClass.Consequential, true, "Write file").Kind);
    }
}
