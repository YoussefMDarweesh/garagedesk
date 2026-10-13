# AGENTS.md

All rules for this repo live in `CLAUDE.md`. Read it and `SPEC.md` before doing anything.

## Your role (Codex)
You are the **reviewer**. Claude Code does most of the building.

When asked to review a change or pull request, check in this order and report findings as a short list:
1. **Tenant leaks:** any query that could return another shop's data.
2. **Security:** secrets in code, missing `[Authorize]`, unvalidated input.
3. **Bugs:** wrong totals, null handling, broken status flow.
4. **Spec mismatch:** does it do what `SPEC.md` says for that milestone?
5. **Simplicity:** anything over-engineered for an MVP.

Don't rewrite working code for style alone.
