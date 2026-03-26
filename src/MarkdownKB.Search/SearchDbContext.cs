using Microsoft.EntityFrameworkCore;
using MarkdownKB.Search.Models;
using Pgvector.EntityFrameworkCore;

namespace MarkdownKB.Search;

public class SearchDbContext : DbContext
{
    public SearchDbContext(DbContextOptions<SearchDbContext> options) : base(options) { }

    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.ToTable("document_chunks");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.RepoId).HasColumnName("repo_id").IsRequired();
            entity.Property(e => e.FilePath).HasColumnName("file_path").IsRequired();
            entity.Property(e => e.HeadingPath).HasColumnName("heading_path");
            entity.Property(e => e.ChunkIndex).HasColumnName("chunk_index");
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.ContentTsv)
                  .HasColumnName("content_tsv")
                  .ValueGeneratedOnAddOrUpdate(); // managed by DB trigger
            entity.Property(e => e.Embedding)
                  .HasColumnName("embedding")
                  .HasColumnType("vector(1536)");
            entity.Property(e => e.FileHash).HasColumnName("file_hash");
            entity.Property(e => e.TokenCount).HasColumnName("token_count");
            entity.Property(e => e.CreatedAt)
                  .HasColumnName("created_at")
                  .ValueGeneratedOnAdd();
        });
    }
}
