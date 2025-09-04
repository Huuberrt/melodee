# Melodee TODO Task List

This document aggregates outstanding TODOs and unimplemented methods across the repository. Items are grouped by domain and organized into phases to guide implementation. Check items off as they are completed.

Last updated: ${DATE}

## Phase 1 — Core API & Serialization

- [x] Songs API: implement `SongById(Guid id)`
  - File: `src/Melodee.Blazor/Controllers/Melodee/SongsController.cs`
  - Status: Implemented.
  - Notes: Should authorize, fetch song by ApiKey/Id, return DTO model with pagination metadata alignment.

- [x] Subsonic Auth: handle JWT auth branch
  - File: `src/Melodee.Common/Services/OpenSubsonicApiService.cs`
  - Location: `AuthenticateSubsonicApiAsync` (JWT section)
  - Status: Implemented.
  - Notes: Follow Navidrome semantics for JWT; verify token, map to user.

- [x] JSON Converter: implement `OpenSubsonicResponseModelConvertor.Read`
  - File: `src/Melodee.Common/Serialization/Convertors/OpenSubsonicResponseModelConvertor.cs`
  - Status: Implemented.
  - Notes: Should deserialize Subsonic format into `ResponseModel` (only needed if reading Subsonic JSON from clients/tests).

- [x] Song deletion: implement `SongService.DeleteAsync(int[])`
  - File: `src/Melodee.Common/Services/SongService.cs`
  - Status: Implemented.
  - Notes: Respect referential integrity, remove cache and related `UserSong` entries; tests expect NotImplemented currently.

- [x] Library deletion: implement `LibraryService.DeleteAsync(int[])`
  - File: `src/Melodee.Common/Services/LibraryService.cs`
  - Status: Implemented.
  - Notes: Must validate libraries are empty or define cascade/cleanup behavior; ensure cache invalidation.

## Phase 2 — Metadata & Tagging

- [x] ID3v2 writer: implement writing/updating multiple ID3v2.4 tags
  - File: `src/Melodee.Common/Metadata/AudioTags/Writers/Id3v2TagWriter.cs`
  - Status: Implemented (write, remove, images).
  - Notes: Leverage library in use (ATL/IdSharp) with safe fallbacks.

- [x] IdSharpMetaTag: implement `UpdateSongAsync(...)`
  - File: `src/Melodee.Common/Plugins/MetaData/Song/IdSharpMetaTag.cs`
  - Status: Implemented.
  - Notes: Update media file tags on disk from domain `Song` instance; integrate with validators.

- [x] Fill TODO fields in album/song DTO conversions
  - Files:
    - `src/Melodee.Common/Data/Models/Extensions/AlbumExtensions.cs` (several `//TODO` array/null placeholders)
    - `src/Melodee.Common/Data/Models/Extensions/SongExtensions.cs` (several `//TODO` array/null placeholders)
  - Status: Implemented (genres, artists, contributors, moods, replay gain).

## Phase 3 — Search Engines & Scrobbling

- [ ] iTunes Search: implement `DoArtistSearchAsync`/related pieces
  - File: `src/Melodee.Common/Plugins/SearchEngine/ITunes/ITunesSearchEngine.cs`
  - Status: method ends with `NotImplementedException`.
  - Notes: Build HTTP client queries, parse results, map to `ArtistSearchResult`.

- [ ] Last.fm Search: implement `DoArtistSearchAsync`
  - File: `src/Melodee.Common/Plugins/SearchEngine/LastFm/LastFm.cs`
  - Status: throws `NotImplementedException`.
  - Notes: Use Last.fm API to return artist results or gracefully disable if API keys missing.

- [ ] Last.fm Scrobbler: use user session key in `NowPlaying`
  - File: `src/Melodee.Common/Plugins/Scrobbling/LastFmScrobbler.cs`
  - Status: `// TODO` comment notes session handling.
  - Notes: Add session management (store `LastFmSessionKey` on user), retry/backoff via Polly.

## Phase 4 — OpenSubsonic Endpoints

- [ ] Implement Similar Songs endpoints
  - File: `src/Melodee.Blazor/Controllers/OpenSubsonic/BrowsingController.cs`
  - Status: `//TODO getSimilarSongs`, `getSimilarSongs2`.
  - Notes: Derive similarity from play counts/tags or search engines; keep pagination.

- [ ] Jukebox Control endpoints
  - File: `src/Melodee.Blazor/Controllers/OpenSubsonic/JukeboxController.cs`
  - Status: `//TODO jukeboxControl` placeholder.
  - Notes: Decide whether to support; if not, keep returning 410 Gone consistently.

## Phase 5 — Blazor UI Actions

- [ ] ImageSearchUpload: handle Radzen Upload events
  - File: `src/Melodee.Blazor/Components/Components/ImageSearchUpload.razor`
  - Methods: `OnChange(byte[] value, string name)`, `OnError(UploadErrorEventArgs args, string name)`
  - Status: `NotImplementedException`.
  - Notes: Wire into selection flow; produce `ImageSearchResult` and close dialog.

- [ ] IdentifyAlbum: handle Upload events
  - File: `src/Melodee.Blazor/Components/Components/IdentifyAlbum.razor`
  - Methods: `OnChange(...)`, `OnError(...)`
  - Status: `NotImplementedException`.
  - Notes: Similar to ImageSearchUpload; produce temporary image and trigger search.

- [ ] ArtistEdit: external search integration button
  - File: `src/Melodee.Blazor/Components/Pages/Data/ArtistEdit.razor`
  - Method: `SearchForExternalButtonClick(string amgid)`
  - Status: `NotImplementedException`.

