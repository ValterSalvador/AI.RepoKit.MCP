---
description: Safety rules for scoped repository changes.
target: vscode
---

# Safe Change Instructions

Keep edits scoped to the user request. Preserve unrelated local changes. Do not overwrite unmanaged files without explicit approval.

Generated AiRepoKit files should stay under the managed-files manifest. Existing unmanaged agent, instruction, and prompt files require manual review instead of overwrite.

Do not read secrets or local credentials. Do not run app servers, Docker, migrations, SQL, or database commands unless explicitly approved.
