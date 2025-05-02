using LMKit.Data;
using LMKit.Model;
using LMKit.Retrieval;

namespace BlinkChatBackend.Services.Interfaces;

public interface ILmKitModelService
{
    #region [AI Model]

    LM? Model { get; }
    void LoadModel(string path);
    void LoadModel(Uri uri);
    void LoadModel(ModelCard modelCard);

    #endregion [AI Model]

    #region [AI Embedding Model]

    LM? EmbeddingModel { get; }
    void LoadEmbeddingModel(string path);
    void LoadEmbeddingModel(Uri uri);
    void LoadEmbeddingModel(ModelCard modelCard);

    #endregion [AI Embedding Model]

    #region [RAG CollectionName]    

    public string? CollectionName { get; }
    public void AddCollectionName(string value);

    #endregion [RAG CollectionName]

    #region [RAG data source]

    DataSource? DataSource { get; }
    void LoadDataSource(string path);

    #endregion [RAG data source]

    #region [RAG engine]

    RagEngine? RagEngine { get; }
    void LoadRagEngine();
    void LoadDataSourceIntoRagEngine();
    void LoadFilesIntoDataSource(string fileName, string sectionIdentifier, string? filePath = null);

    #endregion [RAG engine]
}
