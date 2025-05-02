using BlinkChatBackend.Helpers;
using BlinkChatBackend.Services.Interfaces;
using LMKit.Data;
using LMKit.Model;
using LMKit.Retrieval;

namespace BlinkChatBackend.Services;

public class LmKitModelService(ILogger<LmKitModelService> logger) : ILmKitModelService
{
    static bool _isDownloading;

    #region [AI Model]

    public LM? Model { get; private set; }

    public void LoadModel(string path)
    {
        Model = new LM(path, loadingProgress: ModelLoadingProgress);
    }

    public void LoadModel(Uri uri)
    {
        Model = new LM(uri, downloadingProgress: ModelDownloadingProgress, loadingProgress: ModelLoadingProgress);
    }

    public void LoadModel(ModelCard modelCard)
    {
        Model = new LM(modelCard, downloadingProgress: ModelDownloadingProgress, loadingProgress: ModelLoadingProgress);
    }

    #endregion [AI Model]

    #region [AI Embedding Model]

    public LM? EmbeddingModel { get; private set; }

    public void LoadEmbeddingModel(string path)
    {
        EmbeddingModel = new LM(path, loadingProgress: ModelLoadingProgress);
    }

    public void LoadEmbeddingModel(Uri uri)
    {
        EmbeddingModel = new LM(uri, downloadingProgress: ModelDownloadingProgress, loadingProgress: ModelLoadingProgress);
    }

    public void LoadEmbeddingModel(ModelCard modelCard)
    {
        EmbeddingModel = new LM(modelCard, downloadingProgress: ModelDownloadingProgress, loadingProgress: ModelLoadingProgress);
    }

    #endregion [AI Embedding Model]

    #region [RAG CollectionName]    

    public string? CollectionName { get; private set; }

    public void AddCollectionName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Collection name cannot be null or empty.");
        CollectionName = value;
    }

    #endregion [RAG CollectionName]

    #region [RAG data source]

    public DataSource? DataSource { get; private set; }

    public void LoadDataSource(string path)
    {
        if (EmbeddingModel == null) throw new ArgumentException("Embedding Model is not loaded.");
        if (string.IsNullOrWhiteSpace(CollectionName)) throw new ArgumentException("Collection name is not set.");

        if (File.Exists(path))
        {
            DataSource = DataSource.LoadFromFile(path, EmbeddingModel, readOnly: false);
        }
        else
        {
            DataSource = DataSource.CreateFileDataSource(path, CollectionName, EmbeddingModel);
        }
    }

    #endregion [RAG data source]

    #region [RAG engine]

    public RagEngine? RagEngine { get; private set; }

    public void LoadRagEngine()
    {
        RagEngine = new RagEngine(EmbeddingModel);
    }

    public void LoadDataSourceIntoRagEngine()
    {
        if (RagEngine == null) throw new ArgumentException("RAG engine is not loaded.");
        if (DataSource == null) throw new ArgumentException("Data source is not loaded.");
        RagEngine.AddDataSource(DataSource);
    }

    public void LoadFilesIntoDataSource(string fileName, string sectionIdentifier)
    {
        if (RagEngine == null) throw new ArgumentException("RAG engine is not loaded.");
        if (DataSource == null) throw new ArgumentException("Data source is not loaded.");
        if (string.IsNullOrWhiteSpace(CollectionName)) throw new ArgumentException("Collection name is not set.");
        if (string.IsNullOrWhiteSpace(sectionIdentifier)) throw new ArgumentException("Section identifier cannot be null or empty.");
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name cannot be null or empty.");

        if (DataSource.HasSection(sectionIdentifier))
        {
            logger.LogWarning("{sectionIdentifier} is already in the collection.", sectionIdentifier);
            return;  //we already have this ebook in the collection
        }

        if (!File.Exists(fileName))
        {
            logger.LogWarning("{fileName} does not exist.", fileName);
            return;
        }

        //importing the ebook into a new section
        RagEngine.ImportText(File.ReadAllText(fileName), new TextChunking() { MaxChunkSize = 500 }, CollectionName, sectionIdentifier);
    }

    #endregion [RAG engine]

    #region [Private methods]

    private static bool ModelLoadingProgress(float progress)
    {
        if (_isDownloading)
        {
            //Console.Clear();
            _isDownloading = false;
        }

        LogHelper.LogInformation($"\rLoading model {Math.Round(progress * 100)}%");

        return true;
    }

    private static bool ModelDownloadingProgress(string path, long? contentLength, long bytesRead)
    {
        _isDownloading = true;

        if (contentLength.HasValue)
        {
            double progressPercentage = Math.Round((double)bytesRead / contentLength.Value * 100, 2);
            LogHelper.LogInformation($"\rDownloading model {progressPercentage:0.00}%");
        }
        else
        {
            LogHelper.LogInformation($"\rDownloading model {bytesRead} bytes");
        }
        return true;
    }

    #endregion [Private methods]
}
