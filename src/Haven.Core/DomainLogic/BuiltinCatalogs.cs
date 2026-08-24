namespace Haven.Core;

/// <summary>Built-in reusable prompt definitions.</summary>
public static class PromptCatalog
{
    public static readonly IReadOnlyList<PromptDefinition> BuiltIns =
    [
        BuiltIn("Plan", "Create a precise plan without making changes.", "plan", "Plan only. Inspect enough to remove uncertainty, identify acceptance checks, and do not modify files or perform irreversible actions.", agentic: true),
        BuiltIn("Build", "Inspect enough, then implement and validate.", "build", "Do the minimum safe setup, implement the complete requested change, and validate it with real evidence.", agentic: true),
        BuiltIn("Debug", "Reproduce, isolate, fix, and verify a fault.", "debug", "Focus on the reported fault. Explain the error in plain English, point to its cause, reproduce it, make the smallest correct fix, and verify it.", agentic: true),
        BuiltIn("Production", "Finish the work to production quality.", "production", "Complete edge cases, tests, packaging, accessibility, rollback considerations, and honest validation before finishing.", agentic: true),
        BuiltIn("ExtendedValidation", "Validate each section and the completed result.", "extended-validation", "Run section-level checks and a final detailed validation. Separate verified facts from assumptions.", agentic: true),
        BuiltIn("Report", "Perform deep, source-driven research and return a structured report.", "report", "Research the question deeply. Define scope, gather multiple current and primary sources where possible, reconcile disagreements, cite claims, and return an executive summary, findings, limitations, and sources."),
        BuiltIn("Inspect", "Inspect code or documents and return an improvement report.", "inspect", "Inspect the supplied material without changing it. Find correctness, security, accessibility, maintainability, grammar, clarity, and consistency issues as relevant. Rank findings by impact and return a report with evidence and suggested fixes."),
        BuiltIn("Rapid", "Prioritise the shortest useful answer.", "rapid", "Answer as concisely as possible. For a yes-or-no question, lead with yes or no. Add only information needed to avoid being misleading."),
        BuiltIn("Explain", "Explain thoroughly with reasoning and learning aids.", "explain", "Explain each important step with why it matters, alternatives, common mistakes, and a useful mental model. Calibrate depth to the user's knowledge."),
        BuiltIn("Experiment", "Explore multiple viable approaches.", "experiment", "Develop at least two meaningfully different approaches, keep their assumptions separate, test or compare them consistently, and explain the trade-offs."),
        BuiltIn("GoldenRules", "Apply the project's non-negotiable rules.", "golden-rules", "Treat explicit golden rules in the conversation and project instructions as non-negotiable acceptance criteria. Surface conflicts before acting.", persists: true),
        BuiltIn("Rigid", "Stay strictly within the requested scope.", "rigid", "Follow the user's stated scope as closely as possible. Do not add unrequested product features or broaden the task; ask before any material expansion."),
        BuiltIn("StressTest", "Find edge cases, contradictions, and abuse paths.", "stress-test", "Adversarially test assumptions, edge cases, contradictions, failure modes, security and abuse paths. Rank risks and propose proportionate mitigations."),
        BuiltIn("Context", "Register durable context for this conversation.", "context", "Register the user's supplied context in Haven's context store. Do not produce a handoff. Confirm what was registered and flag contradictions."),
        BuiltIn("Handoff", "Create a portable, evidence-backed handoff.", "handoff", "Produce a structured handoff with objective, current state, decisions, constraints, changed files, verification, unresolved risks, and exact next steps. In Tasks or Studio attach evidence and timestamps to claims about completed work.", agentic: true)
    ];

    private static PromptDefinition BuiltIn(string name, string description, string icon, string instructions, bool persists = false, bool agentic = false, string allowedModes = "[]") =>
        new(GuidUtility.FromStableName($"haven.prompt.{name}"), name, description, icon, instructions, persists, true, true, DateTimeOffset.UnixEpoch, agentic, allowedModes);
}

/// <summary>Built-in agent definitions.</summary>
public static class AgentCatalog
{
    public static readonly IReadOnlyList<AgentDefinition> BuiltIns =
    [
        new(GuidUtility.FromStableName("haven.agent.default"), "Default", "Normal Haven behaviour.", "Be helpful, accurate and direct.", "agent-default", "", null, "", "{}", true, true, DateTimeOffset.UnixEpoch),
        new(GuidUtility.FromStableName("haven.agent.auto"), "Auto", "Selects an installed agent when a request clearly matches.", "Choose a suitable enabled agent, show the reason and allow reversal.", "agent-auto", "", null, "", "{}", true, true, DateTimeOffset.UnixEpoch),
        new(GuidUtility.FromStableName("haven.agent.research"), "Research Agent", "Cross-checks current information and clearly cites evidence.", "Define scope, search multiple relevant sources, prefer primary sources, verify current facts, identify disagreement, distinguish evidence from inference, and cite factual claims.", "agent-research", "", null, "research|verify|latest|compare sources|what do experts|what do sources say", "{\"webSearch\":true}", true, true, DateTimeOffset.UnixEpoch)
    ];
}
