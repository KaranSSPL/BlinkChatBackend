using BlinkChatBackend.Models;
using LMKit.Model;
using LMKit.TextGeneration;
using LMKit.TextGeneration.Chat;
using LMKit.TextGeneration.Sampling;
using System.Text;

namespace BlinkChatBackend.Services;

public class AIService : IAIService
{
    readonly LM _model;
    static ChatHistory? _history;

    public AIService(LM model)
    {
        _model = model;
    }

    public async Task GetChatResponse(AIPrompt prompt, Stream responseStream)
    {
        using (var chat = new MultiTurnConversation(_model, _history ?? new ChatHistory(_model))
        {
            MaximumCompletionTokens = 1000,
            SamplingMode = new RandomSampling()
            {
                Temperature = 0.8f
            },
            SystemPrompt = "You are a chatbot that always responds promptly and helpfully to user requests. You only respond to questions that are related to .Net, C#, .Net ecosystem or any .net framework or .net versions. You will politely reject any questions that are not related to .Net"
        })
        {
            chat.AfterTokenSampling += async (sender, token) =>
            {
                if (token.TextChunk == "<|im_end|>")
                {
                    _history = chat.ChatHistory;
                    return;
                }
                var buffer = Encoding.UTF8.GetBytes(token.TextChunk);
                await responseStream.WriteAsync(buffer);
                await responseStream.FlushAsync();
            };

            await chat.SubmitAsync(new Prompt(prompt.Query), new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);
        }
    }
}
