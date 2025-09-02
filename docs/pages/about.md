---
title: About
permalink: /about/
---

# About

Melodee is an open source, high‑performance music management and streaming system designed for very large personal or organizational libraries (tens of millions of tracks). It combines a modern Blazor Server UI, a powerful media ingestion and normalization pipeline, and dual API surfaces (OpenSubsonic compatible + native Melodee REST) for broad client compatibility.

## Vision

Provide a self‑hosted, privacy‑respecting platform to: ingest messy music collections, enrich them with high‑quality metadata & artwork, curate and stage edits safely, and stream to any Subsonic / OpenSubsonic compatible client—or to custom integrations via a clean JSON REST API.

## Core Pillars

- Scales to gigantic libraries through efficient background jobs and incremental scans.
- Pluggable metadata & artwork enrichment (MusicBrainz, Last.FM, iTunes, Spotify, etc.).
- Powerful inbound -> staging -> production workflow for clean, consistent libraries.
- Real‑time transcoding and optimized streaming path (range requests, concurrency limits).
- First‑class API contracts (OpenSubsonic + Melodee native) with versioning.
- Configuration‑driven behavior (rules engine for tag cleanup, naming, validation).

## High‑Level Architecture

Component overview:

| Component | Purpose | Key Tech |
|-----------|---------|---------|
| Melodee Blazor | Administrative web UI + OpenSubsonic & native REST API host + streaming pipeline | .NET 9, Blazor Server, Radzen |
| Melodee API (native / OpenSubsonic) | Programmatic access layer consumed by external clients & integrations | ASP.NET Core, API Versioning |
| Melodee.Cli | Operational & maintenance commands (jobs, migrations, utilities) | .NET Console |
| MeloAmp | Cross‑platform desktop client (browse, play, queue mgmt, theming, equalizer, scrobbling) | Electron, React, Material‑UI, TypeScript |
| Melodee Player | Native Android & Android Auto streaming client (voice, Media3 playback, clean architecture) | Kotlin, Jetpack Compose, Media3 |


### Component Roles

- **Melodee.Blazor**: Hosts APIs, runs background jobs, presents the administrative and power‑user interface (metadata editing, artwork, user/security settings, job dashboard).
- **Melodee API**: Two faces—OpenSubsonic (compatibility for existing ecosystem clients) and Native JSON (clean, opinionated resource models). Both sit behind the same hosting process for efficiency.
- **MeloAmp**: Electron desktop app offering fast browsing (artist / album / song / playlist), drag‑and‑drop queue, starring / favoriting, playlist saving, user theme + equalizer persistence, JWT auth, scrobbling. Ships cross‑platform packages (AppImage, DEB, RPM, Snap, Pacman, tar.gz; Windows & macOS builds planned/experimental). Ideal for desktop users wanting a richer UI than generic Subsonic clients.
- **Melodee Player**: Kotlin/Compose Android + Android Auto app with Clean Architecture layers (data/domain/presentation/service). Provides automotive‑safe UI, voice commands, MediaSession integration, playlist browsing, search, pull‑to‑refresh, persistent now‑playing bar, and scrobbling. Targets API 21–35 with modern tooling (AGP 8.x, Kotlin 1.9+).

Together these deliver an end‑to‑end ecosystem: ingestion & curation (server) → optimized APIs → native & desktop clients tuned for their platform capabilities (voice control in cars, system tray / desktop media keys on desktops—media key integration planned for MeloAmp).

### Integration Notes

- Both clients primarily consume the native Melodee API; OpenSubsonic layer remains available for legacy third‑party apps.
- Scrobbling events originate client‑side and flow through the `Scrobble` native endpoint to update play history and forward to external scrobble services (if configured).
- Equalizer & visual theming (MeloAmp) are client‑local preferences; server interaction limited to playback, metadata, and user actions (ratings, starring).
- Android Auto voice intents are mapped to search + playback actions over the native API; resilience features (retry/backoff) included in player networking stack.

## Typical Use Cases

- Replace aging Subsonic server with a modern, actively maintained alternative.
- Consolidate multiple scattered music folders into a normalized library.
- Run large private label streaming for a band/collective with editorial control.
- Power analytics or recommendation engines via the structured REST endpoints.

## Support

Need help? Start with:

- Documentation site (this site) for setup & API reference.
- Discord community for real‑time Q&A.
- GitHub Issues for bugs and feature requests.
- GitHub Discussions for design proposals & architecture questions.

If something critical is missing from docs, please open an issue—contributions to documentation are highly valued.

## Community & Contributions

We welcome pull requests! Good first contributions include: fixing typos in docs, adding API usage examples, improving test coverage for services, or building new metadata plugins. Please review the Code of Conduct and contributing guidelines before starting.

## Licensing

Melodee is MIT licensed—permissive for both personal and commercial use. Attribution is appreciated but not required.

## Acknowledgments

Thanks to the wider open media ecosystem and the maintainers of libraries and specifications that make Melodee possible (OpenSubsonic, tag libraries, metadata providers, .NET OSS tooling, and UI component authors).

---

Made with ❤️ by the Melodee community.

