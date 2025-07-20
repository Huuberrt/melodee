# OpenSubsonicApiService Refactoring Plan

## Executive Summary

The `OpenSubsonicApiService` class has grown to 3,797 lines and violates the Single Responsibility Principle by containing domain logic that should reside in the existing well-tested domain services. This document outlines a comprehensive refactoring plan to extract domain logic into the existing services while maintaining API functionality.

## Current State Analysis

### Problems Identified

1. **Direct Database Access**: Raw SQL queries and direct EF context manipulation instead of using domain services
2. **Bypassing Existing Services**: Domain logic reimplemented instead of leveraging tested services
3. **Configuration Management**: Direct configuration access throughout the class
4. **Caching Logic**: Custom caching strategies instead of using service-level caching
5. **File System Operations**: Direct file I/O instead of using service methods
6. **Large Class Size**: 3,797 lines making maintenance difficult

### Existing Service Architecture Analysis

Melodee has a **comprehensive and mature service layer** with:

- ✅ **Complete Authentication**: `UserService.LoginUserAsync()` handles authentication
- ✅ **Image Handling**: `AlbumService.GetAlbumImageBytesAndEtagAsync()`, `ArtistService.GetArtistImageBytesAndEtagAsync()`
- ✅ **Media Streaming**: `SongService.GetStreamForSongAsync()` provides complete streaming
- ✅ **Search Functionality**: `SearchService.SearchAsync()` handles multi-domain search
- ✅ **User Interactions**: `UserService.SetAlbumRatingAsync()`, `UserService.ToggleAlbumStarAsync()`
- ✅ **Playlist Management**: `PlaylistService` with full CRUD operations
- ✅ **Caching Infrastructure**: All services implement robust caching with ETags

### Current Dependencies (Should Be Leveraged More)

```csharp
public class OpenSubsonicApiService(
    // Infrastructure (keep)
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    DefaultImages defaultImages,
    IMelodeeConfigurationFactory configurationFactory,

    // Domain Services (leverage more effectively)
    UserService userService,              // ✅ Has authentication, ratings, starring
    ArtistService artistService,          // ✅ Has image handling, search
    AlbumService albumService,            // ✅ Has image handling, metadata
    SongService songService,              // ✅ Has streaming, search
    ScrobbleService scrobbleService,      // ✅ Has play tracking
    LibraryService libraryService,        // ✅ Has library management
    PlaylistService playlistService,     // ✅ Has playlist CRUD
    ShareService shareService,            // ✅ Has sharing functionality

    // Specialized Services
    ArtistSearchEngineService artistSearchEngineService, // Consider consolidating with SearchService
    IScheduler schedule,
    IBus bus,
    ILyricPlugin lyricPlugin
)
```

## Refactoring Strategy Overview

**Core Principle**: Leverage existing tested services instead of creating new ones.

### Phase 1: Use Existing Authentication & User Services (High Priority)
- Replace custom auth logic with `UserService.LoginUserAsync()`
- Use existing rating/starring methods instead of direct DB access

### Phase 2: Leverage Media & Image Services (High Priority)
- Replace custom streaming with `SongService.GetStreamForSongAsync()`
- Replace custom image handling with `AlbumService.GetAlbumImageBytesAndEtagAsync()`
- Use existing search services instead of raw SQL

### Phase 3: Enhance Existing Services (Medium Priority)
- Add missing methods to existing services rather than creating new ones
- Consolidate search functionality

### Phase 4: Clean API Service (Low Priority)
- Remove direct database access
- Focus on request/response transformation
- Implement proper error handling

## Detailed Refactoring Plan

## Phase 1: Leverage Existing Authentication & User Services

### 1.1 Replace Custom Authentication Logic

**Current Issue**: `AuthenticateSubsonicApiAsync()` contains custom authentication logic

**Solution**: Use existing `UserService.LoginUserAsync()`

**Refactored Implementation**:
```csharp
// BEFORE: Custom authentication in OpenSubsonicApiService
private async Task<User?> AuthenticateUser(string username, string password, string? token, string? salt)
{
    // 50+ lines of custom auth logic with direct DB access
    // Password hashing, token validation, etc.
}

// AFTER: Leverage UserService
public async Task<ResponseModel> AuthenticateSubsonicApiAsync(ApiRequest apiRequest, CancellationToken cancellationToken = default)
{
    if (!apiRequest.RequiresAuthentication)
    {
        var user = apiRequest.Username == null
            ? null
            : await userService.GetByUsernameAsync(apiRequest.Username, cancellationToken);
        return MapToResponseModel(user);
    }

    // Use existing authentication service
    var loginResult = await userService.LoginUserAsync(apiRequest.Username, apiRequest.Password, cancellationToken);

    if (!loginResult.IsSuccess && !string.IsNullOrEmpty(apiRequest.Token))
    {
        // Add token validation to UserService if needed
        loginResult = await userService.ValidateTokenAsync(apiRequest.Username, apiRequest.Token, apiRequest.Salt, cancellationToken);
    }

    return MapToResponseModel(loginResult);
}
```

