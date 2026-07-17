namespace Haven.Application;

/// <summary>
/// Identifies the direct loopback Ollama transport. User-facing surfaces should
/// normally depend on <see cref="IProviderModelClient"/> or <see cref="IOllamaClient"/>,
/// while provider routing uses this boundary to avoid resolving itself recursively.
/// </summary>
public interface ILocalOllamaClient : IOllamaClient
{
}
