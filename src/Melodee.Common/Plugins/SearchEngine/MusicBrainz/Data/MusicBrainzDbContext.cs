using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;

/// <summary>
/// EF Core DbContext for MusicBrainz materialized data
/// </summary>
public class MusicBrainzDbContext : DbContext
{
    public MusicBrainzDbContext(DbContextOptions<MusicBrainzDbContext> options) : base(options)
    {
    }

    public DbSet<Artist> Artists { get; set; } = null!;
    public DbSet<Album> Albums { get; set; } = null!;
    public DbSet<ArtistRelation> ArtistRelations { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Artist entity
        modelBuilder.Entity<Artist>(entity =>
        {
            entity.ToTable("Artist");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            
            entity.HasIndex(e => e.MusicBrainzIdRaw)
                .HasDatabaseName("IX_Artist_MusicBrainzIdRaw");
            entity.HasIndex(e => e.NameNormalized)
                .HasDatabaseName("IX_Artist_NameNormalized");
            entity.HasIndex(e => e.MusicBrainzArtistId)
                .HasDatabaseName("IX_Artist_MusicBrainzArtistId");

            entity.Property(e => e.Name)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.SortName)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.NameNormalized)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.MusicBrainzIdRaw)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
        });

        // Configure Album entity
        modelBuilder.Entity<Album>(entity =>
        {
            entity.ToTable("Album");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            
            entity.HasIndex(e => e.MusicBrainzIdRaw)
                .HasDatabaseName("IX_Album_MusicBrainzIdRaw");
            entity.HasIndex(e => e.MusicBrainzArtistId)
                .HasDatabaseName("IX_Album_MusicBrainzArtistId");
            entity.HasIndex(e => e.NameNormalized)
                .HasDatabaseName("IX_Album_NameNormalized");

            entity.Property(e => e.Name)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.SortName)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.NameNormalized)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.MusicBrainzIdRaw)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.ReleaseGroupMusicBrainzIdRaw)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
        });

        // Configure ArtistRelation entity
        modelBuilder.Entity<ArtistRelation>(entity =>
        {
            entity.ToTable("ArtistRelation");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            
            entity.HasIndex(e => e.ArtistId)
                .HasDatabaseName("IX_ArtistRelation_ArtistId");
            entity.HasIndex(e => e.RelatedArtistId)
                .HasDatabaseName("IX_ArtistRelation_RelatedArtistId");
        });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            // This will be overridden by the dependency injection configuration
            optionsBuilder.UseSqlite("Data Source=:memory:");
        }
    }
}