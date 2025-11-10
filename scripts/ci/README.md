# CI Monitoring Scripts

This directory contains scripts for monitoring GitHub Actions CI status for the Teams Media Bot project.

## Scripts

### `monitor-ci.sh`

Advanced CI monitoring tool with watch mode and artifact download capabilities.

**Requirements:**
- `gh` (GitHub CLI)
- `jq` (JSON processor)

**Usage:**
```bash
# One-time status check
./scripts/ci/monitor-ci.sh

# Watch mode (auto-refresh)
./scripts/ci/monitor-ci.sh --watch

# Custom refresh interval
./scripts/ci/monitor-ci.sh --watch --interval 60

# Monitor specific branch
./scripts/ci/monitor-ci.sh --branch main
```

**Features:**
- Real-time job status
- Color-coded output
- Auto-refresh in watch mode
- Artifact download on completion
- Detailed job information

## Quick Check Script

For a simpler, dependency-free status check, use:
```bash
../check-ci.sh [branch]
```

Located in the parent `scripts/` directory.

## Installation

### GitHub CLI
```bash
# Ubuntu/Debian
curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg | sudo dd of=/usr/share/keyrings/githubcli-archive-keyring.gpg
echo "deb [signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" | sudo tee /etc/apt/sources.list.d/github-cli.list
sudo apt update
sudo apt install gh

# macOS
brew install gh

# Authenticate
gh auth login
```

### jq
```bash
# Ubuntu/Debian
sudo apt install jq

# macOS
brew install jq
```

## See Also

- [CI_SETUP.md](../../Samples/V1.0Samples/LocalMediaSamples/PolicyRecordingBot/CI_SETUP.md) - Complete CI documentation
- [GitHub Actions Workflows](../../.github/workflows/) - Workflow definitions
