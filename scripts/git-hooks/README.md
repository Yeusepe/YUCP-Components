# Git Hooks

`pre-commit.ps1` updates `Website/CHANGELOG.md` before the commit.

`commit-msg.ps1` handles commit message generation and review gating. Use `Generate` as the commit title to replace it with a generated Conventional Commit message. Use `Generate no review` to generate the message and skip CodeRabbit.

For privacy, changelog generation sends only staged file names and summary stats by default. Set `PRECOMMIT_SEND_DIFF=1` to opt in to sending a redacted staged diff. The hook truncates that diff to 30000 characters and scrubs common token, secret, password, and local user path patterns before sending it.

Optional overrides:

- `CODERABBIT_CLI_PATH`: full path to `cr.exe` or `coderabbit.exe`.
- `AGY_CLI_PATH`: full path to `agy.exe`.
- `GEMINI_CLI_PATH`: full path to `gemini.exe` or `gemini.cmd`.
- `OPENCODE_CLI_PATH`: full path to `opencode.exe` or `opencode.cmd`.
- `OPENCODE_MODEL`: opencode model to use. Defaults to `opencode/deepseek-v4-flash-free`.
- `COMMITMSG_AI_PROVIDER=opencode`: try opencode before agy/Gemini for commit message generation.
- `CODERABBIT_TIMEOUT_SECONDS`: CodeRabbit review timeout in seconds. Defaults to 900.
