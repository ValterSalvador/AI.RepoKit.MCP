# Diagnose Build

1. Restore packages.
2. Build the smallest affected project or solution.
3. Capture the first meaningful error.
4. Use `get_context symbols brief` to locate the likely type, service, handler, or DbContext.
5. Inspect only relevant source files.
6. Avoid servers, Docker, migrations, SQL, and database commands.
7. Report fixes and remaining failures.
