using LMKit.Agents;
using LMKit.Data;
using LMKit.Data.Storage;
using LMKit.Model;
using LMKit.Retrieval;
using LMKit.TextGeneration;
using LMKit.TextGeneration.Chat;

namespace BlinkChatBackend.Services.Interfaces;

public interface ILmKitModelService
{
    #region [AI Model]

    LM? Model { get; }
    void LoadModel(string path);
    void LoadModel(Uri uri, string? storagePath = null);
    void LoadModel(ModelCard modelCard);

    #endregion [AI Model]

    #region [AI Embedding Model]

    LM? EmbeddingModel { get; }
    void LoadEmbeddingModel(string path);
    void LoadEmbeddingModel(Uri uri, string? storagePath = null);
    void LoadEmbeddingModel(ModelCard modelCard);

    #endregion [AI Embedding Model]

    #region [RAG CollectionName]    

    public string? CollectionName { get; }
    public void AddCollectionName(string value);

    #endregion [RAG CollectionName]

    #region [RAG Vector Store]    

    IVectorStore? VectorStore { get; }
    List<DataSource>? VectorDataSources { get; }
    void LoadVectorStore(Uri value);

    #endregion [RAG Vector Store]

    #region [RAG data source]

    DataSource? DataSource { get; }
    void LoadDataSource(string path);

    #endregion [RAG data source]

    #region [RAG engine]

    RagEngine? RagEngine { get; }
    void LoadRagEngine();
    void LoadDataSourceIntoRagEngine();
    void LoadVectorStoreRagEngine();
    void LoadDataSourceIntoVectorStoreRagEngine();
    void LoadFilesIntoDataSource(string fileName, string sectionIdentifier);
    void LoadFilesIntoVectorDataSource(string fileName, string sectionIdentifier);

    #endregion [RAG engine]

    #region [Memory]

    AgentMemory? Memory { get; }

    #endregion [Memory]

    #region [MultiTurn Conversation]

    MultiTurnConversation? MultiTurnConversation { get; }

    void LoadMultiTurnConversation(ChatHistory? chatHistory = null);

    #endregion [MultiTurn Conversation]
}
