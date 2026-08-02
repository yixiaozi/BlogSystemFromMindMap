#!/usr/bin/env bash
# Install daily rebuild at 03:30 Asia/Shanghai-ish (server local time)
set -euo pipefail
CRON_LINE="30 3 * * * /root/BlogSystemFromMindMap/scripts/auto_rebuild.sh >> /var/log/blog-auto-rebuild.log 2>&1"
(crontab -l 2>/dev/null | grep -v auto_rebuild.sh; echo "$CRON_LINE") | crontab -
echo "Installed cron:"
crontab -l | grep auto_rebuild
