using BlinkChatBackend.Models;

namespace BlinkChatBackend.Services;

public interface IAIService
{
    Task GetChatResponse(AIPrompt prompt, Stream responseStream);
    void GetRAGResponse(string prompt, Stream responseStream);
    Task GetRAGResponseVector(string prompt, Stream responseStream);
}