- [ ] AlbumEdit: external search integration button
  - File: `src/Melodee.Blazor/Components/Pages/Media/AlbumEdit.razor`
  - Method: `SearchForExternalButtonClick(string amgid)`
  - Status: `NotImplementedException`.

- [ ] PlaylistDetail: image set/lock/unlock actions
  - File: `src/Melodee.Blazor/Components/Pages/Data/PlaylistDetail.razor`
  - Methods: `SetPlaylistImageButtonClick()`, `UnlockButtonClick()`, `LockButtonClick()`
  - Status: `NotImplementedException`.

- [ ] Library page: multi-library move prompt and clean action
  - File: `src/Melodee.Blazor/Components/Pages/Media/Library.razor`
  - Tasks:
    - When moving albums and multiple Storage libraries exist, prompt for target library.
    - Implement `CleanButtonClick()`.
  - Status: `// TODO` + `NotImplementedException`.

- [ ] LibraryDetail: Edit button behavior
  - File: `src/Melodee.Blazor/Components/Pages/Data/LibraryDetail.razor`
  - Method: `EditButtonClick()`
  - Status: `NotImplementedException`.

- [ ] Albums/Songs grids: unimplemented actions
  - Files:
    - `src/Melodee.Blazor/Components/Pages/Data/Albums.razor`
    - `src/Melodee.Blazor/Components/Pages/Data/Songs.razor`
  - Status: one or more `NotImplementedException` handlers.

## Phase 6 — Service/Domain Polish

- [ ] ServiceBase: verify CRC calculation comment and resolve discrepancy
  - File: `src/Melodee.Common/Services/ServiceBase.cs`
  - Status: `// TODO for some reason the song.CrcHash is wrong?`
  - Notes: Add tests to validate computed CRC versus stored value; fix path resolution if needed.

- [ ] ArtistExtensions: verify `Url` placeholder
  - File: `src/Melodee.Common/Data/Models/Extensions/ArtistExtensions.cs`
  - Status: `// TODO ?` next to `"Url"` property assignment.
  - Notes: Confirm whether to populate canonical artist URL from sources.

## Phase 7 — Testing & Tooling

- [ ] UserServiceTests: verify “bus event published” assertion
  - File: `tests/Melodee.Tests.Common/Common/Services/UserServiceTests.cs`
  - Status: `// TODO` comment
  - Notes: Add mock/spies to assert bus publish called on appropriate actions.

---

## Index of References

For quick grep reference, these locations contain TODOs or NotImplementedException:

- tests/Melodee.Tests.Common/Common/Services/SongServiceTests.cs (NotImplemented expected in Delete test)
- tests/Melodee.Tests.Common/Common/Services/UserServiceTests.cs (TODO bus event)
- src/Melodee.Blazor/Controllers/Melodee/SongsController.cs (SongById)
- src/Melodee.Blazor/Controllers/OpenSubsonic/JukeboxController.cs (jukeboxControl)
- src/Melodee.Blazor/Controllers/OpenSubsonic/BrowsingController.cs (similar songs)
- src/Melodee.Common/Serialization/Convertors/OpenSubsonicResponseModelConvertor.cs (Read)
- src/Melodee.Common/Plugins/MetaData/Song/IdSharpMetaTag.cs (UpdateSongAsync)
- src/Melodee.Blazor/Components/Components/ImageSearchUpload.razor (Upload handlers)
- src/Melodee.Blazor/Components/Components/IdentifyAlbum.razor (Upload handlers)
- src/Melodee.Common/Plugins/Scrobbling/LastFmScrobbler.cs (session key)
- src/Melodee.Common/Services/ServiceBase.cs (CRC hash comment)
- src/Melodee.Common/Data/Models/Extensions/SongExtensions.cs (DTO TODO fields)
- src/Melodee.Common/Data/Models/Extensions/ArtistExtensions.cs (Url TODO)
- src/Melodee.Common/Services/OpenSubsonicApiService.cs (JWT auth NotImplemented)
- src/Melodee.Common/Data/Models/Extensions/AlbumExtensions.cs (DTO TODO fields)
- src/Melodee.Common/Plugins/SearchEngine/ITunes/ITunesSearchEngine.cs (NotImplemented)
- src/Melodee.Blazor/Components/Pages/Media/ArtistEdit.razor (Search external button)
- src/Melodee.Common/Plugins/SearchEngine/LastFm/LastFm.cs (NotImplemented search)
- src/Melodee.Common/Services/SongService.cs (DeleteAsync)
- src/Melodee.Common/Services/LibraryService.cs (DeleteAsync)
- src/Melodee.Blazor/Components/Pages/Media/AlbumEdit.razor (Search external button)
- src/Melodee.Blazor/Components/Pages/Media/AlbumDetail.razor (multi-library prompt TODO)
- src/Melodee.Blazor/Components/Pages/Media/Library.razor (multi-library move prompt, clean button)
- src/Melodee.Blazor/Components/Pages/Media/Library.razor (as above)
- src/Melodee.Blazor/Components/Pages/Data/ArtistEdit.razor (as above)
- src/Melodee.Blazor/Components/Pages/Data/Albums.razor (NotImplemented handlers)
- src/Melodee.Blazor/Components/Pages/Data/Songs.razor (NotImplemented handlers)
- src/Melodee.Blazor/Components/Pages/Data/PlaylistDetail.razor (image/lock/unlock)
- src/Melodee.Blazor/Components/Pages/Data/AlbumEdit.razor (as above)
- src/Melodee.Blazor/Components/Pages/Data/LibraryDetail.razor (Edit button)