**Benefits**:
- Use tested authentication logic
- Eliminate 50+ lines of duplicate code
- Consistent authentication across the application

### 1.2 Replace Custom Rating Logic

**Current Issue**: `SetRatingAsync()` contains direct database manipulation

**Solution**: Use existing `UserService.SetAlbumRatingAsync()`, `UserService.SetSongRatingAsync()`

**Refactored Implementation**:
```csharp
// BEFORE: Direct database access in OpenSubsonicApiService
public async Task<ResponseModel> SetRatingAsync(SetRatingRequest request, ApiRequest apiRequest, CancellationToken cancellationToken)
{
    // 100+ lines of custom rating logic with direct EF context manipulation
    await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken);
    // Complex branching logic for different entity types
    // Manual cache invalidation
}

// AFTER: Leverage UserService
public async Task<ResponseModel> SetRatingAsync(SetRatingRequest request, ApiRequest apiRequest, CancellationToken cancellationToken)
{
    var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
    if (!authResponse.IsSuccess) return authResponse;

    var apiKey = ApiKeyFromId(request.Id);
    if (apiKey == null) return ErrorResponse("Invalid ID");

    OperationResult<bool> result;

    if (IsApiIdForSong(request.Id))
    {
        result = await userService.SetSongRatingAsync(authResponse.UserInfo.ApiKey, apiKey.Value, request.Rating, cancellationToken);
    }
    else if (IsApiIdForAlbum(request.Id))
    {
        result = await userService.SetAlbumRatingAsync(authResponse.UserInfo.ApiKey, apiKey.Value, request.Rating, cancellationToken);
    }
    else if (IsApiIdForArtist(request.Id))
    {
        result = await userService.SetArtistRatingAsync(authResponse.UserInfo.ApiKey, apiKey.Value, request.Rating, cancellationToken);
    }
    else
    {
        return ErrorResponse("Unsupported entity type");
    }

    return MapToResponseModel(result);
}
```

**Benefits**:
- Use tested rating logic with proper validation
- Automatic cache invalidation handled by domain services
- Eliminate 100+ lines of duplicate code

## Phase 2: Leverage Media & Image Services

### 2.1 Replace Custom Streaming Logic

**Current Issue**: `StreamAsync()` contains file I/O and streaming logic

**Solution**: Use existing `SongService.GetStreamForSongAsync()`

**Refactored Implementation**:
```csharp
// BEFORE: Custom streaming logic in OpenSubsonicApiService
public async Task<ResponseModel> StreamAsync(StreamRequest request, ApiRequest apiRequest, CancellationToken cancellationToken)
{
    // 200+ lines of custom file I/O, range requests, content-type handling
    var song = await GetSongFromDatabase(songApiKey);
    var fileInfo = new FileInfo(song.FilePath);
    // Manual range request handling
    // Custom content-type detection
    // Manual logging and scrobbling
}

// AFTER: Leverage SongService
public async Task<ResponseModel> StreamAsync(StreamRequest request, ApiRequest apiRequest, CancellationToken cancellationToken)
{
    var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
    if (!authResponse.IsSuccess) return authResponse;

    var songApiKey = ApiKeyFromId(request.Id);
    if (songApiKey == null) return ErrorResponse("Invalid song ID");

    var streamResult = await songService.GetStreamForSongAsync(
        songApiKey.Value,
        request.Format,
        request.MaxBitRate,
        cancellationToken);

    if (!streamResult.IsSuccess)
        return ErrorResponse(streamResult.Messages?.FirstOrDefault() ?? "Stream not available");

    // Optional: Log scrobble using existing ScrobbleService
    if (request.ShouldScrobble)
    {
        await scrobbleService.ScrobbleAsync(authResponse.UserInfo.ApiKey, songApiKey.Value, cancellationToken);
    }

    return new ResponseModel
    {
        IsSuccess = true,
        StreamResult = streamResult.Data
    };
}
```

**Benefits**:
- Use tested streaming logic with proper error handling
- Eliminate 200+ lines of file I/O code
- Automatic format conversion and bitrate handling

### 2.2 Replace Custom Image Handling

