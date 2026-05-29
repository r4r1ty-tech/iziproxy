# IziProxy

Cross-platform C# tool for automated deployment and management of **Xray VLESS + XHTTP + REALITY** proxy servers on Ubuntu/Debian VPS.

The GUI is built with **Avalonia UI** and runs on Windows, Linux, and Android from a single shared codebase.

---

## What it does

1. **Connects to a bare VPS via SSH/SFTP** and uploads deployment scripts.
2. **Runs a Bash setup script** (`Deploy.sh`) that installs Xray, enables TCP BBR, probes candidate SNI domains directly from the server, opens firewall ports, and generates an x25519 key pair and UUID.
3. **Generates `vless://` connection links** (and QR codes) for each inbound so the client app can be configured in seconds.
4. **Monitors the running server**: Xray service status, config validation via `xray -test -config`, and per-inbound traffic stats via the Xray gRPC stats API.

---

## Architecture

```
IziProxy/
├── IziProxy.Core/               # Class library — all domain logic
│   ├── SSH.cs                   # SSH/SFTP wrapper (Renci.SshNet)
│   ├── DeploySckripts.cs        # Uploads scripts, runs deploy, parses output
│   ├── XrayMonitor.cs           # Service status, config check, traffic stats
│   ├── XrayConfigParams.cs      # Key/UUID generation, SNI/port model
│   ├── VlessLinkGenerator.cs    # Builds vless:// URIs
│   ├── ServerConfig.cs          # Connection parameters model
│   └── VDS_setup/               # Deploy.sh, MainInstall.sh, config.json template
│
├── IziProxy.GUI/
│   ├── IziProxy.GUI/            # Shared Avalonia UI (Views, ViewModels, assets)
│   │   ├── ViewModels/          # CommunityToolkit.Mvvm reactive ViewModels
│   │   ├── Views/               # AXAML views (Deploy, Dashboard, Logs)
│   │   └── VdsProfileService.cs # Server profile persistence (JSON, %localappdata%)
│   ├── IziProxy.GUI.Desktop/    # Entry point — Windows / Linux / macOS
│   ├── IziProxy.GUI.Android/    # Entry point — Android
│   └── IziProxy.GUI.Browser/    # Entry point — WebAssembly (experimental)
│
├── IziProxy/                    # CLI entry point (console deploy)
└── tests/IziProxy.Tests/        # xUnit test suite
```

### Key dependencies

| Package | Purpose |
|---|---|
| `Avalonia` 11 | Cross-platform UI framework |
| `CommunityToolkit.Mvvm` 8 | MVVM source generators |
| `SSH.NET` 2025.1 | SSH/SFTP client |
| `QRCoder` 1.8 | Offline QR code rendering |
| `Material.Icons.Avalonia` | Material Design icon set |

---

## Requirements

- **.NET 10 preview SDK** on the build machine.
- A VPS running **Ubuntu 20.04+** or **Debian 11+** with SSH access.

---

## Run locally

**GUI (desktop):**
```bash
dotnet run --project IziProxy.GUI/IziProxy.GUI.Desktop/IziProxy.GUI.Desktop.csproj
```

**CLI:**
```bash
dotnet run --project IziProxy/IziProxy.csproj
```

---

## Usage overview

### 1. Deploy tab

1. Enter the server IP, SSH username, and password or select a private key file.
2. Optionally save the server profile — credentials are stored in `%localappdata%/IziProxy/profiles.json`.
3. Click **Test connection** to verify SSH/SFTP access before running the full install.
4. Click **Install**. The script runs on the server and streams output to the Logs tab. When finished, `vless://` links and QR codes appear at the bottom.

### 2. Dashboard tab

- Shows live Xray service status (`systemctl is-active xray`).
- **Restart** button: `systemctl restart xray`.
- **Check config** button: runs `xray -test -config /etc/xray/config.json` and shows any errors.
- **Traffic** section: queries `xray api statsquery` over gRPC and displays per-inbound uplink/downlink byte counters.

---

## Tests

```bash
dotnet test
```

Test coverage in [`tests/IziProxy.Tests/`](tests/IziProxy.Tests/):

| File | What is tested |
|---|---|
| `VlessLinkGeneratorTests.cs` | Correct `vless://` URI construction |
| `ServerConfigTests.cs` | `ServerConfig` model validation |
| `SshTests.cs` | SSH failure paths — returns `false` / throws when not connected |
| `DeployScriptsTests.cs` | Deploy behaviour without an active connection |
| `XrayConfigParamsTests.cs` | Xray params model |

---

## CI/CD

GitHub Actions builds are triggered manually (`workflow_dispatch`):

| Workflow | Runner | Output |
|---|---|---|
| [`linux-build.yml`](.github/workflows/linux-build.yml) | `ubuntu-latest` | `IziProxy-Linux-x86_64.AppImage` uploaded to release `v1.0.0` |
| [`android-build.yml`](.github/workflows/android-build.yml) | `windows-latest` | Signed `.apk` uploaded to release `v1.0.0` |

Both workflows use `dotnet publish` with `--self-contained` and upload artifacts via `gh release upload`.

---

## Protocol notes

- **VLESS + XHTTP + REALITY** — transport looks like a regular TLS HTTPS session to a real domain (SNI), making traffic hard to fingerprint.
- The deploy script probes a list of candidate domains from the VPS itself and selects the one with the best latency and valid TLS parameters.
- Three inbounds are configured (ports 443, 8443, and a random port) with distinct SNI domains, giving the client three independent connection options.

---

## License

MIT — for educational and personal use.
