---
title: API
permalink: /api/
---

# API

This is where documentation for the Melodee OpenSubsonic extensions will go.

## Recent Improvements

The OpenSubsonic API service has been recently refactored to improve maintainability and performance while maintaining full backward compatibility:

- **Enhanced Architecture**: Core functionality now leverages well-tested domain services instead of duplicate database logic
- **Improved Performance**: Better caching and reduced code duplication
- **Maintained Compatibility**: All existing API endpoints maintain exact same contracts and behavior
- **Better Reliability**: Uses battle-tested service methods for authentication, ratings, bookmarks, playlists, and search

All API endpoints continue to work exactly as before - this was an internal improvement that doesn't affect API consumers.

