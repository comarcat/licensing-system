# Agents

This repo uses Claude Code task execution against `blueprints/licensing-system-v2-admin-hardening/tasks.json`.

Rules:
- Follow the first ready pending task.
- Do not skip verification.
- Do not broaden scope beyond the task's files unless the verify command cannot pass without it; document any extra file in the status log.
