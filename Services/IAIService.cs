using BlinkChatBackend.Models;

namespace BlinkChatBackend.Services;

public interface IAIService
{
    Task GetChatResponse(AIPrompt prompt, Stream responseStream);
}
