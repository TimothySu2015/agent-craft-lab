# Security Policy

## Reporting a Vulnerability

We take security seriously. If you discover a security vulnerability in AgentCraftLab, please report it responsibly.

### How to Report

**Preferred method**: Use [GitHub Private Vulnerability Reporting](../../security/advisories/new)

This ensures your report is only visible to maintainers. Do **not** open a public issue for security vulnerabilities.

### What to Include

- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if any)

### Response Timeline

| Stage | Timeframe |
|-------|-----------|
| Acknowledgment | Within **72 hours** |
| Initial assessment | Within **14 days** |
| Fix & disclosure | Coordinated with reporter |

### Scope

The following are in scope:

- Authentication / authorization bypass
- SQL injection, XSS, CSRF
- Credential exposure (API keys, secrets)
- Remote code execution
- Privilege escalation
- PII data leakage

The following are **out of scope**:

- Vulnerabilities in third-party dependencies (report to the upstream project)
- Issues requiring physical access
- Social engineering

## Security Features

AgentCraftLab includes built-in security mechanisms:

- **Credential encryption** — API keys stored with DPAPI encryption (`ICredentialStore`)
- **PII detection & masking** — 35 regex rules across 6 locales with checksum validation
- **GuardRails** — Prompt injection detection, content filtering, topic restriction
- **No frontend secrets** — Credentials never transmitted to the browser

## Supported Versions

| Version | Supported |
|---------|-----------|
| Latest  | Yes       |
