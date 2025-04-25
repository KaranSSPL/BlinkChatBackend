using BlinkChatBackend.Models;
using LMKit.Model;
using LMKit.TextGeneration;
using LMKit.TextGeneration.Chat;
using LMKit.TextGeneration.Sampling;
using Microsoft.Extensions.Caching.Distributed;
using System.Text;
using Newtonsoft.Json;

namespace BlinkChatBackend.Services;

public class AIService : IAIService
{
    readonly LM _model;
    public IDistributedCache _distributedCache { get; }

    public AIService(LM model, IDistributedCache distributedCache)
    {
        _model = model;
        _distributedCache = distributedCache;
    }

    public async Task GetChatResponse(AIPrompt prompt, Stream responseStream)
    {
        ChatHistory history = LoadChatHistory(prompt.SessionId);
        using (var chat = new MultiTurnConversation(_model, history)
        {
            MaximumCompletionTokens = 1000,
            SamplingMode = new RandomSampling()
            {
                Temperature = 0.8f
            },
            SystemPrompt = "You are a chatbot that only responds to questions that are related to .Net. Simply reply with 'I don't know' when prompt is not related to .Net.",
        })
        {
            chat.AfterTokenSampling += async (sender, token) =>
            {
                if (token.TextChunk == "<|im_end|>")
                {
                    await SaveChatHistory(chat.ChatHistory,prompt.SessionId);
                    return;
                }
                var buffer = Encoding.UTF8.GetBytes(token.TextChunk);
                await responseStream.WriteAsync(buffer);
                await responseStream.FlushAsync();
            };

            if (prompt.Regenerate)
                await chat.RegenerateResponseAsync(new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);
            else if (prompt.Reset)
            {
                _distributedCache.Remove(prompt.SessionId);
                chat.ClearHistory();
            }
            else
                await chat.SubmitAsync(new Prompt(prompt.Query), new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);
        }
    }

    private async Task SaveChatHistory(ChatHistory chatHistory,string sessionId)
    {
        IList<Message> messges = [];
        foreach (var msg in chatHistory.Messages)
        {
            messges.Add(new Message
            {
                Role = msg.AuthorRole.ToString(),
                Content = msg.Content
            });
        }
        var json = JsonConvert.SerializeObject(messges);
        await _distributedCache.SetStringAsync(sessionId, json);
    }
    private ChatHistory LoadChatHistory(string sessionId)
    {
        var chatHistory = new ChatHistory(_model);
        var json = _distributedCache.GetString(sessionId);
        if (json != null && json != "")
        {
            var messages = JsonConvert.DeserializeObject<IList<Message>>(json);
            if (messages != null)
                foreach (var msg in messages)
                {
                    if (msg.Role == "User")
                        chatHistory.AddMessage(AuthorRole.User, msg.Content);
                    else if (msg.Role == "Assistant")
                        chatHistory.AddMessage(AuthorRole.Assistant, msg.Content);
                    else if (msg.Role == "System")
                        chatHistory.AddMessage(AuthorRole.System, msg.Content);
                }
        }
        return chatHistory;
    }
}

