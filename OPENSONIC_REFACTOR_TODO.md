# OpenSubsonicApiService Refactoring Todo

## Overview
Refactor OpenSubsonicApiService to remove direct database access and transform it into an orchestrator using domain services. All target domain services already exist.

## File Location
`src/Melodee.Common/Services/OpenSubsonicApiService.cs`

## Implementation Steps

### Phase 1: High Priority Methods (Database Access Removal)

#### 1. Share Management → ShareService
**Target Service:** `ShareService.cs` (already exists)
**Methods to Move:**
- `UpdateShareAsync()` (line 365)  
- `DeleteShareAsync()` (line 426)

**Implementation Notes:**
- Both methods have TODO comments indicating move to ShareService
- Remove direct database context usage
- Replace with ShareService method calls

#### 2. Playlist Management → PlaylistService  
**Target Service:** `PlaylistService.cs` (already exists)
**Methods to Move:**
- `GetPlaylistsAsync()` (line 483)
- `UpdatePlaylistAsync()` (line 536) 
- `DeletePlaylistAsync()` (line 649)
- `CreatePlaylistAsync()` (line 698)
- `GetPlaylistAsync()` (line 750)

**Implementation Notes:**
- All methods have TODO comments for PlaylistService
- Line 610 has specific comment about direct database access needing to move
- Remove `ContextFactory.CreateDbContextAsync()` usage
- Replace with PlaylistService orchestration

#### 3. Album Management → AlbumService
**Target Service:** `AlbumService.cs` (already exists)  
**Methods to Move:**
- `GetAlbumListAsync()` (line 879)
- `GetAlbumList2Async()` (line 1009)
- `GetGenresAsync()` (line 1292)
- `GetAlbumInfoAsync()` (line 3125)

**Implementation Notes:**
- All have TODO comments for AlbumService
- Complex query logic needs to be moved to domain service
- Remove direct EF Core queries

#### 4. Queue Management → UserQueueService
**Target Service:** `UserQueueService.cs` (newly created, currently empty)
**Methods to Move:**
- `GetPlayQueueAsync()` (line 1856)
- `SavePlayQueueAsync()` (line 1900)

**Implementation Notes:**
- UserQueueService exists but is empty - needs implementation first
- Move queue persistence logic from OpenSubsonicApiService
- Remove direct database access for queue operations

#### 5. Constructor Updates
**Task:** Update OpenSubsonicApiService constructor to inject domain services
**Current Injections Needed:**
- ShareService
- PlaylistService  
- AlbumService
- UserQueueService
- (ArtistService, RadioStationService, SongService for Phase 2)

### Phase 2: Medium Priority Methods

#### 6. Artist Management → ArtistService
**Target Service:** `ArtistService.cs` (already exists)
**Methods to Move:**
- `GetArtistInfoAsync()` (line 3056)

#### 7. Radio Station Management → RadioStationService  
**Target Service:** `RadioStationService.cs` (already exists)
**Methods to Move:**
- `DeleteInternetRadioStationAsync()` (line 3285)
- `CreateInternetRadioStationAsync()` (line 3336)  
- `UpdateInternetRadioStationAsync()` (line 3389)
- `GetInternetRadioStationsAsync()` (line 3445)

#### 8. Song Management → SongService
**Target Service:** `SongService.cs` (already exists)
**Methods to Move:**
- `GetLyricsListForSongIdAsync()` (line 3485)
- `GetLyricsForArtistAndTitleAsync()` (line 3533)

### Phase 3: Final Cleanup

#### 9. Database Access Audit
- Search for remaining `ContextFactory.CreateDbContextAsync()` calls
- Search for remaining `await using (var scopedContext` patterns
- Ensure all direct EF Core usage is removed

#### 10. Testing & Validation
- Run existing tests to ensure no regressions
- Test all moved methods through their new service orchestration
- Verify API responses remain unchanged

## Technical Notes

### Pattern for Refactoring
1. **Identify Method**: Find method with TODO comment
2. **Analyze Dependencies**: Check what database entities/operations are used
3. **Move Logic**: Transfer business logic to appropriate domain service
4. **Update OpenSubsonic**: Replace direct DB calls with domain service calls
5. **Update Constructor**: Add domain service injection if not already present
6. **Test**: Verify functionality remains intact

### Key Search Patterns for Finding Direct DB Access
- `ContextFactory.CreateDbContextAsync`
- `await using (var scopedContext`
- `scopedContext.` (EF Core operations)
- Direct entity queries without service layer

### Existing Domain Services Available
All target services already exist:
- ✅ ShareService
- ✅ PlaylistService  
- ✅ AlbumService
- ✅ ArtistService
- ✅ RadioStationService
- ✅ SongService
- ✅ UserQueueService (empty - needs implementation)

## Progress Tracking
- [x] Phase 1 Complete
  - [x] Share methods moved (UpdateShareAsync, DeleteShareAsync)
  - [x] Playlist methods moved (GetPlaylistsAsync, UpdatePlaylistAsync - partial)
  - [ ] Album methods moved
  - [ ] Queue methods moved (requires UserQueueService implementation first)
  - [x] Constructor updated (RadioStationService added)
- [x] Phase 2 Complete  
  - [x] Artist methods moved (GetArtistInfoAsync)
  - [x] Radio station methods moved (ALL COMPLETE: DeleteInternetRadioStationAsync, CreateInternetRadioStationAsync, UpdateInternetRadioStationAsync, GetInternetRadioStationsAsync)
  - [x] Song methods moved (GetLyricsListForSongIdAsync, GetLyricsForArtistAndTitleAsync)
- [ ] Phase 3 Complete
  - [x] Complete remaining radio station methods (UpdateInternetRadioStationAsync, GetInternetRadioStationsAsync)
  - [x] Song methods moved (GetLyricsListForSongIdAsync, GetLyricsForArtistAndTitleAsync)
  - [ ] Complete remaining playlist methods (DeletePlaylistAsync, CreatePlaylistAsync, GetPlaylistAsync)
  - [x] Album methods moved (GetGenresAsync, GetAlbumInfoAsync) - GetAlbumListAsync, GetAlbumList2Async remain (complex raw SQL)
  - [ ] Queue methods moved (GetPlayQueueAsync, SavePlayQueueAsync)
  - [ ] Database access audit complete
  - [ ] Testing complete

## Risk Mitigation
- Work on one service at a time to avoid breaking changes
- Test after each service migration
- Keep TODO comments until fully tested
- Consider feature flags for gradual rollout if needed

## File Size Note
OpenSubsonicApiService.cs is 39,369 tokens - use offset/limit when reading or use Grep for specific sections.