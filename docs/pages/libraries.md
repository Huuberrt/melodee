---
title: Libraries
permalink: /libraries/
---

# Libraries

Libraries are the backbone of how Melodee organizes media through its lifecycle. Understanding each library type helps optimize ingestion speed, metadata quality, and storage efficiency.

## Lifecycle Overview

```
Inbound  ->  Processing / Normalization  ->  Staging  ->  Review / Edit  ->  Storage (Published)
```

### Inbound Library

Drop raw, unprocessed audio files here (rips, downloads, untagged collections). The ingestion job scans this path periodically.

Key actions:

- Format conversion (if necessary)
- Loudness / technical validation (future expansion)
- Tag normalization & regex cleanup
- Duplicate detection heuristics (planned)

### Staging Library

Files that passed basic validation but may need human curation. While in staging they are not visible to end user playback APIs.

You can:

- Manually edit or merge metadata.
- Attach or replace artwork.
- Resolve duplicates or conflicting releases.

Promotion from staging to storage triggers indexing and exposes content for streaming/search.

### Storage Libraries

Published, canonical library roots. You can have multiple storage libraries (e.g., by genre, decade, physical drive, or performance tier).

Benefits of multiple storage libraries:

- Distribute capacity across NAS mounts / network paths.
- Keep high‑resolution masters separate from compressed collections.
- Apply different backup or snapshot policies.

### Library Indexing

When items enter storage, a metadata index is updated (songs, albums, artists, relationships). This powers fast queries for both OpenSubsonic & native APIs.

### Artwork & Ancillary Assets

Artwork is cached alongside storage libraries for fast retrieval; user avatars and playlist definitions live in their own dedicated volumes.

### Naming & Organization

Default naming rules aim for consistency: Artist/Year - Album/TrackNumber - Title.ext (subject to configuration). Future versions will allow advanced templating.

### Space Management Tips

- Run periodic cleanup jobs (planned) to purge orphaned staging artifacts.
- Consider a quota or monitoring on inbound to avoid runaway disk usage.
- Deduplicate via external tooling before dropping huge batches into inbound when possible.

### Backups

Prioritize: storage libraries > database > staging (optional) > inbound (usually ephemeral). Always back up artwork if you’ve invested manual curation time.

### Future Enhancements

- Cross‑library move & rebalancing tool.
- Duplicate cluster detection & resolution UI.
- Smart differential re‑scan (skip unchanged directory trees).

Have feedback or a library management pain point? Open a discussion so we can refine the roadmap.

