---
description: Oracle and data-layer safety guidance.
target: vscode
---

# Oracle Datalayer Instructions

Treat connection strings, credentials, generated SQL, transactions, and schema assumptions as high-risk. Review parameterization, null handling, mapping boundaries, resource disposal, and error behavior.

Do not read secrets. Do not run SQL, migrations, database commands, Docker, or app servers unless explicitly approved.
