---
title: Configuration
permalink: /configuration/
---

# Configuration

Melodee exposes configuration through environment variables, the web UI, and (internally) a dynamic settings registry. This page outlines common areas to tune.

## Configuration Sources

Priority (highest wins):

1. Environment variables (.env / container env)
2. UI overrides (persisted in database)
3. Default appsettings & internal defaults

## Core Categories

### Server & Network

- MELODEE_PORT: External port to expose web & API.
- BASE_URL (planned): Canonical base URL for reverse proxy setups.

### Database

Provided via compose. Ensure DB_PASSWORD is strong. For external Postgres, set connection string variables (coming doc expansion).

### Libraries

Three logical areas:

- Inbound: Raw, unprocessed files.
- Staging: Processed, awaiting review & metadata edits.
- Storage: Published canonical library (served to users / APIs).

Ensure sufficient disk and backup strategy, especially for storage volume.

### Metadata & Enrichment

Providers: MusicBrainz (local cache), Last.FM, Spotify, iTunes.

Typical options (UI section):

- Enable/disable provider.
- API keys / tokens.
- Artwork size preferences.
- Local MusicBrainz database refresh interval.

### Ingestion Rules

Rule engine applies deterministic transformations:

- Remove "(feat. X)" from title -> moves featured artist into artist metadata fields.
- Normalize numbering (track 1 -> 01 or 1 based on style).
- Strip stray unicode punctuation / marketing phrases.
- Enforce required tags (Album, Artist, Title, Track, Duration) else item stays in staging.

### Transcoding & Streaming

Settings (some forthcoming in UI):

- Preferred output format (Opus / MP3) for constrained bandwidth clients.
- Max concurrent streams per user (enforced by streaming limiter service).
- Buffered vs direct streaming (SettingRegistry.StreamingUseBufferedResponses). Buffered is safer for some reverse proxies; direct is lower latency.

### Jobs & Scheduling

Jobs run on cron‑like schedules (scan inbound, stage promotion, metadata refresh). Adjust intervals balancing freshness vs resource usage.

### Security

- First user is admin—create additional non‑admin accounts for daily use.
- API keys are GUIDs associated with users; rotate by regenerating user key if compromised.
- Blacklist service can deny by email or IP (used to mitigate abuse).

### Logging

Structured logging via Serilog. Configure sinks (console, file, etc.) in appsettings or environment overrides (documentation forthcoming for custom sinks).

## Environment Variable Examples

```
DB_PASSWORD=supersecret
MELODEE_PORT=8080
# FUTURE (illustrative):
# STREAMING_USE_BUFFERED=true
# MAX_CONCURRENT_STREAMS=3
```

## Observability & Metrics

System statistics endpoint (native API) surfaces counts (songs, albums, artists, etc.) — see /api/ for details. Future metrics (transcoding time, cache hit rates) planned.

## Hardening Checklist

- Put behind a reverse proxy (nginx / Caddy) with TLS.
- Restrict inbound port to proxy layer only.
- Regularly export DB & storage backups.
- Monitor log volume for anomalous access.

## Updating Configuration

1. Change value in UI or env.
2. Restart service if an env var (some dynamic settings reload automatically).
3. Observe logs for validation warnings.

## Coming Soon

- Editable YAML/JSON advanced config export/import.
- Live reload for transcoding profiles.
- Per‑user bandwidth caps.

Have a config use‑case not covered? Open an issue so we can expand this section.

