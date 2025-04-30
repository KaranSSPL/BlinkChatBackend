using Microsoft.EntityFrameworkCore;

namespace BlinkChatBackend.Models;
public class BlinkChatContext : DbContext
{
    public DbSet<Embedding> embeddings { get; set; }
    public BlinkChatContext(DbContextOptions<BlinkChatContext> options) : base(options) { }
}

