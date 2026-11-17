# AgOpenWeb Security Threat Model

**Status:** Initial pass for team review. Not a formal certification artifact.
**Method:** STRIDE against each untrusted-input boundary. Focused on what an attacker can actually reach given how the app is deployed.
**Scope reviewed:** UDP receive, NMEA parsing, NTRIP client, field/config file I/O, NuGet supply chain. Translations were reviewed separately (see `TRANSLATION_WORKFLOW.md`-era discussion).

## Deployment context

- AgOpenWeb runs on a tablet in a tractor cab. Single-user, no remote-access path.
- Tablet is usually **on the internet** (for NTRIP/RTCM). LAN hosts the ag hardware modules (AutoSteer, Machine, IMU on port 8888/9999).
- UDP modules are broadcast-based with no authentication. "Trust on first response" is the current posture.
- Safety-critical output: steering commands to AutoSteer module. Wrong values can cause real-world damage or injury.
- App source is open; contributions come via PRs (reviewed) and, eventually, Weblate (translations only, no code).

## Trust boundaries

| Zone | Trust level | Enters via |
|---|---|---|
| Internet | Untrusted | NTRIP TCP socket, DNS |
| Farm LAN / cab WiFi | Semi-trusted (assumed but not enforced) | UDP port 9999 |
| Local filesystem | Semi-trusted (user-controlled but file-shared among farms) | Field files, config JSON, vehicle profiles |
| UDP modules (AutoSteer/Machine/IMU) | Trusted by convention | After first HELLO locks subnet |
| GPS hardware | Trusted | NMEA stream via UDP or serial |

## Findings summary

