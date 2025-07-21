using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Rebus.Bus;
using Serilog;

namespace Melodee.Common.Services;

/// <summary>
/// Handles user queue operations.
/// </summary>
/// <param name="logger"></param>
/// <param name="cacheManager"></param>
/// <param name="contextFactory"></param>
/// <param name="configurationFactory"></param>
/// <param name="libraryService"></param>
/// <param name="artistService"></param>
/// <param name="albumService"></param>
/// <param name="songService"></param>
/// <param name="playlistService"></param>
/// <param name="bus"></param>
public class UserQueService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IMelodeeConfigurationFactory configurationFactory,
    LibraryService libraryService,
    ArtistService artistService,
    AlbumService albumService,
    SongService songService,
    PlaylistService playlistService,
    IBus bus)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    
}
