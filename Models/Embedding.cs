using Qdrant.Client.Grpc;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlinkChatBackend.Models
{
    public class Embedding
    {
        [Key]
        public Guid Id { get; set; }
        public string? Metadata { get; set; }
        
        public string CollectionName { get; set; }
    }
}
