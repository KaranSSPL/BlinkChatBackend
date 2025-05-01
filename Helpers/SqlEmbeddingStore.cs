using BlinkChatBackend.Models;
using Google.Protobuf;
using Google.Protobuf.Collections;
using LMKit.Data;
using LMKit.Data.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Qdrant.Client.Grpc;

namespace BlinkChatBackend.Helpers
{
    public class SqlEmbeddingStore : IVectorStore
    {
        private readonly BlinkChatContext _context;

        public SqlEmbeddingStore(BlinkChatContext context)
        {
            _context = context;
        }
        public async Task<bool> CollectionExistsAsync(string collectionIdentifier, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(collectionIdentifier))
            {
                throw new ArgumentException("Collection identifier cannot be null or empty.", "collectionIdentifier");
            }
            var embedding=_context.embeddings.FirstOrDefault(x => x.CollectionName == collectionIdentifier);
            return embedding!=null;
        }

        public async Task CreateCollectionAsync(string collectionIdentifier, uint vectorSize, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(collectionIdentifier))
            {
                throw new ArgumentException("Collection identifier cannot be null or empty.", "collectionIdentifier");
            }
            var embedding = new Embedding
            {
                CollectionName = collectionIdentifier,
            };
            _context.embeddings.Add(embedding);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteCollectionAsync(string collectionIdentifier, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(collectionIdentifier))
            {
                throw new ArgumentException("Collection identifier cannot be null or empty.", "collectionIdentifier");
            }
            var collection = _context.embeddings.FirstOrDefault(x => x.CollectionName == collectionIdentifier);
            if (collection != null)
            {
                _context.embeddings.Remove(collection);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task DeleteFromMetadataAsync(string collectionIdentifier, MetadataCollection metadata, CancellationToken cancellationToken = default)
        {
            return;
        }

        public async Task<MetadataCollection> GetMetadataAsync(string collectionIdentifier, string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(collectionIdentifier))
            {
                throw new ArgumentException("Collection identifier cannot be null or empty.", "collectionIdentifier");
            }
            cancellationToken.ThrowIfCancellationRequested();
            MetadataCollection metadata = new MetadataCollection();

            var deserializedMeta = MapPointStruct(_context.embeddings.FirstOrDefault(x => x.CollectionName == collectionIdentifier)?.Metadata??"");
            
            PointStruct readOnlyPoint = deserializedMeta.FirstOrDefault(x => x.Id.Uuid == id)!;

            if (readOnlyPoint == null)
            {
                throw new KeyNotFoundException(collectionIdentifier + " with id " + id + " not found");
            }
            foreach (KeyValuePair<string, Value> item in readOnlyPoint.Payload)
            {
                metadata.Add(PayloadEntryToMetadata(item));
            }

            return metadata;
        }

        public async Task<List<PointEntry>> RetrieveFromMetadataAsync(string collectionIdentifier, MetadataCollection metadata, bool getVector, bool getMetadata, CancellationToken cancellationToken = default)
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
            
            CancellationToken cancellationToken2 = cancellationToken;

            var embedding = _context.embeddings.FirstOrDefault(x => x.CollectionName == collectionIdentifier);
            if (embedding == null)
            {
                return new List<PointEntry>(); 
            }
            var deserializedMeta = MapPointStruct(embedding?.Metadata ?? "");

            List<ScoredPoint> scoredPoints = new List<ScoredPoint>();
            foreach ( var point in deserializedMeta)
            {
                if (point.Payload.Count == 0)
                    continue;
                var metadata2 = new MetadataCollection();
                point.Payload.ToList().ForEach(x => metadata2.Add(PayloadEntryToMetadata(x)));
                if(Match.Equals(metadata, metadata2))
                {
                    var scoredPoint=new ScoredPoint()
                    {
                        Id=point.Id,
                    };
                    if (getVector)
                        scoredPoint.Vectors.MergeFrom(point.Vectors.ToByteArray());
                    if (getMetadata)
                        scoredPoint.Payload.Add(point.Payload);
                    scoredPoints.Add(scoredPoint);
                }
                
            }

            List<PointEntry> list = new List<PointEntry>(scoredPoints.Count);
            foreach (ScoredPoint item2 in scoredPoints)
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

        public async Task<List<(PointEntry Point, float Score)>> SearchSimilarVectorsAsync(string collectionIdentifier, float[] vector, uint limit, bool getVector, bool getMetadata, CancellationToken cancellationToken = default)
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

            var embedding = _context.embeddings.FirstOrDefault(x => x.CollectionName == collectionIdentifier);
            if (embedding == null)
            {
                return new List<(PointEntry, float)>();
            }   
            var deserializedMeta = MapPointStruct(embedding?.Metadata ?? "");

