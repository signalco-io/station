<p align="center">
  <a href="#">
    <img height="128" width="455" alt="signalco" src="https://raw.githubusercontent.com/signalco-io/station/main/docs/images/logo-ghtheme-128x455.png">
  </a>
</p>
<h4 align="center">Automate your life.</h4>

<p align="center">
  <a href="#getting-started">Getting Started</a> •
  <a href="#development-for-station">Development for Station</a>
</p>

## Getting Started

Visit <a aria-label="Signalco learn" href="https://www.signalco.io/learn">https://www.signalco.io/learn</a> to get started with Signalco.

## Development for Station

Deployments

[![Create new release](https://github.com/signalco-io/station/actions/workflows/create-release.yml/badge.svg)](https://github.com/signalco-io/station/actions/workflows/create-release.yml)

[![Publish Binaries](https://github.com/signalco-io/station/actions/workflows/release-binaries.yml/badge.svg)](https://github.com/signalco-io/station/actions/workflows/release-binaries.yml)

Code

[![CodeQL](https://github.com/signalco-io/station/actions/workflows/codeql-analysis.yml/badge.svg)](https://github.com/signalco-io/station/actions/workflows/codeql-analysis.yml)
[![Maintainability](https://api.codeclimate.com/v1/badges/61b2b1f79a40220b7054/maintainability)](https://codeclimate.com/github/signalco-io/station/maintainability)
[![CodeFactor](https://www.codefactor.io/repository/github/signalco-io/station/badge)](https://www.codefactor.io/repository/github/signalco-io/station)

### Commits

Commit messages need to contain semantic-release anotations in order to propperly trigger release.

The table below shows which commit message gets you which release type when `semantic-release` runs (using the default configuration):

| Commit message | Release type               |
| -------------- | -------------------------- |
| `fix(Docs): Fixed a typo in documentation` | Patch Release |
| `feat(Docs): Added new documentation file` | Feature Release |
| `perf(Docs): Removed documentation for feature`<br><br>`BREAKING CHANGE: The documentation for this feature was removed.`<br>`This feature is no longer supported.` | Breaking Release |

For more info see [semantic-release](https://semantic-release.gitbook.io/semantic-release/).

### Publishing

Example publish command for Windows x64 target:

```bash
dotnet publish -r win-x64 --self-contained true
```

Example publish command for ARM64 (eg. >= Rpi3) target:

```bash
dotnet publish -r linux-arm64 --self-contained true
```

_[.NET Core RID Catalog](https://docs.microsoft.com/en-us/dotnet/core/rid-catalog) for more options for `-r` flag._

## Services

**Internal**

- Processor
- Voice
- ConductHandler

**Channels**

- BroadLink
- Divoom
- iRobot
- PhilipsHue
- Processor
- Signal
- Samsung
- Tasmota
- Zigbee2Mqtt

### Voice

#### Wake word - Porcupine

Wake word aucustic model is located in:

- `resourceskeyword_files/<PLATFORM>/computer.ppn`
