# osu-tools-api

An API that wraps [osu-tools](https://github.com/ppy/osu-tools) via gRPC. Downloads beatmap `.osu` files and delegates difficulty and performance (PP) calculations to a C# service using the `ppy.osu.Game` NuGet packages.

This keeps PP and difficulty calculations in sync with the latest osu! changes without depending on other third-party calculation engines like rosu-pp.

In theory we just have to update the NuGet packages and redeploy, but in practice this WILL break considering the tests are comparing against fixed calculated values. Fixing the tests and redeploying should be easy if the underlying calculations don't break the wrapper.

The C# service is spawned as a child process and communicates with the Node.js API over a Unix domain socket.

**Linux only.** The C# service is built for `linux-x64` and uses Unix domain sockets, so this does not run on Windows or macOS.

## Prerequisites

- Node.js 22+
- .NET 10 SDK
- npm

## Quick start

```bash
# Install dependencies
npm install

# Build the C# service, generate proto types, and compile TypeScript
npm run build

# Create config from the example
cp config-example.json config.json
# Edit your config.json

# Start the API
npm start
```

For development with hot reload:

```bash
npm run proto:gen   # generate gRPC types (first time only)
npm run dev
```

## Configuration

Configuration is loaded from `config.json` at the project root. Copy `config-example.json` and edit it.

| Field                      | Type                              | Default                      | Description                                                      |
| -------------------------- | --------------------------------- | ---------------------------- | ---------------------------------------------------------------- |
| `auth.api_keys`            | `string[]`                        | `[]`                         | API keys for authenticating requests. Empty array disables auth. |
| `general.mode`             | `"development"` \| `"production"` | `"development"`              | Currently just adds a "dev\_" prefix in Prometheus metrics.      |
| `general.public_api_port`  | `number`                          | `3000`                       | Port for the public API.                                         |
| `general.private_api_port` | `number`                          | `3001`                       | Port for the private API (Prometheus metrics).                   |
| `general.api_prefix`       | `string`                          | `"/api"`                     | URL prefix for API routes.                                       |
| `general.grpc_socket_path` | `string`                          | `"/tmp/osu-tools-calc.sock"` | Unix socket path for gRPC communication.                         |
| `general.reqs_per_min`     | `number`                          | `60`                         | Rate limit per minute per IP.                                    |
| `cache.path`               | `string`                          | `"./cache"`                  | Directory for cached beatmap files.                              |
| `cache.ttl_days`           | `number`                          | `1`                          | Cache TTL in days. Set to `0` to disable caching entirely.       |
| `urls.webhook`             | `string`                          | `""`                         | Webhook URL for error/fatal notifications. Empty disables.       |
| `urls.webhook_token`       | `string`                          | `""`                         | Authorization header value for webhook. Empty sends no header.   |
| `urls.loki`                | `string`                          | `""`                         | Grafana Loki URL for log shipping. Empty disables.               |

## API

All API routes require an `Authorization: Bearer <key>` header with a valid API key, unless `auth.api_keys` is empty (auth disabled). Requests are rate-limited to `reqs_per_min` per minute (default 60).

This API shouldn't be ran publically because it can be abused quite easily. The calculations are relatively heavy and allowing everyone to request them would be bad for the osu! download server or mirror and your own server. If you STILL want to run this API in public, you can keep the config.auth.api_keys empty.

### POST /api/difficulty

Calculates difficulty attributes for a beatmap.

**Request body:**

| Field       | Type     | Default      | Description                                                                 |
| ----------- | -------- | ------------ | --------------------------------------------------------------------------- |
| `beatmapId` | `number` | _(required)_ | osu! beatmap ID.                                                            |
| `rulesetId` | `number` | `-1`         | Ruleset: 0=osu, 1=taiko, 2=catch, 3=mania. `-1` = auto-detect from beatmap. |
| `mods`      | `Mod[]`  | `[]`         | Mods with optional settings. See below.                                     |

**`Mod` object:**

| Field      | Type                     | Default | Description                                            |
| ---------- | ------------------------ | ------- | ------------------------------------------------------ |
| `acronym`  | `string`                 | —       | Mod acronym, e.g. `"DT"`, `"HD"`.                      |
| `settings` | `Record<string, string>` | `{}`    | Optional mod settings, e.g. `{"speed_change": "1.1"}`. |

**Example:**

```json
{
    "beatmapId": 75,
    "mods": [{ "acronym": "DT", "settings": { "speed_change": "1.1" } }, { "acronym": "HD" }]
}
```

**Response (200):**

```json
{
    "starRating": 5.25,
    "approachRate": 9,
    "overallDifficulty": 8,
    "circleSize": 4,
    "drainRate": 5,
    "maxCombo": 500,
    "bpm": 180,
    "length": 120,
    "drainLength": 100,
    "attributes": { ... }
}
```

### POST /api/performance

Calculates performance (PP) for a score on a beatmap.

**Request body:**

| Field                     | Type      | Default      | Description                                                                                                    |
| ------------------------- | --------- | ------------ | -------------------------------------------------------------------------------------------------------------- |
| `beatmapId`               | `number`  | _(required)_ | osu! beatmap ID.                                                                                               |
| `rulesetId`               | `number`  | `-1`         | Ruleset. `-1` = auto-detect.                                                                                   |
| `mods`                    | `Mod[]`   | `[]`         | Mods with optional settings. Same format as difficulty endpoint.                                               |
| `accuracy`                | `number`  | `100`        | Target accuracy (0-100).                                                                                       |
| `misses`                  | `number`  | `0`          | Number of misses.                                                                                              |
| `mehs`                    | `number?` | —            | Number of 50s (osu!).                                                                                          |
| `goods`                   | `number?` | —            | Number of 100s (osu!) or droplets (catch).                                                                     |
| `oks`                     | `number?` | —            | Number of 200s (mania).                                                                                        |
| `greats`                  | `number?` | —            | Number of 300s (mania).                                                                                        |
| `combo`                   | `number?` | —            | Max combo achieved. Falls back to `percentCombo`.                                                              |
| `percentCombo`            | `number`  | `100`        | Percentage of max combo (0-100). Used if `combo` is not set.                                                   |
| `largeTickMisses`         | `number`  | `0`          | Large tick misses (osu!).                                                                                      |
| `sliderTailMisses`        | `number`  | `0`          | Slider tail misses (osu!).                                                                                     |
| `tinyDroplets`            | `number?` | —            | Tiny droplet count (catch).                                                                                    |
| `droplets`                | `number?` | —            | Droplet count (catch).                                                                                         |
| `legacyTotalScore`        | `number?` | —            | Legacy total score (for score-based PP).                                                                       |
| `precalculatedDifficulty` | `string?` | —            | JSON-serialized difficulty attributes from a prior `/difficulty` call. Skips redundant difficulty calculation. |

**Response (200):**

```json
{
    "performance": 456.78,
    "difficulty": {
        "starRating": 5.25,
        "approachRate": 9,
        "overallDifficulty": 8,
        "circleSize": 4,
        "drainRate": 5,
        "maxCombo": 500,
        "bpm": 180,
        "length": 120,
        "drainLength": 100,
        "attributes": { ... }
    },
    "score": {
        "rulesetId": 0,
        "beatmapId": 75,
        "beatmapName": "Artist - Title [Difficulty]",
        "accuracy": 99.5,
        "combo": 500,
        "statistics": { "great": 490, "ok": 5, "meh": 3, "miss": 2 }
    },
    "performanceAttributes": {
        "total": 456.78,
        "aim": 200,
        "speed": 150,
        "accuracyPp": 50,
        "flashlight": 0,
        "effectiveMissCount": 2,
        "extra": {}
    }
}
```

### GET /metrics (on private API)

Prometheus-format metrics endpoint. No authentication required. Exposed on `private_api_port` (default 3001).

## Beatmap sources

Beatmap files are downloaded from two sources in order:

1. **osu.ppy.sh**: `https://osu.ppy.sh/osu/{id}`
2. **Mino**: `https://catboy.best/osu/{id}` (fallback)

If the first source fails (non-200, invalid content, or network error), the second is tried automatically.

## Caching

Beatmap files are cached in two layers:

- **Memory cache**: 1 hour TTL, max 1000 entries (NodeCache)
- **Disk cache**: configurable TTL via `cache.ttl_days`

Set `cache.ttl_days` to `0` to disable disk caching. Memory caching remains active.

## Testing

```bash
# TypeScript tests (Jest)
npm test

# C# tests (xUnit)
npm run test:cs
```

## License

BSD-3-Clause
