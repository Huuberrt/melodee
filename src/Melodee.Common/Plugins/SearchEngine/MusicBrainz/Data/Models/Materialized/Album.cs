using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Enums;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized;

/// <summary>
///     This is a materialized release for MusicBrainz release from all the MusicBrainz export files.
/// </summary>
[Table("Album")]
[Index(nameof(MusicBrainzIdRaw))]
[Index(nameof(MusicBrainzArtistId))]
[Index(nameof(NameNormalized))]
public sealed record Album
{
    public const string TableName = "albums";

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>
    ///     This is the MusicBrainz database Id
    /// </summary>
    public required long MusicBrainzArtistId { get; init; }

    [MaxLength(MusicBrainzRepositoryBase.MaxIndexSize)]
    public required string Name { get; init; }

    [MaxLength(MusicBrainzRepositoryBase.MaxIndexSize)]
    public required string SortName { get; init; }

    [MaxLength(MusicBrainzRepositoryBase.MaxIndexSize)]
    public required string NameNormalized { get; init; }

    public int ReleaseType { get; init; }

    [NotMapped] 
    public ReleaseType ReleaseTypeValue => SafeParser.ToEnum<ReleaseType>(ReleaseType);

    [NotMapped]
    public bool DoIncludeInArtistSearch => ReleaseDate > DateTime.MinValue;

    [MaxLength(MusicBrainzRepositoryBase.MaxIndexSize)]
    public required string MusicBrainzIdRaw { get; init; }

    [NotMapped] 
    public Guid MusicBrainzId => SafeParser.ToGuid(MusicBrainzIdRaw) ?? Guid.Empty;

    [MaxLength(MusicBrainzRepositoryBase.MaxIndexSize)]
    public required string ReleaseGroupMusicBrainzIdRaw { get; init; }

    [NotMapped] 
    public Guid ReleaseGroupMusicBrainzId => SafeParser.ToGuid(ReleaseGroupMusicBrainzIdRaw) ?? Guid.Empty;

    public required DateTime ReleaseDate { get; init; }

    public string? ContributorIds { get; init; }
}
