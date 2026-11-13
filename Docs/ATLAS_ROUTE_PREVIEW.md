# ATLAS full-field route preview — experimental fork

This branch preserves the tested AgOpenWeb 26.7.0 base commit
`ee162399d34c074d74b03ecb3ff1f8a1d65add60`. It is not a port to current develop.
Build identifier: `26.7.0+ee16239-atlas-route-v1`.

## Features

- Read-only, frozen full-field preview from the currently loaded boundary,
  reference, actual tool width/overlap, row skipping and headland configuration.
- Native CurveProcessing offsets, working passes clipped to a reserved headland,
  explicit offline skip-and-fill ordering, alternating direction and sampled
  Dubins headland connections.
- A separate exact-arc preview sampler in DubinsPathService. The existing live
  GeneratePath/YouTurn controller is unchanged and does not execute this plan.
- Disabled-by-default, bearer-authenticated loopback HTTP export under
  `/atlas-route/v1/`. One bounded request at a time; stationary/control-off checks;
  frozen source comparison before/after planning; no actuation or field writes.
- Ordered local ENU segments, source configuration, pass/turn counts and issues
  for downstream independent validation and WGS84 GeoJSON export in ATLAS.

ATLAS UI, signed delivery, independent validation and the reset/reload SDK are
separate projects. Reset/reload uses existing native commands; it is not part of
this native patch. No route-following GPS generator is added by this release.

## Build

Install .NET SDK 10 and, from the repository root:

```bash
dotnet publish Platforms/AgOpenWeb.Desktop/AgOpenWeb.Desktop.csproj \
  -c Release -r linux-arm64 --self-contained true \
  -p:DesktopOnly=true -p:AtlasRoutePreviewBuild=true \
  -o artifacts/agopenweb-linux-arm64
```

Use the preview build flag only for this exact compatibility branch. The build
identifier is explicit rather than pretending to be unmodified upstream.

The release archive is the previously tested self-contained Linux ARM64 publish,
with upstream license and installation notes. `SHA256SUMS` authenticates its
bytes against the release metadata; the tagged Git source is the corresponding
source. No credentials, device identities or runtime data are included.

## Run

Extract the full archive together, preserve the existing service user and
AGOPENWEB_DATA location, and start `AgOpenWeb.Desktop --headless`. Enable:

```text
ATC_AGOPEN_ROUTE_PREVIEW_ENABLED=true
ATC_AGOPEN_ROUTE_PREVIEW_TOKEN_FILE=/etc/agopenweb-route-preview/gateway.token
```

The token file must contain the same private bearer token used by the local
Jetson gateway, readable only by the necessary service account. Do not commit it.
The HTTP request path suffix is base64url JSON containing schemaVersion:1,
fieldName, trackName and options:{schemaVersion:1,envelopeRadiusM:<metres>}.
Authorization uses `Bearer <token>`. Direct remote browser export is refused;
ATLAS exchanges requests/results through its authenticated Jetson adapter.

## Validation and limitations

The full RemoteServer and Desktop projects built successfully, and ARM64
self-contained publication succeeded. A compiled synthetic native fixture and
protected HTTP export were tested with the SDK/gateway. The standalone geometry
smoke check is reproducible with:

```bash
dotnet run --project Tools/AtlasRoutePreviewCheck/AtlasRoutePreviewCheck.csproj
```

Its synthetic fixture produces 11 passes and 10 turn connections. This is bench
evidence, not physical tractor acceptance or a certification of route safety.
Existing upstream compiler warnings remain.

- Preview-only: no executor consumes the exported route. The live YouTurn
  controller may plan differently. A reviewed preview never enables driving.
- One outer boundary/no holes; bounded field size, reference complexity, number
  of passes, point count and planning duration. Split passes are unsupported.
- Forward Dubins preview connections only; K-style and worked-coverage modes
  block valid review. Radius, spacing, clearance and skip count affect this
  planner. Runtime extension/smoothing are captured, not applied to this solver.
- Manually supplied conservative collision disk; no calibrated articulated
  footprint, obstacles, terrain or steering dynamics. Turn space is reserved;
  no separate headland working passes, first-pass approach or final exit.
- ATLAS coverage is an approximate sampled working-interior estimate. Invalid
  geometry is flagged rather than labeled executable. Persistence is unverified.

AgOpenWeb is GPL-3.0-or-later; preserve LICENSE.md and existing notices. This fork
adds no relicensing restriction. See the native source and tagged history for
the exact changes.