| ID | Title | Severity | Status |
|---|---|---|---|
| [F1](#f1) | NTRIP credentials sent in cleartext over TCP | **High** | Needs design decision |
| [F2](#f2) | NTRIP header buffer is unbounded (memory-exhaustion DoS) | **High** | Needs fix |
| [F3](#f3) | `Tmds.DBus.Protocol 0.90.3` transitive dep has published High-severity CVE | **High** | Tracking |
| [F4](#f4) | UDP inbound PGNs include CRC in protocol but we don't verify it | **Medium** | Needs fix |
| [F5](#f5) | Any LAN peer can spoof a module (no auth on UDP) | **Medium** | Design accepted risk |
| [F6](#f6) | `ForwardRtcmData` sends caster bytes to GPS hardware without parsing | **Medium** | Design accepted risk |
| [F7](#f7) | Field-directory path joined from user-visible name without traversal guard | **Low** | Defense-in-depth |
| [F8](#f8) | NTRIP password stored plaintext in `AppSettings.json` | **Low** | Design-acceptable |
| [F9](#f9) | DNS resolution uses `addresses[0]` only, no fallback | **Low** | Usability nit |

---

## Entry-point STRIDE analysis

### 1. UDP receive (port 9999)

Attacker model: host on the same LAN subnet as the tablet. Realistic in a compromised farm WiFi scenario, less so on wired Ethernet inside a cab.

| STRIDE | Threat | Mitigation today | Finding |
|---|---|---|---|
| **S**poofing | Malicious host impersonates AutoSteer/Machine/IMU by sending HELLO + data PGNs | `LockToSubnet()` fixes the broadcast target to the first responder's /24, but any host on that subnet is still accepted | [F5](#f5) |
| **T**ampering | Attacker injects SENSOR_DATA with extreme values, corrupting guidance inputs | CRC is present in the AgOpen PGN spec and in our outbound frames, but inbound packets are dispatched without verifying the CRC byte against a recomputation | [F4](#f4) |
| **R**epudiation | Spoofed packets leave no trail beyond Debug.WriteLine | N/A for ag use case | — |
| **I**nfo disclosure | Module state (heading, position) readable by any LAN peer via broadcast | N/A — broadcast is how the protocol works | Out of scope |
| **D**enial of service | Flood port 9999 with bad packets | Parser bails on `data.Length < 6`; receive is async and starts next `BeginReceiveFrom` immediately | Acceptable |
| **E**levation | No privilege boundaries inside a single-user cab app | — | Out of scope |

### 2. NMEA parsing (`NmeaParserServiceFast`)

Attacker model: whoever can put bytes onto the NMEA stream. In practice GPS hardware, but over UDP path a LAN peer could.

Good defensive coding throughout — length checks, XOR checksum validation, sentence-type whitelist (`PANDA`/`PAOGI` only), comma-position stack-allocated bounded to 20, `TryParse` with `InvariantCulture`. No findings.

### 3. NTRIP client (`NtripClientService`)

Attacker model: network path between tablet and NTRIP caster. ISP, public WiFi, compromised farm router, rogue caster, DNS hijack. **Internet-exposed** — highest-privilege attacker in the model.

| STRIDE | Threat | Mitigation today | Finding |
|---|---|---|---|
| **S**poofing | Attacker impersonates caster (no TLS, no cert check) | None | [F1](#f1) |
| **T**ampering | RTCM corrections corrupted/replaced mid-stream | None; bytes forwarded unvalidated to GPS module | [F6](#f6) |
| **R**epudiation | N/A | — | — |
| **I**nfo disclosure | Basic Auth credentials readable on the wire (cleartext) | None — plain HTTP on TCP 2101 is the standard NTRIP profile | [F1](#f1) |
| **D**enial of service | Malicious caster never terminates HTTP header → `List<byte> _headerBuffer` grows unbounded | None; no max-header-size | [F2](#f2) |
| **E**levation | — | — | — |

### 4. File I/O (field files, AppSettings, vehicle profiles)

Attacker model: someone who can place a file in the app's data directory — typically the operator themselves or a field-sharing partner sending a KML/IsoXML.

| STRIDE | Threat | Mitigation today | Finding |
|---|---|---|---|
| **S**poofing | N/A | — | — |
| **T**ampering | Crafted `Field.txt`, `Boundary.txt`, or KML causes crash via malformed numbers | `TryParse` with bounds checks (lat ∈ [-90,90], lon ∈ [-180,180]) in `FieldPlaneFileService` | Low |
| **T**ampering | Serialized-object attack via `System.Text.Json` | `JsonSerializer.Deserialize<AppSettings>` — no `TypeInfoResolver` custom rules, no polymorphism. Safe by default. | Accepted |
| **R**epudiation | — | — | — |
| **I**nfo disclosure | Bug-report dump (screenshot + state) may include sensitive coords | User-triggered action | Low, documented |
| **D**enial of service | Huge file exhausts memory on load | No file-size cap | Low |
| **E**levation | Field name `../../etc/passwd` → read/write outside fields root | `Path.Combine` offers no traversal protection | [F7](#f7) |

### 5. NuGet supply chain

Attacker model: compromised upstream package, typosquat, transitive dep with published CVE.

| Package | Severity | Source | Finding |
|---|---|---|---|
| `Tmds.DBus.Protocol 0.90.3` | High ([GHSA-xrw6-gwf8-vvr9](https://github.com/advisories/GHSA-xrw6-gwf8-vvr9)) | Transitive from Avalonia's Linux platform binding | [F3](#f3) |

---

## Detailed findings

### <a name="f1"></a>F1 — NTRIP credentials in cleartext over TCP

**Severity:** High

The NTRIP client uses plain TCP on port 2101 with HTTP/1.1 Basic Authentication. Credentials (`_config.Username`/`_config.Password`) are concatenated, Base64-encoded, and sent in the `Authorization:` header in the first request. Anyone on the network path — ISP, hotel/public WiFi, compromised farm router, local DHCP attacker — can recover the plaintext credentials by Base64-decoding the intercepted header.

```csharp
// NtripClientService.SendNtripRequestAsync (line 189)
var credentials = Convert.ToBase64String(
    Encoding.ASCII.GetBytes($"{_config.Username}:{_config.Password}"));
request.Append($"Authorization: Basic {credentials}\r\n");
```

This matches the legacy NTRIP 1.0 profile (which AgOpenGPS also uses), but the deployment context has shifted: tablets are now routinely on untrusted internet rather than a closed farm LAN only.

**Why:** NTRIP 1.0 was designed before TLS was routine. NTRIP 2.0 supports TLS but many casters still don't. The AgOpen codebase this was ported from made the same choice.

**Recommendation:**
1. Short term: document in the NTRIP config dialog that credentials travel in cleartext over plain HTTP.
2. Medium term: add NTRIP-over-TLS (port 443 or 2102) as an opt-in config, attempting the upgrade first and falling back to plain if the caster doesn't support it. Keep it optional because many amateur casters remain plain-HTTP.

### <a name="f2"></a>F2 — NTRIP header buffer is unbounded

**Severity:** High

`NtripClientService.ReceiveLoop` accumulates response bytes into `List<byte> _headerBuffer` until it finds `\r\n\r\n`. A malicious caster (or MITM proxy) can stream bytes indefinitely without emitting the header terminator, causing the app's heap to grow without bound.

```csharp
// NtripClientService.ReceiveLoop (line 226)
if (!headerReceived)
{
    for (int i = 0; i < bytesReceived; i++)
    {
        _headerBuffer.Add(_receiveBuffer[i]);
    }
    // ... scans for \r\n or \r\n\r\n, but never bails on size
}
```

A few hundred MB of header would exhaust a tablet in seconds to minutes depending on throttle; the app crashes or becomes unresponsive.

**Recommendation:** cap `_headerBuffer` at a sane limit (8 KiB is well over what any legitimate caster sends) and close the connection with a warning if exceeded.

```csharp
if (_headerBuffer.Count + bytesReceived > MaxHeaderBytes)
{
    _logger.LogWarning("NTRIP header exceeded {Max} bytes, disconnecting", MaxHeaderBytes);
    await DisconnectAsync();
    return;
}
```

### <a name="f3"></a>F3 — `Tmds.DBus.Protocol 0.90.3` has published high-severity CVE

**Severity:** High (but reachable only on Linux Desktop)

Every project in the solution warns on build:
```
NU1903: Package 'Tmds.DBus.Protocol' 0.90.3 has a known high severity vulnerability,
https://github.com/advisories/GHSA-xrw6-gwf8-vvr9
```

The package is a transitive dependency of Avalonia's Linux platform binding. It doesn't affect Windows / macOS / iOS / Android builds — only Linux desktop targets that actually execute D-Bus code.

**Recommendation:**
1. Check if Avalonia has released a newer version that pulls in a fixed `Tmds.DBus.Protocol`. If yes, bump.
2. If not, add an explicit `<PackageReference Include="Tmds.DBus.Protocol" Version="<fixed>" />` override at the Desktop.csproj level to force the newer version.
3. CI already surfaces this as a warning; consider escalating to an error once there's a known fix path.

### <a name="f4"></a>F4 — UDP inbound PGN CRC is received but not verified

**Severity:** Medium

Per the AgOpen PGN spec (`Docs/PGN.md`), every PGN frame has a uniform layout:

```
Byte 0: 0x80   (preamble)
Byte 1: 0x81   (preamble)
Byte 2: Src
Byte 3: PGN
Byte 4: Len    (data payload length only)
Bytes 5..(5+Len-1): Data
Byte (5+Len): CRC = sum of bytes 2 through (4+Len)
```

Our `PgnBuilder` computes and writes it correctly on every outbound frame — every `CalculateCrc(buf, …)` call in the codebase is in the outbound builder. The inbound path at `UdpCommunicationService.ProcessReceivedData:309-343` does not verify it:

```csharp
if (data.Length >= 2 && data[0] == PgnMessage.HEADER1 && data[1] == PgnMessage.HEADER2)
{
    if (data.Length < 6) return;
    byte pgn = data[3];
    UpdateModuleConnection(pgn, remoteEndPoint);
    DataReceived?.Invoke(this, new UdpDataReceivedEventArgs { ... });
    // no CalculateCrc, no comparison against data[5 + data[4]]
}
```

So the CRC is transmitted but effectively decorative on receive. Any LAN peer sending `0x80 0x81 …anything… 0x00` for the CRC byte will be accepted — an attacker does not need to compute a valid CRC to get the app to process the packet. More mundanely, silent data corruption from a faulty cable or radio link also passes through undetected.

**Why this is a fix and not accepted risk:** CRC verification on ingest is cheap (a few XOR-additions per packet), reuses `PgnBuilder.CalculateCrc`, and catches both adversarial spoofing attempts that don't bother to compute a CRC *and* non-adversarial bit-flips. A motivated attacker can still compute a correct CRC, but raising the effort bar is worth the small cost.

**Recommendation:** add CRC verification to `ProcessReceivedData`:

```csharp
int len = data[4];                          // payload length from PGN header
int crcIndex = 5 + len;                     // CRC sits right after the payload
if (data.Length < crcIndex + 1) return;     // truncated — drop
byte expected = PgnBuilder.CalculateCrc(data, 2, len + 3);
if (data[crcIndex] != expected) return;     // CRC mismatch — drop
// ... proceed with existing dispatch
```

`PgnBuilder.CalculateCrc` is currently private; promote it to `internal static` (or extract to a shared helper) so `UdpCommunicationService` can call it. The byte offsets are the same for every PGN — the `buf[13]` / `buf[10]` / `buf[29]` variation in `PgnBuilder` is just `5 + Len` for different `Len` values (8, 5, 24 respectively), not multiple layouts.

### <a name="f5"></a>F5 — Any LAN peer can spoof a module

**Severity:** Medium — **safety-adjacent**

There is no authentication between AgOpenWeb and the UDP modules. Once `LockToSubnet` fires on the first HELLO response, the subnet is fixed — but any host within that /24 is still accepted as valid source for subsequent packets. An attacker on the farm WiFi can:

1. Race-win the initial HELLO (get `LockToSubnet` pointed at their subnet) — easy if they boot up before the legit hardware.
2. Impersonate SENSOR_DATA or AUTOSTEER_DATA packets after lock, injecting false steering-angle readings into the guidance loop.

Threat scenario: compromised device on a shared farm WiFi → fake sensor data → guidance computes wrong corrective steering → tractor steers into a ditch/fence/boundary. The tractor's physical steering actuators won't apply AgOpenWeb's output blindly — the operator can disengage at any time — but depending on disengage latency and attack timing, real damage is possible.

**Why accepted as medium and not high:** requires attacker on the same LAN (not internet), and operator-in-the-loop is a meaningful mitigation. But it's the worst safety-implication path we have.

**Recommendation:**
1. Document that AgOpenWeb requires a trusted LAN (no IoT, no guest WiFi) — add to user-facing docs and README.
2. Longer term: consider a shared-secret HMAC per deployment. Operator configures a small secret once (setup wizard); modules and app both include it in a trailing byte range. Doesn't need to be crypto-strong — just needs to prevent a casual LAN peer from walking in. This would require firmware changes on the module side (Teensy), so a coordinated change across projects.

### <a name="f6"></a>F6 — RTCM bytes forwarded to GPS hardware unvalidated

**Severity:** Medium

`NtripClientService.ForwardRtcmData` enqueues every byte received from the caster post-header and forwards them verbatim to the GPS module over UDP broadcast (port 2233). No RTCM3 framing validation, no message-type filter.

A compromised/malicious caster can send arbitrary bytes. Some GPS hardware may have parser bugs; in the worst case a crafted stream could brick or misconfigure the GPS module. That's a hardware-level concern not directly exploitable inside AgOpenWeb, but we're the injection point.

**Why accepted as medium:** the GPS module is hardware we don't control; RTCM3 validation is non-trivial and the caster trust model already assumes a reputable provider.

**Recommendation:** at minimum, validate the RTCM3 preamble (`0xD3` byte + length field) and discard runs that don't match. Full message-type filtering is overkill; preamble sanity catches the worst garbage.

### <a name="f7"></a>F7 — Field-directory path traversal via field name

**Severity:** Low

```csharp
// FieldService.cs:159
var fieldDirectory = Path.Combine(fieldsRootDirectory, fieldName);
```

`Path.Combine` does not reject `..` segments. A field name of `../../../../tmp/evil` would produce a path outside the fields root. In practice `fieldName` comes from directory-enumeration (the UI only offers existing field names), so reach is limited — but a crafted IsoXML import or a malformed settings entry could plausibly feed this a bad name.

**Recommendation:** after `Path.Combine`, call `Path.GetFullPath` and verify the result starts with the canonicalized `fieldsRootDirectory`. Reject otherwise.

### <a name="f8"></a>F8 — NTRIP password stored plaintext in AppSettings.json

**Severity:** Low — **accepted risk**

`ConnectionConfig.NtripPassword` is stored as a plain string, serialized to JSON on disk with all other settings.

This is consistent with AgOpenGPS. The tablet is single-user and physical possession already implies full access. Adding OS-keyring encryption would be cross-platform busywork for minimal gain.

**Recommendation:** document in the user-facing settings dialog so the operator knows. No code change.

### <a name="f9"></a>F9 — DNS resolution picks `addresses[0]` only

**Severity:** Low

```csharp
// NtripClientService.ConnectAsync (line 96)
var addresses = await Dns.GetHostAddressesAsync(config.CasterAddress);
casterIP = addresses.Length > 0 ? addresses[0] : throw new Exception("Could not resolve hostname");
```

If the caster has multiple A records for redundancy, we only ever try the first. If the first IP is down, the connection fails even though the caster is reachable via the second. Not a security finding per se — a reliability/usability issue — but worth noting.

**Recommendation:** loop through `addresses` until one connects.

## Not in scope

- **OS-level privilege escalation** — single-user cab app, not a privilege boundary we're defending.
- **Physical tampering with Teensy firmware** — separate project (`Firmware_Teensy_*`), own threat model.
- **Side-channel attacks on RTK signatures** — unrealistic threat class for this deployment.
- **Denial of service by the operator on themselves** — they already have the disengage switch.
- **Supply-chain of AgOpenWeb builds themselves** (release artifact signing) — worth considering but outside scope of this first pass.

## Proposed next steps

Ranking the findings by cost/benefit:

**Do now (cheap + directly exploitable):**
- [F2](#f2) — cap NTRIP header buffer. 5 lines of code, prevents trivial DoS from a hostile caster.
- [F3](#f3) — evaluate Avalonia version bump to get fixed Tmds.DBus.Protocol; or override the version. Likely a package bump.

**Plan and ticket:**
- [F1](#f1) — NTRIP-over-TLS support. Larger design piece; requires compatibility testing with common casters.
- [F5](#f5) — document trusted-LAN requirement in README immediately. HMAC per-deployment is a followup that needs module-firmware coordination.
- [F4](#f4) — add inbound PGN CRC verification. Touches the hot UDP path; measure perf impact.
- [F6](#f6) — RTCM3 preamble validation.

**Defensive coding:**
- [F7](#f7) — add `Path.GetFullPath` + prefix check for field directory resolution.
- [F9](#f9) — iterate DNS results.

**Document-only:**
- [F8](#f8) — note password plaintext storage in the settings UI.

## ATC system-wide threat model

Added for ATC security scans; covers this repository alongside the ATC platform, ATLAS-Edge, ATC-GCP-bridge and tlc-harvester-sdk.


### Scope and evidence

Repositories (default branches, whole repositories unless noted under exclusions):

- `American-Tractor-Company/American-Tractor-Company` — website, account/organization services, Atlas workspace, device API, SDKs, release/infra config (`infra/`, `ops/`, Dockerfiles, cloudbuild files).
- `American-Tractor-Company/American-Tractor-Company-ATLAS-Edge` — on-vehicle ROS capture to MCAP spool (`atlas_edge/`), vehicle profiles (`edge/jets*`), bench-only live `media_bridge/`, systemd units (`deploy/`).
- `American-Tractor-Company/ATC-GCP-bridge` — on-device uploader from the Edge spool to GCS and the Atlas catalog (`atc_gcp_bridge/`, `deploy/atc-gcp-bridge@.service`).
- `American-Tractor-Company/ATC_AgOpenWeb` — ATC fork of AgOpenWeb3: .NET guidance host + web client served on the cab LAN (`Shared/`, `Platforms/`).
- `American-Tractor-Company/tlc-harvester-sdk` — private Rust/Wasm vehicle SDK (`rust/`, `protocol-v2/`, `schemas/`).

Infrastructure evidence lives inside these repositories (GCP configs in `American-Tractor-Company/ops` and `infra/`, systemd units in Edge and bridge). No separate Terraform/Kubernetes repository was found. Existing security documents folded in: the earlier website threat model (profile "ATC repository security threat model", user-confirmed on 2026-11-13), `ATC_AgOpenWeb/Docs/SECURITY_THREAT_MODEL.md`, `ATC-GCP-bridge/docs/security-qualification.md`.

### Components

| Component | Code covered | Boundary |
|---|---|---|
| Web | `American-Tractor-Company`: `app/` public/UI routes, `public/`, `proxy.ts`, client `lib/`, `next.config.mjs` | Untrusted browsers over HTTPS; private responses must not be cached. |
| Identity | `American-Tractor-Company`: `lib/server/auth.ts`, `account-*`, `organization-*`, `activation-console.ts`, `social-profile.ts`, `signup-proof.ts`, `company-email.ts`, auth/org API routes | Better Auth accounts, sessions, org roles, ATC admin vs customer admin. |
| Atlas | `American-Tractor-Company`: `machines.ts`, `machine-*`, `atlas-*`, `farms.ts`, `farm-http.ts`, `guidance-paths.ts`, `app/api/atlas/`, machine routes, `sdk/atlas-browser` | Tenant- and machine-scoped fleet, farm, mission and live data. |
| Device | `American-Tractor-Company`: `devices.ts`, `device-http.ts`, `device-rate-limit.ts`, `telemetry.ts`, `app/api/v1/`, `sdk/atlas-device` | Ed25519-enrolled devices with scoped short-lived sessions and signed sequenced reports. |
| Media | `American-Tractor-Company`: `media*`, camera/recording/media-access routes, `workers/media-service.mjs` | Camera sessions, private GCS recordings, signed URLs. |
| Email | `American-Tractor-Company`: `email-*`, `workers/email-*` | Encrypted outbox, private workers, tokens in mail. |
| Platform | `American-Tractor-Company`: `database.ts`, `config.ts`, `monitoring.ts`, `infra/`, `ops/`, `.github/workflows/`, Dockerfiles, `scripts/`, `db/` | Cloud Run identities, Cloud SQL, GCS, Secret Manager, release/promotion authority. |
| Edge | `ATLAS-Edge`: `atlas_edge/`, `edge/`, `deploy/`, `media_bridge/` | On-vehicle capture and spool; loopback DDS; outbound WHIP for bench media. |
| GCP Bridge | `ATC-GCP-bridge`: all | Reads the Edge spool, holds GCS/WIF credentials and the Atlas device key, uploads create-only. |
| AgOpenWeb | `ATC_AgOpenWeb`: all | Cab guidance host; web UI on LAN port 5174; UDP modules 8888/9999; NTRIP to the internet; steering output. |
| Harvester SDK | `tlc-harvester-sdk`: all | Library: WSS/mTLS telemetry, command handoff, v2 transport; applications own authorization. |

Assign overlapping files by the vulnerable operation.

### Entry points and who reaches them

1. Internet → Atlas website, Better Auth, webhooks (`figma-webhook`), device ingestion (`app/api/v1/devices`). Anonymous users, customers of other orgs, enrolled devices.
2. Device ↔ cloud: Edge/bridge/SDK clients use enrolled-device signed APIs and GCS with pinned service-account/WIF principals.
3. Cab LAN → AgOpenWeb web UI (`http://<host>:5174`, WebSocket commands gated by `ControlAuthority`) and UDP PGN modules; internet → NTRIP client.
4. Local host: Edge spool files read by the bridge; root and the capture producer are trusted (bridge qualification report).
5. Supply chain: GitHub workflows, Cloud Build, NuGet/npm/cargo dependencies, vendored Atlas SDK.

### Assets

- Credentials/authority: account sessions and tokens, device private keys and enrollment/session tokens, GCS/WIF credentials on devices, cloud runtime/migration/release identities, NTRIP credentials.
- Customer data: accounts, org membership, machine ownership, farm boundaries, telemetry/location, missions/coverage, recordings and camera streams.
- Safety/integrity: steering and section commands (AgOpenWeb), device-to-machine binding, recording integrity, release integrity.

User-confirmed priorities (2026-11-17, all repositories): customer tenant data and cross-org isolation in Atlas; vehicle control/steering safety (AgOpenWeb, Edge); device identity/enrollment and GCS recordings; GCP credentials and the release pipeline. The user weighted these all equally; this is context, not a blanket escalation.

### Attacker profiles

User-confirmed (2026-11-17): every attacker matters, including Internet users and other Atlas customers (tenants), a malicious device on the farm/cab LAN, a stolen or compromised vehicle device, and insiders with repository or GCP access. No attacker profile is ruled out.

### Existing controls

- Website: see prior model — Better Auth with session revalidation, origin/CSRF checks, DB-backed rate limits, private IAM Cloud SQL, Ed25519 device proofs, monitoring allowlists, separate promotion approvals.
- Bridge: pinned principal/WIF provider/endpoints, rejection of executable/URL credential sources, create-only generation preconditions, bounded manifests, traversal/hardlink rejection, separate uploader UID, local deletion forbidden (`config.py`, `security.py`).
- Edge: loopback-only DDS profiles; media bridge is bench-only and not started by the capture target.
- AgOpenWeb: single control holder with deadman; simulator never steers; NMEA checksum validation (`Docs/SECURITY_THREAT_MODEL.md`).

### Out of scope and accepted risk (user/author-provided)

User-confirmed (2026-11-17): whole repositories stay in scope; nothing is excluded from scanning. The user kept these draft assumptions as accepted: root administrators on vehicle hosts and the cab LAN are trusted, and AgOpenWeb F5/F6 are accepted risk. The user also said:

- Issues that only affect bench-only or legacy code (`ATLAS-Edge/media_bridge/`, legacy vehicle profiles under `ATLAS-Edge/edge/jets*`, other code marked legacy/bench) are lower priority.
- Issues that only affect staging are lower priority.

Note the tension: "malicious LAN device" is a confirmed attacker, but the user also accepted the cab LAN as trusted and AgOpenWeb F5/F6 as accepted risk. Read together: LAN-only issues that fall within F5/F6 or need a trusted root/LAN position are accepted risk; other LAN-reachable issues are still reported.

Repository-authored records:

- `ATC_AgOpenWeb/Docs/SECURITY_THREAT_MODEL.md` (authored security model) records as design-accepted risk: unauthenticated LAN UDP module spoofing (F5) and forwarding unparsed RTCM to GPS hardware (F6); plaintext NTRIP password in `AppSettings.json` (F8) as design-acceptable; elevation inside the single-user cab app out of scope.
- `ATC-GCP-bridge/docs/security-qualification.md`: root administrators and the configured capture producer are trusted; no device attestation claimed.

### Finding tags

Component (above), CVE, Owner team, Environment (prod/staging/device) — chosen by the user.

### Open questions

- No team-ownership map was supplied; Owner team is set only when repository evidence (CODEOWNERS etc.) names one.
- Live deployment state and physical safety interlocks are unverified.
