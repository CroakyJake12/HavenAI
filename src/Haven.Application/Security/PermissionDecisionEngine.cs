using Haven.Core;

namespace Haven.Application;

public enum HavenPermissionPolicy
{
    AlwaysAsk = 0,
    AskWithHighRisk = 1,
    AlwaysAllow = 2
}

public enum PermissionDecisionKind
{
    Allowed = 0,
    Ask = 1,
    Denied = 2
}

public sealed record PermissionDecision(
    PermissionDecisionKind Kind,
    string Scope,
    string Reason);

public interface IPermissionDecisionEngine
{
    HavenPermissionPolicy Policy { get; }
    PermissionDecision Evaluate(string scope, CapabilityRiskClass risk, bool requiresPermission, string reason);
    void SetPolicy(HavenPermissionPolicy policy);
    void Grant(string scope);
    void Revoke(string scope);
    IReadOnlySet<string> Grants { get; }
}

/// <summary>
/// Central risk-based Haven approval policy. A grant is scoped to one
/// capability/action key and never becomes authority for unrelated actions.
/// </summary>
public sealed class PermissionDecisionEngine : IPermissionDecisionEngine
{
    private readonly object _gate = new();
    private readonly HashSet<string> _grants = new(StringComparer.OrdinalIgnoreCase);
    private HavenPermissionPolicy _policy = HavenPermissionPolicy.AlwaysAsk;

    public HavenPermissionPolicy Policy
    {
        get { lock (_gate) return _policy; }
    }

    public IReadOnlySet<string> Grants
    {
        get { lock (_gate) return new HashSet<string>(_grants, StringComparer.OrdinalIgnoreCase); }
    }

    public PermissionDecision Evaluate(string scope, CapabilityRiskClass risk, bool requiresPermission, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        lock (_gate)
        {
            if (!requiresPermission || _grants.Contains(scope))
                return new(PermissionDecisionKind.Allowed, scope, reason);
            var ask = _policy switch
            {
                HavenPermissionPolicy.AlwaysAsk => true,
                HavenPermissionPolicy.AskWithHighRisk => risk >= CapabilityRiskClass.Consequential,
                HavenPermissionPolicy.AlwaysAllow => false,
                _ => true
            };
            return ask
                ? new(PermissionDecisionKind.Ask, scope, reason)
                : new(PermissionDecisionKind.Allowed, scope, reason);
        }
    }

    public void SetPolicy(HavenPermissionPolicy policy)
    {
        lock (_gate) _policy = policy;
    }

    public void Grant(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        lock (_gate) _grants.Add(scope);
    }

    public void Revoke(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return;
        lock (_gate) _grants.Remove(scope);
    }
}
