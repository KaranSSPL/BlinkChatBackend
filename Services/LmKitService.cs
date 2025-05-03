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
                var path = Path.Combine(webHostEnvironment.WebRootPath, "models", "gemma-3-it-1B-Q4_K_M.gguf");
                if (File.Exists(path))
                    lmKitModelService.LoadModel(path);
                else
                    lmKitModelService.LoadModel(new Uri("https://huggingface.co/lm-kit/gemma-3-1b-instruct-gguf/resolve/main/gemma-3-it-1B-Q4_K_M.gguf?download=true"), path);
            }

            if (lmKitModelService.EmbeddingModel == null)
            {
                var path = Path.Combine(webHostEnvironment.WebRootPath, "models", "bge-small-en-v1.5-f16.gguf");
                if (File.Exists(path))
                    lmKitModelService.LoadEmbeddingModel(path);
                else
                    lmKitModelService.LoadEmbeddingModel(new Uri("https://huggingface.co/lm-kit/bge-1.5-gguf/resolve/main/bge-small-en-v1.5-f16.gguf?download=true"), path);
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

            lmKitModelService.LoadFilesIntoDataSource(Path.Combine(webHostEnvironment.WebRootPath, "ebooks", "harekrishna.txt"), "harekrishna");

            using var chat = new MultiTurnConversation(lmKitModelService.Model, LoadChatHistory(request.SessionId))
            {
                MaximumCompletionTokens = 512,
                SamplingMode = new GreedyDecoding(),
                SystemPrompt = "You are a chatbot that only responds to questions that are related to .Net. Simply reply with 'I don't know' when prompt is not related to .Net.",
            };

            chat!.AfterTokenSampling += async (sender, token) =>
            {
                if (token.TextChunk == "<|im_end|>" || token.TextChunk == "<end_of_turn>")
                {
                    await SaveChatHistoryAsync(chat.ChatHistory, request.SessionId);
                    return;
                }
                var buffer = Encoding.UTF8.GetBytes(token.TextChunk);
                await httpResponse.Body.WriteAsync(buffer, cancellationToken);
                await httpResponse.Body.FlushAsync(cancellationToken);
            };

            if (request.Reset)
            {
                distributedCache.Remove(request.SessionId);
                chat.ClearHistory();

                var buffer = Encoding.UTF8.GetBytes("Chat history has been reset.");
                await httpResponse.Body.WriteAsync(buffer, cancellationToken);
                await httpResponse.Body.FlushAsync(cancellationToken);
            }

            if (request.Regenerate)
            {
                await chat.RegenerateResponseAsync(new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);
                return;
            }

            // Determine the number of top partitions to select based on GPU support.
            // If GPU is available, select the top 3 partitions; otherwise, select only the top 1 to maintain acceptable speed.
            int topK = Runtime.HasGpuSupport ? 3 : 1;
            List<PartitionSimilarity>? partitions = lmKitModelService.RagEngine!
                .FindMatchingPartitions(request.Question, topK, cancellationToken: cancellationToken);
            if (partitions != null && partitions.Count > 0)
            {
                logger.LogInformation("Answer from {SectionIdentifier}", partitions[0].SectionIdentifier);
                await lmKitModelService.RagEngine.QueryPartitionsAsync(request.Question, partitions, chat, cancellationToken);
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
