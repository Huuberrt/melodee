---
title: Async Best Practices
layout: page
---

Guidelines

- Avoid `.Result`, `.Wait()`, and `GetAwaiter().GetResult()` in request threads and service code.
- Prefer end-to-end async with `await` and `ConfigureAwait(false)` in libraries.
- Do not block in DI registrations; resolve async services at runtime where needed.
- Use `Task.Run` sparingly; prefer true async I/O APIs over CPU offloading.

Exceptions

- Generated code or APIs requiring synchronous contracts may be excluded with clear justification.
- UI components with purely sync needs should avoid deep async plumbing unless necessary.

