using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Melodee.Common.Enums;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized;

/// <summary>
///     This is a materialized record for MusicBrainz Artist to Artist relation from all the MusicBrainz export files.
/// </summary>
[Table("ArtistRelation")]
[Index(nameof(ArtistId))]
[Index(nameof(RelatedArtistId))]
public sealed record ArtistRelation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; init; }

    public required long ArtistId { get; init; }

    public required long RelatedArtistId { get; init; }

    public required int ArtistRelationType { get; set; }

    [NotMapped] 
    public ArtistRelationType ArtistRelationTypeValue => SafeParser.ToEnum<ArtistRelationType>(ArtistRelationType);

    public int SortOrder { get; init; }

    public required DateTime? RelationStart { get; init; }

    public required DateTime? RelationEnd { get; init; }
}
