# Signal Beacon

![CodeQL](https://github.com/dfnoise/beacon/workflows/CodeQL/badge.svg)

## Development

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

```
dotnet publish -r win-x64 --self-contained true
```

_[.NET Core RID Catalog](https://docs.microsoft.com/en-us/dotnet/core/rid-catalog) for more options for `-r` flag._

## Services

- API
- Processor
- Zigbee2Mqtt
- Voice
- PhilipsHue
- Processor

### Voice

#### Wake word - Porcupine

Porcupine custom trained model for wake word `"signal"` expires every 30 days. You need to retrail one ourself or use provided model if not expired. More info on training your custom wake work model can be found on [GitHub: Picovoice Porcupine repo](https://github.com/Picovoice/porcupine/).

Wake word aucustic model is located in:

- `Profiles/signal_windows_2020-12-33_v1.8.0.ppn`

Model is automatically selected based on date and version. Version must match with installed Procupine NuGet package version.

#### DeepSpeech

To enable DeepSpeech support model and scorer needs to be added:

- `Profiles/deepspeech-x.x.x-models.pbmm`
- `Profiles/deepspeech-x.x.x-models.scorer`

Profiles can be downloaded from [Github: Mozilla DeepSpeech repo](https://github.com/mozilla/DeepSpeech) Releases page.
