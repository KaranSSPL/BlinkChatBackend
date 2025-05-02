using BlinkChatBackend.Helpers;
using BlinkChatBackend.Models;
using BlinkChatBackend.Services.Interfaces;
using LMKit.Data;
using LMKit.Global;
using LMKit.Model;
using LMKit.Retrieval;
using LMKit.TextGeneration;
using LMKit.TextGeneration.Chat;
using LMKit.TextGeneration.Sampling;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using System.Text;
using static LMKit.Retrieval.RagEngine;

namespace BlinkChatBackend.Services;

public class AIService(LM model, IDistributedCache distributedCache, IWebHostEnvironment webHostEnvironment, BlinkChatContext context) : IAIService
{
    DataSource? dataSource;
    RagEngine? ragEngine;
    const string COLLECTION_NAME = "Ebooks";

    #region [Public Methods]
    public async Task GetChatResponse(AIPrompt prompt, Stream responseStream)
    {
        ChatHistory history = LoadChatHistory(prompt.SessionId);
        // ToDo: Should check chat length if it is too long send error.
        using var chat = new MultiTurnConversation(model, history)
        {
            MaximumCompletionTokens = 1000,
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
                await SaveChatHistory(chat.ChatHistory, prompt.SessionId);
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
            distributedCache.Remove(prompt.SessionId);
            chat.ClearHistory();
        }
        else
            await chat.SubmitAsync(new Prompt(prompt.Query), new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);
    }

    public void GetRAGResponse(string prompt, Stream responseStream)
    {
        using (LM _embeddingModel = new LM("https://huggingface.co/lm-kit/bge-1.5-gguf/resolve/main/bge-small-en-v1.5-f16.gguf?download=true"))
        {
            const string DATA_SOURCE_PATH = COLLECTION_NAME + ".dat";

            if (File.Exists(DATA_SOURCE_PATH))
            {
                dataSource = DataSource.LoadFromFile(DATA_SOURCE_PATH, _embeddingModel, readOnly: false);
            }
            else
            {
                dataSource = DataSource.CreateFileDataSource(DATA_SOURCE_PATH, COLLECTION_NAME, _embeddingModel);

            }

            ragEngine = new RagEngine(_embeddingModel);

            ragEngine.AddDataSource(dataSource);

            //LoadEbook("Architecting-Modern-Web-Applications-with-ASP.NET-Core-and-Azure.txt", "Architecting Modern Web Applications with ASP.NET Core and Azure");
            LoadEbook("Intro.txt", "Intro");

            var chat = new SingleTurnConversation(model)
            {
                SamplingMode = new GreedyDecoding(),
                SystemPrompt = "You are an expert RAG assistant,  that only answers questions using the provided context. If the answer cannot be found in the context, respond with: 'I don’t know.' Do not use outside knowledge or make assumptions."
            };

            chat.AfterTextCompletion += async (sender, token) =>
            {
                if (token.Text == "<|im_end|>")
                    return;
                var buffer = Encoding.UTF8.GetBytes(token.Text);
                await responseStream.WriteAsync(buffer);
                await responseStream.FlushAsync();
            };

            // Determine the number of top partitions to select based on GPU support.
            // If GPU is available, select the top 3 partitions; otherwise, select only the top 1 to maintain acceptable speed.
            int topK = Runtime.HasGpuSupport ? 3 : 1;
            List<PartitionSimilarity> partitions = ragEngine.FindMatchingPartitions(prompt, topK, forceUniqueSection: true);

            if (partitions.Count > 0)
            {
                _ = ragEngine.QueryPartitions(prompt, partitions, chat);
            }
            else
            {
                var buffer = Encoding.UTF8.GetBytes("No relevant information found in the loaded sources to answer your query. Please try asking a different question.");
                Task.Run(async () => await responseStream.WriteAsync(buffer));
                Task.Run(async () => await responseStream.FlushAsync());
            }
        }
    }

