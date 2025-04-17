using BlinkChatBackend.Models;

namespace BlinkChatBackend.Services
{
    public interface IAIService
    {
        Task GetResponse(AIPrompt prompt,Stream responseStream);
    }
}
