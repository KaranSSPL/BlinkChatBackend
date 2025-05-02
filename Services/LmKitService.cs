using BlinkChatBackend.Models;
using BlinkChatBackend.Services.Interfaces;
using LMKit.Global;
using LMKit.Retrieval;
using LMKit.TextGeneration;
using LMKit.TextGeneration.Chat;
using LMKit.TextGeneration.Sampling;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using System.Text;

namespace BlinkChatBackend.Services;

// Without license, chat session will be closed after some time.
public class LmKitService(ILogger<LmKitService> logger,
    IHttpContextAccessor httpContextAccessor,
    ILmKitModelService lmKitModelService,
    IWebHostEnvironment webHostEnvironment,
    IDistributedCache distributedCache) : ILmKitService
{
    private const string CollectionName = "Ebooks";

    public async Task GenerateResponseAsync(UserRequest request, CancellationToken cancellationToken)
    {
        var httpResponse = httpContextAccessor.HttpContext?.Response;
        if (httpResponse == null)
        {
            logger.LogWarning("Http Response is null.");
            return;
        }

        if (request == null)
        {
            logger.LogWarning("User request is null.");
            httpResponse.StatusCode = StatusCodes.Status400BadRequest;
            await httpResponse.WriteAsync("Invalid parameters.", cancellationToken: cancellationToken);
            return;
        }

        try
        {
            httpResponse.ContentType = "text/event-stream; charset=utf-8";

            if (lmKitModelService.Model == null)
            {
                lmKitModelService.LoadModel(Path.Combine(webHostEnvironment.WebRootPath, "Models", "gemma-3-4b-it-Q4_K_M.lmk"));
            }

            if (lmKitModelService.EmbeddingModel == null)
            {
                lmKitModelService.LoadEmbeddingModel(Path.Combine(webHostEnvironment.WebRootPath, "Models", "gemma-3-4b-it-Q4_K_M.lmk"));
            }

            if (string.IsNullOrEmpty(lmKitModelService.CollectionName))
            {
                lmKitModelService.AddCollectionName(CollectionName);
            }

            if (lmKitModelService.DataSource == null)
            {
                lmKitModelService.LoadDataSource(CollectionName + ".dat");
            }

            if (lmKitModelService.RagEngine == null)
            {
                lmKitModelService.LoadRagEngine();
            }

            lmKitModelService.LoadDataSourceIntoRagEngine();

            lmKitModelService.LoadFilesIntoDataSource(Path.Combine(webHostEnvironment.WebRootPath, "Ebooks", "Architecting-Modern-Web-Applications-with-ASP.NET-Core-and-Azure.txt"), "ASP .NET Core");

            using var chat = new MultiTurnConversation(lmKitModelService.Model, LoadChatHistory(request.SessionId))
            {
                // Using defaults.
                MaximumCompletionTokens = 512,
                SamplingMode = new RandomSampling()
                {
                    Temperature = 0.8f
                },
                SystemPrompt = "You are a chatbot that only responds to questions that are related to .Net. Simply reply with 'I don't know' when prompt is not related to .Net.",
            };

            chat.AfterTokenSampling += async (sender, token) =>
            {
                if (token.TextChunk == "<|im_end|>")
                {
                    await SaveChatHistoryAsync(chat.ChatHistory, request.SessionId);
                    return;
                }
                var buffer = Encoding.UTF8.GetBytes(token.TextChunk);
                await httpResponse.Body.WriteAsync(buffer, cancellationToken);
                await httpResponse.Body.FlushAsync(cancellationToken);
            };

            // Determine the number of top partitions to select based on GPU support.
            // If GPU is available, select the top 3 partitions; otherwise, select only the top 1 to maintain acceptable speed.
            int topK = Runtime.HasGpuSupport ? 3 : 1;
            List<PartitionSimilarity>? partitions = lmKitModelService.RagEngine?
                .FindMatchingPartitions(request.Question, topK, forceUniqueSection: true, cancellationToken: cancellationToken);

            if (request.Regenerate)
            {
                await chat.RegenerateResponseAsync(new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);
            }
            else if (request.Reset)
            {
                distributedCache.Remove(request.SessionId);
                chat.ClearHistory();
            }
            else if (partitions != null && partitions.Count > 0)
            {
                logger.LogInformation("Answer from {SectionIdentifier}", partitions[0].SectionIdentifier);
                _ = lmKitModelService.RagEngine?.QueryPartitions(request.Question, partitions, chat, cancellationToken);
            }
            else
            {
                var buffer = Encoding.UTF8.GetBytes("No relevant information found in the loaded sources to answer your query. Please try asking a different question.");
                await httpResponse.Body.WriteAsync(buffer, cancellationToken);
                await httpResponse.Body.FlushAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate response.");
            if (!httpResponse.HasStarted)
            {
                httpResponse.StatusCode = StatusCodes.Status500InternalServerError;
                await httpResponse.WriteAsync(ex.Message, cancellationToken: cancellationToken);
            }
            // Should handle response whe response already started.
        }
    }

    private ChatHistory LoadChatHistory(string sessionId)
    {
        var chatHistory = new ChatHistory(lmKitModelService.Model);
        var json = distributedCache.GetString(sessionId);
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
    private async Task SaveChatHistoryAsync(ChatHistory chatHistory, string sessionId)
    {
        IList<Message> message = [];
        foreach (var msg in chatHistory.Messages)
        {
            message.Add(new Message
            {
                Role = msg.AuthorRole.ToString(),
                Content = msg.Content
            });
        }
        var json = JsonConvert.SerializeObject(message);
        await distributedCache.SetStringAsync(sessionId, json);
    }
}
