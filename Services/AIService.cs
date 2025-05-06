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
using LMKit.TextGeneration.Events;

namespace BlinkChatBackend.Services;

public class AIService : IAIService
{
    readonly LM _model;
    readonly LM _embeddingModel;
    DataSource _dataSource;
    RagEngine _ragEngine;
    const string COLLECTION_NAME = "Ebooks";
    public IDistributedCache _distributedCache;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly BlinkChatContext _context;
    private MultiTurnConversation? _multiTurnConversation;
    private AsyncLocal<Stream> _currentResponseStream = new AsyncLocal<Stream>();
    private AsyncLocal<string> _currentSessionId = new AsyncLocal<string>();
    public AIService(LM model, IDistributedCache distributedCache, IWebHostEnvironment webHostEnvironment,BlinkChatContext context)
    {
        _model = model;
        _distributedCache = distributedCache;
        _webHostEnvironment = webHostEnvironment;
        _context = context;
        _embeddingModel = new LM("https://huggingface.co/lm-kit/bge-1.5-gguf/resolve/main/bge-small-en-v1.5-f16.gguf?download=true");
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

    public async Task GetRAGResponse(AIPrompt prompt, Stream responseStream)
    {
        if (_multiTurnConversation == null)
        {
            _multiTurnConversation = new MultiTurnConversation(_model, LoadChatHistory(prompt.SessionId))
            {
                SamplingMode = new GreedyDecoding(),
                SystemPrompt = "You are an expert RAG assistant,  that only answers questions using the provided conversation Transcript."
            };

            _multiTurnConversation.AfterTokenSampling += MultiTurnConversation_AfterTokenSampling;

            const string DATA_SOURCE_PATH = COLLECTION_NAME + ".dat";

            if (File.Exists(DATA_SOURCE_PATH))
            {
                _dataSource = DataSource.LoadFromFile(DATA_SOURCE_PATH, _embeddingModel, readOnly: false);
            }
            else
            {
                _dataSource = DataSource.CreateFileDataSource(DATA_SOURCE_PATH, COLLECTION_NAME, _embeddingModel);
            }
        }

        _currentResponseStream.Value = responseStream;
        _currentSessionId.Value = prompt.SessionId;
        _ragEngine = new RagEngine(_embeddingModel);

        _ragEngine.AddDataSource(_dataSource);

        //LoadEbook("Architecting-Modern-Web-Applications-with-ASP.NET-Core-and-Azure.txt", "Architecting Modern Web Applications with ASP.NET Core and Azure");
        LoadEbook("harekrishna.txt", "harekrishna");

        // Determine the number of top partitions to select based on GPU support.
        // If GPU is available, select the top 3 partitions; otherwise, select only the top 1 to maintain acceptable speed.
        int topK = Runtime.HasGpuSupport ? 3 : 1;
        List<TextPartitionSimilarity> partitions = _ragEngine.FindMatchingPartitions(prompt.Query, topK, forceUniqueSection: true);
        if (partitions.Count > 0)
        {
            if (prompt.Regenerate)
                await _multiTurnConversation.RegenerateResponseAsync(new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);
            else if (prompt.Reset)
            {
                _distributedCache.Remove(prompt.SessionId);
                _multiTurnConversation.ClearHistory();
            }
            else
                _ = _ragEngine.QueryPartitions(prompt.Query, partitions, _multiTurnConversation);
        }
        else
        {
            var buffer = Encoding.UTF8.GetBytes("No relevant information found in the loaded sources to answer your query. Please try asking a different question.");
            await responseStream.WriteAsync(buffer);
            await responseStream.FlushAsync();
        }
    }

    public async Task GetRAGResponseVector(string prompt, Stream responseStream)
    {
        using (LM _embeddingModel = new LM("https://huggingface.co/lm-kit/bge-1.5-gguf/resolve/main/bge-small-en-v1.5-f16.gguf?download=true"))
        {
            var _store = new SqlEmbeddingStore(_context);

            _ragEngine = new RagEngine(_embeddingModel,_store);
            if (await _store.CollectionExistsAsync(COLLECTION_NAME))
            {
                _dataSource = DataSource.LoadFromStore(_store, COLLECTION_NAME, _embeddingModel);
            }
            else
            {
                string path = Path.Combine(_webHostEnvironment.ContentRootPath, "wwwroot", COLLECTION_NAME, "Intro.txt");
                string eBookContent = File.ReadAllText(path);
                _dataSource=_ragEngine.ImportText(eBookContent, new TextChunking() { MaxChunkSize = 500 }, COLLECTION_NAME, "default");
            }

            _ragEngine.ClearDataSources();
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

    public async Task GetRAGResponseVectorFromDocker(string prompt, Stream responseStream)
    {
        using (LM _embeddingModel = new LM("https://huggingface.co/lm-kit/bge-1.5-gguf/resolve/main/bge-small-en-v1.5-f16.gguf?download=true"))
        {
            var _qdrantstore = new DummyQdrantStore(new Uri("http://localhost:6334"));

            _ragEngine = new RagEngine(_embeddingModel, _qdrantstore);
            await _qdrantstore.DeleteCollectionAsync(COLLECTION_NAME);
            if (await _qdrantstore.CollectionExistsAsync(COLLECTION_NAME))
            {
                _dataSource = DataSource.LoadFromStore(_qdrantstore, COLLECTION_NAME, _embeddingModel);
            }
            else
            {
                string path = Path.Combine(_webHostEnvironment.ContentRootPath, "wwwroot", COLLECTION_NAME, "Intro.txt");
                string eBookContent = File.ReadAllText(path);
                _dataSource = _ragEngine.ImportText(eBookContent, new TextChunking() { MaxChunkSize = 500 }, COLLECTION_NAME, "default");
            }

            _ragEngine.ClearDataSources();
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
        var serializedHistory = chatHistory.Serialize();
        await _distributedCache.SetAsync(sessionId, serializedHistory);
    }
    private ChatHistory LoadChatHistory(string sessionId)
    {
        var serializedHistory = _distributedCache.Get(sessionId);
        if (serializedHistory == null)
            return new ChatHistory(_model);
        var DeserializedHistory = ChatHistory.Deserialize(serializedHistory, _model);
        return DeserializedHistory;
    }
    private void LoadEbook(string fileName, string sectionIdentifier)
    {
        if (_dataSource.HasSection(sectionIdentifier))
            return;  //we already have this ebook in the collection

        string path = Path.Combine(_webHostEnvironment.WebRootPath, COLLECTION_NAME, fileName);
        //importing the ebook into a new section
        string eBookContent = File.ReadAllText(path);
        _ragEngine.ImportText(eBookContent, new TextChunking() { MaxChunkSize = 500 }, COLLECTION_NAME, sectionIdentifier);
    }
    private async void MultiTurnConversation_AfterTokenSampling(object sender, AfterTokenSamplingEventArgs token)
    {
        if (token.TextChunk == "<|im_end|>")
        {
            // Access the current SessionId from AsyncLocal
            await SaveChatHistory(_multiTurnConversation?.ChatHistory, _currentSessionId.Value);
            return;
        }

        // Access the current responseStream from AsyncLocal
        var buffer = Encoding.UTF8.GetBytes(token.TextChunk);
        await _currentResponseStream.Value.WriteAsync(buffer);
        await _currentResponseStream.Value.FlushAsync();
    }
    #endregion

}

