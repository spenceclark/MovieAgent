using Microsoft.Extensions.AI;
using MovieAgent.Core.Configuration;

namespace MovieAgent.Llm.Providers;

/// <summary>
/// Builds the innermost <see cref="IChatClient"/> for one provider. Adding a new backend
/// means adding one implementation of this and registering it — nothing downstream of
/// <see cref="IChatClient"/> changes.
/// </summary>
public interface IChatClientProvider
{
    LlmProvider Provider { get; }

    IChatClient Create();
}
