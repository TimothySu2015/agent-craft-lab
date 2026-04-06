# Contributing to AgentCraftLab

Thank you for your interest in contributing to AgentCraftLab! This document provides guidelines and instructions for contributing.

> **繁體中文摘要**：歡迎貢獻！請先開 Issue 討論，然後 Fork → 建立分支 → 提交 PR。需要 1 位維護者 approve + CI 通過才能合併。Commit message 英文或繁中皆可。程式碼須遵循 .editorconfig 規範且零警告。新的 public API 需要附帶單元測試。

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code.

## How to Contribute

### Reporting Bugs

1. Check [existing issues](../../issues) to avoid duplicates
2. Use the **Bug Report** issue template
3. Include: steps to reproduce, expected vs actual behavior, environment details

### Suggesting Features

1. Open an issue using the **Feature Request** template
2. Describe the problem and proposed solution
3. Wait for maintainer feedback before starting implementation

### Submitting Pull Requests

#### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 20+](https://nodejs.org/)
- An LLM API key for integration testing (optional)

#### Development Setup

```bash
git clone https://github.com/<your-fork>/agent-craft-lab.git
cd agent-craft-lab/AgentCraftLab.Web
npm install
npm run dev:all
```

This starts three services:
- .NET API on `http://localhost:5200`
- CopilotKit Runtime on `http://localhost:4000`
- React Dev Server on `http://localhost:5173`

#### Workflow

1. **Open an issue first** to discuss the change
2. **Fork** the repository
3. **Create a branch** from `main`:
   - `feat/description` for new features
   - `fix/description` for bug fixes
   - `docs/description` for documentation
4. **Make your changes**
5. **Run tests**:
   ```bash
   dotnet build        # Must produce 0 warnings
   dotnet test         # All tests must pass
   cd AgentCraftLab.Web && npx vitest run  # Frontend tests
   ```
6. **Submit a PR** targeting `main`

#### PR Requirements

- Linked to an issue (when applicable)
- 1 maintainer approval required
- CI checks must pass (build + test)
- New public APIs must include unit tests
- No increase in warnings (`TreatWarningsAsErrors` is enabled)

## Code Standards

### C# (.NET)

- **`.editorconfig` enforced** — file-scoped namespaces, required braces, using order
- **`TreatWarningsAsErrors`** — zero warnings policy across all projects
- **No Semantic Kernel** — use `Microsoft.Agents.AI` APIs exclusively
- Follow existing patterns in the codebase

### TypeScript / React

- Follow existing component patterns
- Use `shadcn/ui` components
- Translations via `i18next` (en + zh-TW)

### Commit Messages

Both English and Traditional Chinese (繁體中文) are accepted:

```
feat: add webhook support for workflow hooks
fix: 修正 SkillForm tools.join 錯誤
docs: update API reference for Search endpoints
```

Use [Conventional Commits](https://www.conventionalcommits.org/) prefixes: `feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`.

## Architecture Overview

See [docs/en/developer-guide/architecture.md](docs/en/developer-guide/architecture.md) for detailed architecture documentation.

## Questions?

- Open a [Discussion](../../discussions) for general questions
- Check the [documentation](docs/) for guides and references

## License

By contributing, you agree that your contributions will be licensed under the [Apache License 2.0](LICENSE).
