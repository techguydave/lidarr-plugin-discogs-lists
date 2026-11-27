# Contributing

## Rules

- **All network access is read-only, with no exceptions.** Requests go only through `Api/DiscogsTransport.cs` and `Api/MusicBrainzTransport.cs`, which can only send `GET` to their own host. `scripts/check-read-only.sh` enforces this.
- Follow the [MusicBrainz API rules](https://musicbrainz.org/doc/MusicBrainz_API/Rate_Limiting): at most one request per second and a User-Agent with contact details.
- Follow the [Discogs API documentation](https://www.discogs.com/developers) and [Terms of Use](https://support.discogs.com/hc/articles/360009334593-API-Terms-of-Use): authenticate every request, keep the unique User-Agent, respect the rate limit, and do not store Discogs data.

## Building

You need the .NET 8 SDK.

```bash
git submodule update --init --depth 1
```

```bash
scripts/test.sh
```

```bash
scripts/build.sh
```

The installable zip is written to `artifacts/`. To try it, extract it into `<Lidarr data folder>/plugins/techguydave/lidarr-plugin-discogs-lists/` and restart Lidarr.

`lidarr.version` is the oldest supported Lidarr. The `ext/Lidarr` submodule must be at the matching tag.

## Branches and pull requests

- Branch from `develop` using [Conventional Branch](https://conventional-branch.github.io) names, such as `feat/label-support` or `fix/pagination`.
- Open pull requests against `develop` only. Their titles must be [Conventional Commits](https://www.conventionalcommits.org), and they are squash merged, so the title decides the next version.
- Add user-visible changes to the `Unreleased` section of `CHANGELOG.md`.
- A pull request is merged once CI passes.

## Versions

Versions follow [Semantic Versioning](https://semver.org).

Every merge to `develop` publishes a pre-release, `X.Y.Z-develop.N`. Lidarr compares only `X.Y.Z` when it looks for pre-release updates, so each pre-release raises it: a breaking change raises the major version (the minor version before 1.0.0), `feat` raises the minor version, and anything else raises the patch version.

Inside Lidarr a pre-release shows as `X.Y.Z.0 (develop)`, without the `N`, to match how Lidarr names the available version.

A stable release takes the `X.Y.Z` of the pre-release with identical content, so stable version numbers can skip.

## Releasing

Releases are made only when the maintainer asks for one.

1. On a `chore/release-X.Y.Z` branch, move the `Unreleased` entries in `CHANGELOG.md` under `## [X.Y.Z] - YYYY-MM-DD`, where `X.Y.Z` is the latest pre-release's version with the patch number raised by one. Write the release notes there: they become the GitHub release text.
2. Open a pull request to `develop` titled `chore(release): prepare X.Y.Z`, and merge it once CI passes. Wait for its pre-release to be published.
3. Open a pull request from `develop` to `main` titled `chore(release): vX.Y.Z`.
4. Once CI passes, merge it with a **merge commit**. The release workflow tags `vX.Y.Z` and publishes the release.
