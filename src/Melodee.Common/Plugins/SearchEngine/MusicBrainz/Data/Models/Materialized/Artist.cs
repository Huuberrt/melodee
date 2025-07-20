using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Melodee.Common.Extensions;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized;

/// <summary>
///     This is a materialized record for MusicBrainz Artist from all the MusicBrainz export files.
/// </summary>
[Table("Artist")]
[Index(nameof(MusicBrainzIdRaw))]
[Index(nameof(NameNormalized))]
public sealed record Artist
{
    public const string TableName = "artists";

    private string[]? _alternateNames;

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public required long MusicBrainzArtistId { get; init; }

    [MaxLength(MusicBrainzRepositoryBase.MaxIndexSize)]
    public required string Name { get; init; }

    [MaxLength(MusicBrainzRepositoryBase.MaxIndexSize)]
    public required string SortName { get; init; }

    [MaxLength(MusicBrainzRepositoryBase.MaxIndexSize)]
    public required string NameNormalized { get; init; }

    [MaxLength(MusicBrainzRepositoryBase.MaxIndexSize)]
    public required string MusicBrainzIdRaw { get; init; }

    [NotMapped] 
    public Guid MusicBrainzId => SafeParser.ToGuid(MusicBrainzIdRaw) ?? Guid.Empty;

    public string? AlternateNames { get; init; }

    [NotMapped] 
    public string[] AlternateNamesValues => _alternateNames ??= AlternateNames?.ToTags()?.ToArray() ?? [];
}