**Current Issue**: `GetImageForApiKeyId()` contains complex image processing and caching

**Solution**: Use existing `AlbumService.GetAlbumImageBytesAndEtagAsync()`, `ArtistService.GetArtistImageBytesAndEtagAsync()`

**Refactored Implementation**:
```csharp
// BEFORE: Custom image handling in OpenSubsonicApiService
private async Task<ImageBytesAndEtag> GetImageForApiKeyId(string? id, string? size)
{
    // 150+ lines of custom image processing, caching, resizing
    var cacheKey = GenerateCustomCacheKey(id, size);
    // Custom cache management
    // Manual image resizing
    // Default image fallback logic
}

// AFTER: Leverage existing image services
private async Task<ImageBytesAndEtag> GetImageForApiKeyId(string? id, string? size, CancellationToken cancellationToken)
{
    var apiKey = ApiKeyFromId(id);
    if (apiKey == null)
        return new ImageBytesAndEtag(null, null);

    if (IsApiIdForAlbum(id))
    {
        return await albumService.GetAlbumImageBytesAndEtagAsync(apiKey.Value, size, cancellationToken);
    }
    else if (IsApiIdForArtist(id))
    {
        return await artistService.GetArtistImageBytesAndEtagAsync(apiKey.Value, size, cancellationToken);
    }
    else if (IsApiIdForPlaylist(id))
    {
        return await playlistService.GetPlaylistImageBytesAndEtagAsync(apiKey.Value, size, cancellationToken);
    }

    return defaultImages.GetDefaultImage(ImageType.Unknown, size);
}
```

**Benefits**:
- Use tested image processing with proper caching
- Eliminate 150+ lines of image handling code
- Consistent image caching across application

### 2.3 Replace Custom Search Logic

**Current Issue**: `SearchAsync()` contains raw SQL queries

**Solution**: Use existing `SearchService.SearchAsync()` and domain service search methods

**Refactored Implementation**:
```csharp
// BEFORE: Raw SQL in OpenSubsonicApiService
public async Task<ResponseModel> SearchAsync(SearchRequest request, bool isSearch3, ApiRequest apiRequest, CancellationToken cancellationToken)
{
    // 300+ lines of raw SQL queries
    await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken);
    var dbConn = scopedContext.Database.GetDbConnection();
    var artistSql = "SELECT * FROM Artists WHERE...";
    var albumSql = "SELECT * FROM Albums WHERE...";
    var songSql = "SELECT * FROM Songs WHERE...";
}

// AFTER: Leverage existing search services
public async Task<ResponseModel> SearchAsync(SearchRequest request, bool isSearch3, ApiRequest apiRequest, CancellationToken cancellationToken)
{
    var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
    if (!authResponse.IsSuccess) return authResponse;

    var searchQuery = new PagedRequest
    {
        Page = 1,
        PageSize = request.Count ?? 20,
        Filter = request.Query
    };

    // Use existing domain service search methods
    var artistResults = await artistService.SearchAsync(searchQuery, cancellationToken);
    var albumResults = await albumService.SearchAsync(searchQuery, cancellationToken);
    var songResults = await songService.SearchAsync(searchQuery, cancellationToken);

    var searchResult = new SearchResult3
    {
        Artists = artistResults.Data.Select(MapToOpenSubsonicArtist).ToArray(),
        Albums = albumResults.Data.Select(MapToOpenSubsonicAlbum).ToArray(),
        Songs = songResults.Data.Select(MapToOpenSubsonicSong).ToArray()
    };

    return new ResponseModel
    {
        IsSuccess = true,
        ResponseData = await NewApiResponse(true, searchResult)
    };
}
```

**Benefits**:
- Use tested search logic with proper indexing
- Eliminate 300+ lines of raw SQL
- Consistent search behavior across application

## Phase 3: Enhance Existing Services (Only Where Needed)

### 3.1 Add Missing Methods to UserService

**Only add if not already available**:

```csharp
public class UserService // Enhanced only where needed
{
    // Add only if missing - check existing methods first
    Task<OperationResult<bool>> ValidateTokenAsync(string username, string token, string salt, CancellationToken cancellationToken);
    Task<OperationResult<bool>> SetArtistRatingAsync(Guid userApiKey, Guid artistApiKey, int rating, CancellationToken cancellationToken);

    // Bookmark management (if not in existing services)
    Task<OperationResult<bool>> CreateBookmarkAsync(Guid userApiKey, Guid songApiKey, long positionMs, string? comment, CancellationToken cancellationToken);
    Task<OperationResult<bool>> DeleteBookmarkAsync(Guid userApiKey, Guid songApiKey, CancellationToken cancellationToken);
}
```

