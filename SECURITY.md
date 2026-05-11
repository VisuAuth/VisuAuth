# Security Policy

## Reporting a vulnerability

**Do not open a public issue for security vulnerabilities.**

VisuAuth handles authentication and identity data, so security reports are high priority.

Please report vulnerabilities by opening a [GitHub Security Advisory](https://github.com/VisuAuth/visuauth/security/advisories/new) on this repository, or by email to the maintainer.

Include:
- A clear description of the issue
- Steps to reproduce
- Affected version(s)
- Suggested fix (if you have one)

We aim to acknowledge reports within **72 hours** and to publish a fix or mitigation within **14 days** for high-severity issues.

## Supported versions

VisuAuth is pre-alpha. Until v1.0, only the latest minor version is supported for security fixes.

## Scope

In scope:
- Authentication bypass
- Token forgery or replay
- Privilege escalation
- Data exposure across tenants
- Injection (SQL, LDAP, command, etc.)
- Cross-site scripting in shipped UI

Out of scope:
- Issues that require the attacker to already control the host application
- Misconfiguration by the consumer (we'll happily harden defaults, but a missing `[Authorize]` on the consumer side isn't a vuln in VisuAuth)
- Findings from automated scanners without a working PoC
