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
    IConfiguration configuration,
    IDistributedCache distributedCache) : ILmKitService
{
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

            if (lmKitModelService.Model == null || lmKitModelService.EmbeddingModel == null)
            {
                LoadModelsFromConfigurationAsync();
            }

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

    #region [Load Models]

    public async Task LoadModelsFromConfigurationAsync()
    {
        LMKit.Licensing.LicenseManager.SetLicenseKey(configuration["LM:LicenseKey"]);

        var modelName = configuration["LM:Model"];
        var modelUri = configuration["LM:ModelUri"];
        if (lmKitModelService.Model == null && !string.IsNullOrWhiteSpace(modelName))
        {
            logger.LogInformation("Loading {modelName} model..", modelName);
            var path = Path.Combine(webHostEnvironment.WebRootPath, "models", modelName);
            if (File.Exists(path))
                lmKitModelService.LoadModel(path);
            else if (!string.IsNullOrWhiteSpace(modelUri))
                lmKitModelService.LoadModel(new Uri(modelUri), path);
            else
                throw new ArgumentException("Model name and URI is not set.");
            logger.LogInformation("Loaded model..");
        }
        else
            throw new ArgumentException("Model name and URI is not set.");

        var embeddingModelName = configuration["LM:EmbeddingModel"];
        var embeddingModelUri = configuration["LM:EmbeddingModelUri"];
        if (lmKitModelService.EmbeddingModel == null && !string.IsNullOrWhiteSpace(embeddingModelName))
        {
            logger.LogInformation("Loading {modelName} embedding model..", embeddingModelName);
            var path = Path.Combine(webHostEnvironment.WebRootPath, "models", embeddingModelName);
            if (File.Exists(path))
                lmKitModelService.LoadEmbeddingModel(path);
            else if (!string.IsNullOrWhiteSpace(embeddingModelUri))
                lmKitModelService.LoadEmbeddingModel(new Uri(embeddingModelUri), path);
            else
                throw new ArgumentException("Embedding model name and URI is not set.");
            logger.LogInformation("Loaded embedding model..");
        }

        var collectionName = configuration["LM:CollectionName"];
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName, "CollectionName is null or empty.");
        if (string.IsNullOrEmpty(lmKitModelService.CollectionName))
        {
            lmKitModelService.AddCollectionName(collectionName);
            logger.LogInformation("Collection name is set to {collectionName}.", collectionName);
        }

        var qdrantConnectionString = configuration.GetConnectionString("Qdrant");
        if (string.IsNullOrEmpty(qdrantConnectionString))
        {
            if (lmKitModelService.DataSource == null)
            {
                lmKitModelService.LoadDataSource(Path.Combine(webHostEnvironment.WebRootPath, "collections", collectionName + ".dat"));
                logger.LogInformation("Data source is loaded.");
            }

            if (lmKitModelService.RagEngine == null)
            {
                lmKitModelService.LoadRagEngine();
                logger.LogInformation("RAG engine is loaded.");
            }

            lmKitModelService.LoadDataSourceIntoRagEngine();
            logger.LogInformation("Data source is loaded into RAG engine.");

            if (Directory.Exists(Path.Combine(webHostEnvironment.WebRootPath, "source-files")))
            {
                var files = Directory.GetFiles(Path.Combine(webHostEnvironment.WebRootPath, "source-files"));
                foreach (var file in files)
                {
                    lmKitModelService.LoadFilesIntoDataSource(file, Path.GetFileNameWithoutExtension(file));
                }
                logger.LogInformation("Files are loaded into data source.");
            }

            //lmKitModelService.LoadFilesIntoDataSource(Path.Combine(webHostEnvironment.WebRootPath, "ebooks", "harekrishna.txt"), "harekrishna");
        }
        else
        {
            if (lmKitModelService.VectorStore == null)
            {
                lmKitModelService.LoadVectorStore(new Uri(qdrantConnectionString));
                logger.LogInformation("Vector store is loaded.");
            }

            if (Directory.Exists(Path.Combine(webHostEnvironment.WebRootPath, "source-files")))
            {
                var files = Directory.GetFiles(Path.Combine(webHostEnvironment.WebRootPath, "source-files"));
                foreach (var file in files)
                {
                    await lmKitModelService.LoadFilesIntoVectorDataSourceAsync(file, Path.GetFileNameWithoutExtension(file));
                }
                logger.LogInformation("Files are loaded into data source.");
            }

            if (lmKitModelService.RagEngine == null)
            {
                lmKitModelService.LoadVectorStoreRagEngine();
                logger.LogInformation("RAG engine is loaded.");
            }

            lmKitModelService.LoadDataSourceIntoVectorStoreRagEngine();
        }
    }

    #endregion [Load Models]

    #region [Model Loading Progress]

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

    #endregion [Model Loading Progress]
}
