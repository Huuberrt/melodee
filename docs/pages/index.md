---
layout: page
title: Melodee
permalink: /
---
# Melodee Music System

> ⚠️ Documentation is evolving. Core concepts & API reference are being expanded. Check back frequently. ⚠️

Melodee is a self‑hosted music management & streaming platform. It ingests disorganized audio files; cleans, normalizes & enriches metadata; stages human edits; then serves a pristine library over both the OpenSubsonic protocol and a native JSON REST API.

Think of it as a blend of:

- A streaming server (e.g. [Navidrome](https://github.com/navidrome/navidrome))
- A tag & artwork editor (e.g. [Mp3Tag](https://www.mp3tag.de/en/))
- An automated metadata enrichment + library quality pipeline

## End‑to‑End Flow

1. Inbound scan detects new files in the inbound volume.
2. Ingestion converts/transcodes (if needed), normalizes tags, applies regex cleanup & validation.
3. Items move to Staging for optional manual metadata & artwork curation.
4. Approved items are published into one or more Storage Libraries.
5. Indexed metadata powers fast search, browsing & streaming via APIs.

## Feature Highlights

- Media normalization & configurable tag rewrite rules.
- Regex driven cleanup (featuring/with removal, numbering fixes, stray tokens).
- Multi‑stage pipeline (Inbound ➜ Staging ➜ Storage).
- Pluggable metadata & artwork fetch (MusicBrainz local cache, Last.FM, Spotify, iTunes).
- Real‑time transcoding (MP3, Ogg, Opus, etc.) with range & partial streaming.
- Cron‑like job scheduler (scans, enrichment, cleanup, background sync).
- Multi‑library federation (spread storage across NAS / mounts).
- Blazor Server UI for metadata, artwork, users, config & monitoring.
- OpenSubsonic compatibility (tested against several popular clients).
- Native REST API (versioned) for custom integrations.
- User features: starring, ratings, scrobbling, play tracking.
- Concurrency‑aware streaming limiter & optional buffered responses.

## Tested OpenSubsonic Clients

- Airsonic (refix)
- Dsub
- Feishin
- Symphonium
- Sublime Music
- Supersonic
- Ultrasonic

## Quick Links

- Installation: /installing/
- Configuration: /configuration/
- Libraries Concept: /libraries/
- API Reference (OpenSubsonic + Native): /api/
- Changelog / News: /news/
- About & Project Direction: /about/

## Contributing

Found a gap or want to propose an improvement? Open a discussion or issue: {{ site.repo }}/issues — Documentation PRs are especially welcome.

---

Happy streaming! 🎵