            List<ScoredPoint> scoredPoints = new List<ScoredPoint>();
            foreach (var point in deserializedMeta)
            {
                if (point.Payload.Count == 0)
                    continue;
                
                if (Match.Equals(vector, point.Vectors.Vector.Data.ToArray()))
                {
                    ScoredPoint scoredPoint = new ScoredPoint() { Id=point.Id};
                    if (getVector)
                        scoredPoint.Vectors.MergeFrom(point.Vectors.ToByteArray());
                    if(getMetadata)
                        scoredPoint.Payload.Add(point.Payload);
                    scoredPoints.Add(scoredPoint);
                }

            }

            List<(PointEntry, float)> list = new List<(PointEntry, float)>(scoredPoints.Count);
            foreach (ScoredPoint item in scoredPoints)
            {
                MetadataCollection metadataCollection = new MetadataCollection();
                if (item.Payload != null)
                {
                    foreach (KeyValuePair<string, Value> item3 in item.Payload)
                    {
                        metadataCollection.Add(PayloadEntryToMetadata(item3));
                    }
                }

                list.Add((new PointEntry(PointIdToString(item.Id), item.Vectors?.Vector?.Data, metadataCollection), item.Score));
            }

            return list;
        }

        public async Task UpdateMetadataAsync(string collectionIdentifier, string id, MetadataCollection metadata, bool clearFirst, CancellationToken cancellationToken = default)
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

            Embedding embedding = _context.embeddings.FirstOrDefault(x => x.CollectionName == collectionIdentifier);
            if(embedding is not null)
            {
                var deserializedMeta = MapPointStruct(embedding?.Metadata ?? "");
                if (clearFirst)
                {
                    deserializedMeta.FirstOrDefault(x=>x.Id==ParsePointId(id))?.Payload.Clear();
                }
                deserializedMeta.FirstOrDefault(x => x.Id == ParsePointId(id))?.Payload.Add(payload);
                embedding!.Metadata =JsonConvert.SerializeObject(deserializedMeta);
                _context.embeddings.Update(embedding);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task UpsertAsync(string collectionIdentifier, string id, float[] vectors, MetadataCollection metadata, CancellationToken cancellationToken = default)
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
                Id = new PointId(Guid.Parse(id)),
                Vectors = vectors
            };
            foreach (Metadata metadatum in metadata)
            {
                pointStruct.Payload.Add(metadatum.Key, metadatum.Value);
            }

            CancellationToken cancellationToken2 = cancellationToken;
            
            var embedding = _context.embeddings.FirstOrDefault(x => x.CollectionName == collectionIdentifier);
            if(embedding!=null)
            {
                var deserializedMeta = MapPointStruct(embedding?.Metadata ?? "");
                List<PointStruct> points =[pointStruct];
                if(deserializedMeta!=null && deserializedMeta.Count>0)
                {
                    points.AddRange(deserializedMeta);
                }
                embedding!.Metadata = JsonConvert.SerializeObject(points);
                _context.embeddings.Update(embedding);
                await _context.SaveChangesAsync();
            }
        }

        private Metadata PayloadEntryToMetadata(KeyValuePair<string, Value> pair)
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
        private PointId ParsePointId(string id)
        {
            if (Guid.TryParse(id, out var result2))
            {
                return new PointId(result2);
            }
            throw new ArgumentException("The provided id is neither a valid unsigned long nor a GUID.", "id");
        }
        private string PointIdToString(PointId id)
        {
            return id.Uuid.ToString();
        }
        
        private List<PointStruct> MapPointStruct(string JsonString)
        {
            if(string.IsNullOrEmpty(JsonString)) return new List<PointStruct>();

            var jArray = JArray.Parse(JsonString);
            var points = new List<PointStruct>();
            foreach (var item in jArray)
            {
                var point = new PointStruct();

                if (item["Id"] is JToken idToken)
                {
                    point.Id = JsonConvert.DeserializeObject<PointId>(idToken.ToString());
                }

                if (item["Payload"] is JToken payloadToken)
                {
                    var payloadMap = new MapField<string, Value>();
                    var payloadObj = JObject.Parse(payloadToken.ToString());

                    foreach (var prop in payloadObj.Properties())
                    {
                        var tempValue = new Value
                        {
                            StringValue = prop.Value["StringValue"]?.ToString(),
                        };

                        payloadMap.Add(prop.Name, tempValue);
                    }

                    point.Payload.Add(payloadMap);
                }

                if (item["Vectors"] is JToken vectorsToken)
                {
                    var vectorObj = JObject.Parse(vectorsToken.ToString());
                    if(vectorObj is not null)
                    {
                        var vectorArray=vectorObj.Properties().FirstOrDefault()?.Value["Data"]?.ToObject<float[]>() ?? Array.Empty<float>();
                        point.Vectors = vectorArray;
                    }
                }

                points.Add(point);
            }
            return points;
        }
    }
}