### 3.2 Add Missing Methods to PlaylistService

**Only add if not already available**:

```csharp
public class PlaylistService // Enhanced only where needed
{
    // Add only if missing image handling
    Task<ImageBytesAndEtag> GetPlaylistImageBytesAndEtagAsync(Guid playlistApiKey, string? size, CancellationToken cancellationToken);

    // Add only if missing song management (check existing methods)
    Task<OperationResult<bool>> AddSongsToPlaylistAsync(Guid playlistApiKey, IEnumerable<Guid> songApiKeys, CancellationToken cancellationToken);
    Task<OperationResult<bool>> RemoveSongsFromPlaylistAsync(Guid playlistApiKey, IEnumerable<int> songIndexes, CancellationToken cancellationToken);
}
```

## Phase 4: Clean API Service

### 4.1 Remove All Direct Database Access

**Pattern for all methods**:
```csharp
public class OpenSubsonicApiService // Refactored
{
    // Remove all direct database access patterns like:
    // await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken);
    // var dbConn = scopedContext.Database.GetDbConnection();

    // Replace with domain service calls:
    public async Task<ResponseModel> GetAlbumsAsync(GetAlbumsRequest request, ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess) return authResponse;

        var pagedRequest = MapToPagedRequest(request);
        var albumsResult = await albumService.ListAsync(pagedRequest, cancellationToken);

        return MapToResponseModel(albumsResult);
    }
}
```

### 4.2 Focus on API Concerns Only

**Remaining Responsibilities**:
- Request validation and parameter parsing using existing validation
- Response model mapping and transformation
- HTTP status code determination
- Error message formatting using existing error patterns
- API versioning concerns

## Implementation Schedule

### Phase 1 - Authentication & User Services
- [ ] Replace custom authentication with `UserService.LoginUserAsync()`
- [ ] Replace custom rating logic with `UserService.SetAlbumRatingAsync()` etc.
- [ ] Replace custom starring logic with `UserService.ToggleAlbumStarAsync()` etc.
- [ ] Add token validation to UserService if missing

### Phase 2 - Media & Image Services
- [ ] Replace custom streaming with `SongService.GetStreamForSongAsync()`
- [ ] Replace custom image handling with `AlbumService.GetAlbumImageBytesAndEtagAsync()`
- [ ] Replace custom search with existing search services
- [ ] Remove all raw SQL queries

### Phase 3 - Service Enhancement
- [ ] Add missing methods to existing services (minimal additions)
- [ ] Consolidate any duplicate search functionality
- [ ] Ensure all OpenSubsonic features are covered by domain services

### Phase 4 - API Service Cleanup
- [ ] Remove all direct database access from OpenSubsonicApiService
- [ ] Implement consistent error handling using service patterns
- [ ] Focus API service on request/response transformation only
- [ ] Performance testing and optimization

### Testing & Documentation
- [ ] Comprehensive integration testing
- [ ] Performance regression testing
- [ ] Update API documentation
- [ ] Code review and cleanup

## Success Metrics

### Code Quality
- [ ] OpenSubsonicApiService reduced from 3,797 to < 1,000 lines
- [ ] Zero direct database access in API service
- [ ] Zero raw SQL queries in API service
- [ ] All functionality delegated to existing domain services

### Performance
- [ ] No degradation in API response times
- [ ] Improved caching hit rates through service-level caching
- [ ] Reduced memory usage by eliminating duplicate code

### Maintainability
- [ ] API service focused solely on API concerns
- [ ] Clear separation between API and domain logic
- [ ] Reuse of existing tested service methods

## Risk Mitigation

### Testing Strategy
1. **Existing Tests**: All existing domain service tests continue to pass
2. **API Tests**: All OpenSubsonic API endpoints maintain same behavior
3. **Performance Tests**: Ensure leveraging services doesn't degrade performance

### Backward Compatibility
1. **API Contracts**: All existing API endpoints maintain exact same contracts
2. **Service Contracts**: Existing domain services maintain their interfaces
3. **Configuration**: No configuration changes required

## Conclusion

This refactoring leverages Melodee's existing mature service architecture instead of creating new services. The benefits include:

- **Proven Reliability**: Use battle-tested domain service methods
- **Reduced Code**: Eliminate 2,000+ lines of duplicate logic
- **Better Performance**: Leverage existing optimized caching strategies
- **Maintainability**: Single source of truth for business logic
- **Consistency**: Uniform behavior across all application entry points

The key insight is that Melodee already has a comprehensive service layer - the refactoring simply needs to use it effectively instead of reimplementing domain logic in the API service.
