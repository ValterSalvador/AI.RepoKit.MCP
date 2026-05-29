---
description: Test and verification conventions for AI-assisted changes.
target: vscode
---

# Test Instructions

Start with MCP context to find relevant production and test symbols. Use existing test frameworks, naming patterns, fixtures, and assertion style. Add or update focused tests for changed behavior when practical.

Prefer deterministic tests that do not require network, databases, Docker, app servers, or local secrets. When fixing failures, identify whether production code or the test expectation is wrong before editing.
