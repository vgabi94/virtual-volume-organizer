# Build and release

## Versioning

`Directory.Build.props` at the repository root holds `<Version>`, and is the only place it is
written. MSBuild imports it before each project body, so every assembly inherits it and a project
that needs another value can still set its own — `VVO.UiTests` does, pinning `1.4.2` and a fixed
`SourceRevisionId` so the About box has a known version to be asserted against.

The .NET SDK appends the git commit to the informational version, giving `1.0.0+<sha>`.
`AboutDialogViewModel` trims at the `+`, so the About box shows `1.0.0` while the executable's file
properties still identify the commit it was built from.

## Continuous integration

`.github/workflows/ci.yml` runs on every push to any branch and on pull requests into `main`:
restore, build, and `dotnet test VVO.slnx`, on `windows-latest`. The headless UI tests need no
display and run in the same job.

A push to `main` additionally publishes the `win-x64` executable and uploads it as a workflow
artifact, kept 14 days.

## Cutting a release

Bump the version, commit it, and push a matching tag:

```
git commit -am "Version 1.0.1" && git tag v1.0.1 && git push origin main v1.0.1
```

`.github/workflows/release.yml` triggers on `v*.*.*`. It first compares the tag against
`Directory.Build.props` and fails if they disagree — the failure that guard exists for is a tag
pushed without the bump committed, which would attach an executable to a release it does not name.
It then tests, publishes `win-x64`, and attaches
`VirtualVolumeOrganizer-<version>-win-x64.exe` to a **draft** GitHub release, so the generated
notes can be read before anything is published. Remove `--draft` from the last step to publish
directly.

The release is created with `gh release create`, which is preinstalled on the runner and uses the
built-in `GITHUB_TOKEN`; the workflow needs `permissions: contents: write` and no third-party
action. Only `win-x64` is released, because that is the only runtime the tests run on. Adding
another means adding a publish and an upload for its runtime identifier.

## Publish flags

The publish command is in the [README](../README.md#build-and-publish). Three of its flags are not
optional in practice:

- `IncludeNativeLibrariesForSelfExtract` — without it the native Skia and HarfBuzz libraries stay
  beside the executable and there is no single file.
- `PublishTrimmed` must stay off. `ViewLocator` resolves views by reflection on view-model type
  names and the trimmer removes them.
- `DebugType=none` — `VVO.UI.csproj` also drops `.pdb` files from the publish output in the
  `DropNativeSymbolsFromPublish` target, because SkiaSharp and HarfBuzz ship around 100 MB of
  native symbols in their packages.
