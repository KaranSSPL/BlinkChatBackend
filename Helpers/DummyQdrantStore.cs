using LMKit.Data;
using LMKit.Data.Storage;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace BlinkChatBackend.Helpers;

public class DummyQdrantStore : IVectorStore
{
    private readonly QdrantClient _client;

    public DummyQdrantStore(Uri address, string apiKey = null, string certificateThumbprint = null)
    {
        if (!string.IsNullOrWhiteSpace(certificateThumbprint))
        {
            QdrantGrpcClient grpcClient = new QdrantGrpcClient(QdrantChannel.ForAddress(address, new ClientConfiguration
            {
                CertificateThumbprint = certificateThumbprint,
                ApiKey = apiKey
            }));
            _client = new QdrantClient(grpcClient);
        }
        else
        {
            _client = new QdrantClient(address ?? throw new ArgumentNullException("address"), apiKey);
        }
    }

    public DummyQdrantStore(QdrantGrpcClient grpcClient)
    {
        _client = new QdrantClient(grpcClient);
    }

    public async Task<bool> CollectionExistsAsync(string collectionIdentifier, CancellationToken cancellationToken = default(CancellationToken))
    {
        if (string.IsNullOrWhiteSpace(collectionIdentifier))
        {
            throw new ArgumentException("Collection identifier cannot be null or empty.", "collectionIdentifier");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await _client.CollectionExistsAsync(collectionIdentifier, cancellationToken);
    }

    public async Task CreateCollectionAsync(string collectionIdentifier, uint vectorSize, CancellationToken cancellationToken = default(CancellationToken))
    {
        if (string.IsNullOrWhiteSpace(collectionIdentifier))
        {
            throw new ArgumentException("Collection identifier cannot be null or empty.", "collectionIdentifier");
        }

        cancellationToken.ThrowIfCancellationRequested();
        QdrantClient client = _client;
        VectorParams vectorsConfig = new VectorParams
        {
            Size = vectorSize,
            Distance = Distance.Cosine
        };
        CancellationToken cancellationToken2 = cancellationToken;
        await client.CreateCollectionAsync(collectionIdentifier, vectorsConfig, 1u, 1u, 1u, onDiskPayload: false, null, null, null, null, null, null, null, null, null, cancellationToken2);
    }

    public async Task DeleteCollectionAsync(string collectionIdentifier, CancellationToken cancellationToken = default(CancellationToken))
    {
        if (string.IsNullOrWhiteSpace(collectionIdentifier))
        {
            throw new ArgumentException("Collection identifier cannot be null or empty.", "collectionIdentifier");
        }

        cancellationToken.ThrowIfCancellationRequested();
        QdrantClient client = _client;
        CancellationToken cancellationToken2 = cancellationToken;
        await client.DeleteCollectionAsync(collectionIdentifier, null, cancellationToken2);
    }

    public async Task<MetadataCollection> GetMetadataAsync(string collectionIdentifier, string id, CancellationToken cancellationToken = default(CancellationToken))
    {
        if (string.IsNullOrWhiteSpace(collectionIdentifier))
        {
            throw new ArgumentException("Collection identifier cannot be null or empty.", "collectionIdentifier");
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("ID cannot be null or empty.", "id");
        }

        cancellationToken.ThrowIfCancellationRequested();
        MetadataCollection metadata = new MetadataCollection();
        IReadOnlyList<RetrievedPoint> readOnlyList;
        if (IsUintId(id))
        {
            readOnlyList = await _client.RetrieveAsync(collectionIdentifier, ulong.Parse(id), withPayload: true, withVectors: false, null, null, cancellationToken);
        }
        else
        {
            if (!Guid.TryParse(id, out var result))
            {
                throw new ArgumentException("Invalid GUID format.", "id");
            }

            readOnlyList = await _client.RetrieveAsync(collectionIdentifier, result, withPayload: true, withVectors: false, null, null, cancellationToken);
        }

        if (readOnlyList.Count == 0)
        {
            throw new KeyNotFoundException(collectionIdentifier + " with id " + id + " not found");
        }

        foreach (KeyValuePair<string, Value> item in readOnlyList[0].Payload)
        {
            metadata.Add(PayloadEntryToMetadata(item));
        }

        return metadata;
    }

    public async Task<List<PointEntry>> RetrieveFromMetadataAsync(string collectionIdentifier, MetadataCollection metadata, bool getVector, bool getMetadata, CancellationToken cancellationToken = default(CancellationToken))
    {
        if (string.IsNullOrWhiteSpace(collectionIdentifier))
        {
            throw new ArgumentException("Collection identifier cannot be null or empty.", "collectionIdentifier");
        }

        if (metadata == null)
        {
            throw new ArgumentNullException("metadata");
        }

        cancellationToken.ThrowIfCancellationRequested();
        Filter filter = new Filter();
        foreach (Metadata metadatum in metadata)
        {
            Condition item = new Condition
            {
                Field = new FieldCondition
                {
                    Key = metadatum.Key,
                    Match = new Match
                    {
                        Text = metadatum.Value
                    }
                }
            };
            filter.Must.Add(item);
        }

        QdrantClient client = _client;
        WithPayloadSelector payloadSelector = new WithPayloadSelector
        {
            Enable = getMetadata
        };
        WithVectorsSelector vectorsSelector = new WithVectorsSelector
        {
            Enable = getVector
        };
        CancellationToken cancellationToken2 = cancellationToken;
        IReadOnlyList<ScoredPoint> obj = await client.QueryAsync(collectionIdentifier, null, null, null, filter, null, null, ulong.MaxValue, 0uL, payloadSelector, vectorsSelector, null, null, null, null, cancellationToken2);
        List<PointEntry> list = new List<PointEntry>(obj.Count);
        foreach (ScoredPoint item2 in obj)
        {
            MetadataCollection metadataCollection = new MetadataCollection();
            if (item2.Payload != null)
            {
                foreach (KeyValuePair<string, Value> item3 in item2.Payload)
                {
                    metadataCollection.Add(PayloadEntryToMetadata(item3));
                }
            }

            list.Add(new PointEntry(PointIdToString(item2.Id), item2.Vectors?.Vector?.Data, metadataCollection));
        }

        return list;
    }

    public async Task<List<(PointEntry Point, float Score)>> SearchSimilarVectorsAsync(string collectionIdentifier, float[] vector, uint limit, bool getVector, bool getMetadata, CancellationToken cancellationToken = default(CancellationToken))
    {
        if (string.IsNullOrWhiteSpace(collectionIdentifier))
        {
            throw new ArgumentException("Collection identifier cannot be null or empty.", "collectionIdentifier");
        }

        if (vector == null || vector.Length == 0)
        {
            throw new ArgumentException("Vector cannot be null or empty.", "vector");
        }

        if (limit == 0)
        {
            throw new ArgumentOutOfRangeException("limit", "Limit must be greater than zero.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        QdrantClient client = _client;
        ReadOnlyMemory<float> vector2 = vector;
        WithPayloadSelector payloadSelector = new WithPayloadSelector
        {
            Enable = getMetadata
        };
        WithVectorsSelector vectorsSelector = new WithVectorsSelector
        {
            Enable = getVector
        };
        long limit2 = limit;
        long offset = 0L;
        CancellationToken cancellationToken2 = cancellationToken;
        IReadOnlyList<ScoredPoint> obj = await client.SearchAsync(collectionIdentifier, vector2, null, null, (ulong)limit2, (ulong)offset, payloadSelector, vectorsSelector, null, null, null, null, null, null, cancellationToken2);
        List<(PointEntry, float)> list = new List<(PointEntry, float)>(obj.Count);
        foreach (ScoredPoint item in obj)
        {
            MetadataCollection metadataCollection = new MetadataCollection();
            if (item.Payload != null)
            {
                foreach (KeyValuePair<string, Value> item2 in item.Payload)
                {
                    metadataCollection.Add(PayloadEntryToMetadata(item2));
                }
            }

            list.Add((new PointEntry(PointIdToString(item.Id), item.Vectors?.Vector?.Data, metadataCollection), item.Score));
        }

        return list;
    }

    public async Task DeleteFromMetadataAsync(string collectionIdentifier, MetadataCollection metadata, CancellationToken cancellationToken = default(CancellationToken))
    {
        if (string.IsNullOrWhiteSpace(collectionIdentifier))
        {
            throw new ArgumentException("Collection identifier cannot be null or empty.", "collectionIdentifier");
        }

        if (metadata == null)
        {
            throw new ArgumentNullException("metadata");
        }

        cancellationToken.ThrowIfCancellationRequested();
        Filter filter = new Filter();
        foreach (Metadata metadatum in metadata)
        {
            Condition item = new Condition
            {
                Field = new FieldCondition
                {
                    Key = metadatum.Key,
                    Match = new Match
                    {
                        Text = metadatum.Value
                    }
                }
            };
            filter.Must.Add(item);
        }

        QdrantClient client = _client;
        CancellationToken cancellationToken2 = cancellationToken;
        if ((await client.DeleteAsync(collectionIdentifier, filter, wait: true, null, null, cancellationToken2)).Status != UpdateStatus.Completed)
        {
            throw new Exception("Failed to delete vector from collection '" + collectionIdentifier + "'");
        }
    }

    public async Task UpsertAsync(string collectionIdentifier, string id, float[] vectors, MetadataCollection metadata, CancellationToken cancellationToken = default(CancellationToken))
    {
        if (string.IsNullOrWhiteSpace(collectionIdentifier))
        {
            throw new ArgumentException("Collection identifier cannot be null or empty.", "collectionIdentifier");
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("ID cannot be null or empty.", "id");
        }

        if (vectors == null || vectors.Length == 0)
        {
            throw new ArgumentException("Vector data cannot be null or empty.", "vectors");
        }

        if (metadata == null)
        {
            throw new ArgumentNullException("metadata");
        }

        cancellationToken.ThrowIfCancellationRequested();
        PointStruct pointStruct = new PointStruct
        {
            Id = ParsePointId(id),
            Vectors = vectors
        };
        foreach (Metadata metadatum in metadata)
        {
            pointStruct.Payload.Add(metadatum.Key, metadatum.Value);
        }

        QdrantClient client = _client;
        PointStruct[] points = new PointStruct[1] { pointStruct };
        CancellationToken cancellationToken2 = cancellationToken;
        if ((await client.UpsertAsync(collectionIdentifier, points, wait: true, null, null, cancellationToken2)).Status != UpdateStatus.Completed)
        {
            throw new Exception("Failed to upsert vector for collection '" + collectionIdentifier + "' with id " + id);
        }
    }

    public async Task UpdateMetadataAsync(string collectionIdentifier, string id, MetadataCollection metadata, bool clearFirst, CancellationToken cancellationToken = default(CancellationToken))
    {
        if (string.IsNullOrWhiteSpace(collectionIdentifier))
        {
            throw new ArgumentException("Collection identifier cannot be null or empty.", "collectionIdentifier");
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("ID cannot be null or empty.", "id");
        }

        if (metadata == null)
        {
            throw new ArgumentNullException("metadata");
        }

        cancellationToken.ThrowIfCancellationRequested();
        Dictionary<string, Value> payload = new Dictionary<string, Value>(metadata.Count);
        foreach (Metadata metadatum in metadata)
        {
            payload.Add(metadatum.Key, new Value
            {
                StringValue = metadatum.Value
            });
        }

        UpdateResult updateResult;
        if (clearFirst)
        {
            if (IsUintId(id))
            {
                QdrantClient client = _client;
                ulong id2 = ulong.Parse(id);
                CancellationToken cancellationToken2 = cancellationToken;
                updateResult = await client.ClearPayloadAsync(collectionIdentifier, id2, wait: true, null, null, cancellationToken2);
            }
            else
            {
                QdrantClient client2 = _client;
                Guid id3 = new Guid(id);
                CancellationToken cancellationToken2 = cancellationToken;
                updateResult = await client2.ClearPayloadAsync(collectionIdentifier, id3, wait: true, null, null, cancellationToken2);
            }

            if (updateResult.Status != UpdateStatus.Completed)
            {
                throw new Exception("Failed to clear metadata for collection '" + collectionIdentifier + "' with id " + id);
            }
        }

        if (IsUintId(id))
        {
            QdrantClient client3 = _client;
            ulong id4 = ulong.Parse(id);
            CancellationToken cancellationToken2 = cancellationToken;
            updateResult = await client3.SetPayloadAsync(collectionIdentifier, payload, id4, wait: true, null, null, null, cancellationToken2);
        }
        else
        {
            QdrantClient client4 = _client;
            Guid id5 = new Guid(id);
            CancellationToken cancellationToken2 = cancellationToken;
            updateResult = await client4.SetPayloadAsync(collectionIdentifier, payload, id5, wait: true, null, null, null, cancellationToken2);
        }

        if (updateResult.Status != UpdateStatus.Completed)
        {
            throw new Exception("Failed to update metadata for collection '" + collectionIdentifier + "' with id " + id);
        }
    }

    //
    // Summary:
    //     Converts a Qdrant payload entry (key-value pair) to a LMKit.Data.Metadata instance.
    //
    //
    // Parameters:
    //   pair:
    //     A key-value pair from the Qdrant payload.
    //
    // Returns:
    //     A new LMKit.Data.Metadata instance representing the key and its corresponding
    //     value.
    private static Metadata PayloadEntryToMetadata(KeyValuePair<string, Value> pair)
    {
        if (pair.Value.HasStringValue)
        {
            return new Metadata(pair.Key, pair.Value.StringValue);
        }

        if (pair.Value.HasDoubleValue)
        {
            return new Metadata(pair.Key, pair.Value.DoubleValue.ToString());
        }

        if (pair.Value.HasBoolValue)
        {
            return new Metadata(pair.Key, pair.Value.BoolValue.ToString());
        }

        if (pair.Value.HasIntegerValue)
        {
            return new Metadata(pair.Key, pair.Value.IntegerValue.ToString());
        }

        if (pair.Value.HasNullValue)
        {
            return new Metadata(pair.Key, pair.Value.NullValue.ToString());
        }

        return new Metadata(pair.Key, pair.Value.ToString());
    }

    //
    // Summary:
    //     Determines whether the given string identifier represents a numeric (unsigned
    //     long) ID.
    //
    // Parameters:
    //   id:
    //     The identifier to test.
    //
    // Returns:
    //     true if the identifier can be parsed as an unsigned long; otherwise, false.
    private static bool IsUintId(string id)
    {
        ulong result;
        return ulong.TryParse(id, out result);
    }

    //
    // Summary:
    //     Parses the provided string identifier into a Qdrant.Client.Grpc.PointId.
    //
    // Parameters:
    //   id:
    //     The identifier to parse.
    //
    // Returns:
    //     A Qdrant.Client.Grpc.PointId corresponding to the provided identifier.
    //
    // Exceptions:
    //   T:System.ArgumentException:
    //     Thrown if the identifier is not a valid unsigned long or GUID.
    private static PointId ParsePointId(string id)
    {
        if (ulong.TryParse(id, out var result))
        {
            return new PointId(result);
        }

        if (Guid.TryParse(id, out var result2))
        {
            return new PointId(result2);
        }

        throw new ArgumentException("The provided id is neither a valid unsigned long nor a GUID.", "id");
    }

    //
    // Summary:
    //     Converts a Qdrant.Client.Grpc.PointId to its string representation.
    //
    // Parameters:
    //   id:
    //     The Qdrant.Client.Grpc.PointId to convert.
    //
    // Returns:
    //     A string representation of the Qdrant.Client.Grpc.PointId.
    private static string PointIdToString(PointId id)
    {
        if (!id.HasUuid)
        {
            return id.Num.ToString();
        }

        return id.Uuid.ToString();
    }
}