using BlinkChatBackend.Models;

namespace BlinkChatBackend.Services;

public interface IAIService
{
    Task GetChatResponse(AIPrompt prompt, Stream responseStream);
    Task GetRAGResponse(AIPrompt prompt, Stream responseStream);
    Task GetRAGResponseVector(string prompt, Stream responseStream);
    Task GetRAGResponseVectorFromDocker(string prompt, Stream responseStream);
}
