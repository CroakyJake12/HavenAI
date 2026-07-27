namespace Haven.Core;

/// <summary>
/// Tool capability values.
/// </summary>
public enum ToolCapability
{
    Text = 0,
    Vision = 1,
    Tools = 2,
    Browser = 3,
    ComputerUse = 4,
    WebSearch = 5,
    Embeddings = 6,
    Streaming = 7,
    StructuredOutput = 8,
    Reranking = 9,
    PromptCaching = 10,
    UsageReporting = 11,
    AudioInput = 12,
    AudioOutput = 13
}
/// <summary>
/// Effort level values.
/// </summary>
public enum EffortLevel { Low, Medium, High, Max }
/// <summary>
/// Permission mode values.
/// </summary>
public enum PermissionMode { Ask, AutoSafe, FullAccess }
/// <summary>
/// Container resource kind values.
/// </summary>
public enum ContainerResourceKind { Text = 0, Document = 1, Image = 2 }
