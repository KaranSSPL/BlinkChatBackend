using BlinkChatBackend.Models;
using LMKit.Model;
using LMKit.TextGeneration;
using LMKit.TextGeneration.Chat;
using LMKit.TextGeneration.Sampling;
using Microsoft.Extensions.Caching.Distributed;
using System.Text;
using Newtonsoft.Json;
using LMKit.Global;
using LMKit.Retrieval;
using static LMKit.Retrieval.RagEngine;
using LMKit.Data;
using LMKit.Data.Storage.Qdrant;
using LMKit.Data.Storage;
using System.Diagnostics;
using BlinkChatBackend.Helpers;

namespace BlinkChatBackend.Services;

public class AIService : IAIService
{
    readonly LM _model;
    DataSource _dataSource;
    RagEngine _ragEngine;
    const string COLLECTION_NAME = "Ebooks";
    public IDistributedCache _distributedCache;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly BlinkChatContext _context;

    public AIService(LM model, IDistributedCache distributedCache, IWebHostEnvironment webHostEnvironment,BlinkChatContext context)
    {
        _model = model;
        _distributedCache = distributedCache;
        _webHostEnvironment = webHostEnvironment;
        _context = context;
    }

    #region [Public Methods]
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
                _distributedCache.Remove(prompt.SessionId);
                chat.ClearHistory();
            }
            else
                await chat.SubmitAsync(new Prompt(prompt.Query), new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);
        }
    }

    public void GetRAGResponse(string prompt, Stream responseStream)
    {
        using (LM _embeddingModel = new LM("https://huggingface.co/lm-kit/bge-1.5-gguf/resolve/main/bge-small-en-v1.5-f16.gguf?download=true"))
        {
            const string DATA_SOURCE_PATH = COLLECTION_NAME + ".dat";

            if (File.Exists(DATA_SOURCE_PATH))
            {
                _dataSource = DataSource.LoadFromFile(DATA_SOURCE_PATH, _embeddingModel, readOnly: false);
            }
            else
            {
                _dataSource = DataSource.CreateFileDataSource(DATA_SOURCE_PATH, COLLECTION_NAME, _embeddingModel);
                
            }

            _ragEngine = new RagEngine(_embeddingModel);

            _ragEngine.AddDataSource(_dataSource);

            //LoadEbook("Architecting-Modern-Web-Applications-with-ASP.NET-Core-and-Azure.txt", "Architecting Modern Web Applications with ASP.NET Core and Azure");
            LoadEbook("Intro.txt", "Intro");

            var chat = new SingleTurnConversation(_model)
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
            List<TextPartitionSimilarity> partitions = _ragEngine.FindMatchingPartitions(prompt, topK, forceUniqueSection: true);

            if (partitions.Count > 0)
            {
                _ = _ragEngine.QueryPartitions(prompt, partitions, chat);
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
        var v=_context.embeddings.ToList();
        using (LM _embeddingModel = new LM("https://huggingface.co/lm-kit/bge-1.5-gguf/resolve/main/bge-small-en-v1.5-f16.gguf?download=true"))
        {

            IVectorStore _store = new SqlEmbeddingStore(_context);

            _ragEngine = new RagEngine(_embeddingModel,_store);

            try
            {
                await _store.CollectionExistsAsync(COLLECTION_NAME);
            }
            catch (Exception)
            {
                throw;
            }
            if (await _store.CollectionExistsAsync(COLLECTION_NAME))
            {
                _dataSource=DataSource.LoadFromStore(_store, COLLECTION_NAME, _embeddingModel);
            }
            else
            {
                string path = Path.Combine(_webHostEnvironment.ContentRootPath, "wwwroot", COLLECTION_NAME, "Intro.txt");
                string eBookContent = File.ReadAllText(path);
                _dataSource=_ragEngine.ImportText(eBookContent, new TextChunking() { MaxChunkSize = 500 }, COLLECTION_NAME, "default");
            }

            _ragEngine.AddDataSource(_dataSource);

            var chat = new SingleTurnConversation(_model)
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
            List<TextPartitionSimilarity> partitions = _ragEngine.FindMatchingPartitions(prompt, topK, forceUniqueSection: true);

            if (partitions.Count > 0)
            {
                _ = _ragEngine.QueryPartitions(prompt, partitions, chat);
            }
            else
            {
                var buffer = Encoding.UTF8.GetBytes("No relevant information found in the loaded sources to answer your query. Please try asking a different question.");
                await responseStream.WriteAsync(buffer);
                await responseStream.FlushAsync();
            }
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
    private void LoadEbook(string fileName, string sectionIdentifier)
    {
        if (_dataSource.HasSection(sectionIdentifier))
            return;  //we already have this ebook in the collection

        string path = Path.Combine(_webHostEnvironment.ContentRootPath, "wwwroot", COLLECTION_NAME, fileName);
        //importing the ebook into a new section
        string eBookContent = File.ReadAllText(path);
        _ragEngine.ImportText(eBookContent, new TextChunking() { MaxChunkSize = 500 }, COLLECTION_NAME, sectionIdentifier);
    }
    #endregion

}

