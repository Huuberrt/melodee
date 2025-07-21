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
public class UserQueueService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IMelodeeConfigurationFactory configurationFactory,
    LibraryService libraryService,
    ArtistService artistService,
    AlbumService albumService,
    SongService songService,
    IBus bus)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    
}