    public async Task GetRAGResponseVector(string prompt, Stream responseStream)
    {
        using LM embeddingModel = new("https://huggingface.co/lm-kit/bge-1.5-gguf/resolve/main/bge-small-en-v1.5-f16.gguf?download=true");
        var store = new SqlEmbeddingStore(context);

        ragEngine = new RagEngine(embeddingModel, store);
        if (await store.CollectionExistsAsync(COLLECTION_NAME))
        {
            dataSource = DataSource.LoadFromStore(store, COLLECTION_NAME, embeddingModel);
        }
        else
        {
            string path = Path.Combine(webHostEnvironment.ContentRootPath, "wwwroot", COLLECTION_NAME, "Intro.txt");
            string eBookContent = File.ReadAllText(path);
            dataSource = ragEngine.ImportText(eBookContent, new TextChunking() { MaxChunkSize = 500 }, COLLECTION_NAME, "default");
        }

        ragEngine.AddDataSource(dataSource);

        var chat = new SingleTurnConversation(model)
        {
            SamplingMode = new GreedyDecoding(),
            SystemPrompt = "You are an expert RAG assistant, that only answers questions using the provided context. If the answer cannot be found in the context, respond with: 'I don’t know.' Do not use outside knowledge or make assumptions."
        };

        chat.AfterTextCompletion += async (sender, token) =>
        {
            if (token.Text == "<|im_end|>")
                return;
            var buffer = Encoding.UTF8.GetBytes(token.Text);
            await responseStream.WriteAsync(buffer);
            await responseStream.FlushAsync();
        };

        // Determine the number of top partitions to select based on GPU support.
        // If GPU is available, select the top 3 partitions; otherwise, select only the top 1 to maintain acceptable speed.
        int topK = Runtime.HasGpuSupport ? 3 : 1;
        List<PartitionSimilarity> partitions = ragEngine.FindMatchingPartitions(prompt, topK, forceUniqueSection: true);

        if (partitions.Count > 0)
        {
            _ = ragEngine.QueryPartitions(prompt, partitions, chat);
        }
        else
        {
            var buffer = Encoding.UTF8.GetBytes("No relevant information found in the loaded sources to answer your query. Please try asking a different question.");
            await responseStream.WriteAsync(buffer);
            await responseStream.FlushAsync();
        }
    }

    public async Task GetRAGResponseVectorFromDocker(string prompt, Stream responseStream)
    {
        using LM embeddingModel = new("https://huggingface.co/lm-kit/bge-1.5-gguf/resolve/main/bge-small-en-v1.5-f16.gguf?download=true");
        var qdrantStore = new DummyQdrantStore(new Uri("http://localhost:6334"));

        ragEngine = new RagEngine(embeddingModel, qdrantStore);
        if (await qdrantStore.CollectionExistsAsync(COLLECTION_NAME))
        {
            dataSource = DataSource.LoadFromStore(qdrantStore, COLLECTION_NAME, embeddingModel);
        }
        else
        {
            string path = Path.Combine(webHostEnvironment.ContentRootPath, "wwwroot", COLLECTION_NAME, "Intro.txt");
            string eBookContent = File.ReadAllText(path);
            dataSource = ragEngine.ImportText(eBookContent, new TextChunking() { MaxChunkSize = 500 }, COLLECTION_NAME, "default");
        }

        ragEngine.ClearDataSources();
        ragEngine.AddDataSource(dataSource);

        var chat = new SingleTurnConversation(model)
        {
            SamplingMode = new GreedyDecoding(),
            SystemPrompt = "You are an expert RAG assistant,  that only answers questions using the provided context. If the answer cannot be found in the context, respond with: 'I don’t know.' Do not use outside knowledge or make assumptions."
        };

        chat.AfterTextCompletion += async (sender, token) =>
        {
            if (token.Text == "<|im_end|>")
                return;
            var buffer = Encoding.UTF8.GetBytes(token.Text);
            await responseStream.WriteAsync(buffer);
            await responseStream.FlushAsync();
        };

        // Determine the number of top partitions to select based on GPU support.
        // If GPU is available, select the top 3 partitions; otherwise, select only the top 1 to maintain acceptable speed.
        int topK = Runtime.HasGpuSupport ? 3 : 1;
        List<PartitionSimilarity> partitions = ragEngine.FindMatchingPartitions(prompt, topK, forceUniqueSection: true);

        if (partitions.Count > 0)
        {
            _ = ragEngine.QueryPartitions(prompt, partitions, chat);
        }
        else
        {
            var buffer = Encoding.UTF8.GetBytes("No relevant information found in the loaded sources to answer your query. Please try asking a different question.");
            await responseStream.WriteAsync(buffer);
            await responseStream.FlushAsync();
        }
    }
    #endregion

    #region [Private Methods]
    private async Task SaveChatHistory(ChatHistory chatHistory, string sessionId)
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
        await distributedCache.SetStringAsync(sessionId, json);
    }
    private ChatHistory LoadChatHistory(string sessionId)
    {
        var chatHistory = new ChatHistory(model);
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
    private void LoadEbook(string fileName, string sectionIdentifier)
    {
        if (dataSource.HasSection(sectionIdentifier))
            return;  //we already have this ebook in the collection

        string path = Path.Combine(webHostEnvironment.ContentRootPath, "wwwroot", COLLECTION_NAME, fileName);
        //importing the ebook into a new section
        string eBookContent = File.ReadAllText(path);
        ragEngine.ImportText(eBookContent, new TextChunking() { MaxChunkSize = 500 }, COLLECTION_NAME, sectionIdentifier);
    }
    #endregion

}

