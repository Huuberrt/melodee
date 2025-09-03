---
title: EF Core Query Splitting Strategy
layout: page
---

Overview

- Default: Use `QuerySplittingBehavior.SplitQuery` for contexts to avoid cartesian explosion on complex includes.
- Per-query override: Prefer `AsSingleQuery()` only when projections are small and joins are simple.
- Always combine with `AsNoTracking()` for read-only queries.

When to use which

- Split queries: Multiple `Include().ThenInclude()` paths; large graphs; risk of duplicate data; read-mostly operations.
- Single query: Small, simple joins; projection to flat DTOs without multiple collection navigations.

Examples

- Split: `context.Songs.Include(s => s.Album).ThenInclude(a => a.Artist).AsSplitQuery().AsNoTracking()`
- Single: `context.Songs.Select(s => new { s.Id, s.Title, Artist = s.Album.Artist.Name }).AsNoTracking()`

