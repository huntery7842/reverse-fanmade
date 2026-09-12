# ReVerse Community Backend

An unofficial, experimental replacement backend and desktop relay for Resident
Evil Re:Verse, intended to let players connect their existing game installations
to a community-hosted server and play together.

## Components

- **Backend:** account management, save-storage APIs, matchmaking, and a
  signaling and packet-relay service for multiplayer connections.
- **Desktop relay:** a local Windows or Linux application that forwards game
  service requests to a backend address supplied by the player.
- **Tests:** automated checks for backend, relay, and protocol behavior.

The project does not include the game, game assets, or a license to use them.
Players must obtain their own lawful copy and any required permissions.
Compatibility is experimental; bugs, incomplete features, and connection issues
may occur.

## Running the packages

The backend and relay packages include their .NET runtimes; no .NET installation
is required on the target machine. The backend package targets Windows x64. The
relay is published for Windows x64 and Linux x64, including Steam Deck desktop
mode. Extract the appropriate relay ZIP and run `Relay.Avalonia.exe` on Windows
or `Relay.Avalonia` on Linux. If Linux does not preserve the executable bit, run
`chmod +x Relay.Avalonia` once. The source checkout still requires the .NET SDK.

## License and ownership

Original project code is provided under the MIT License, without warranty.
See [LICENSE](LICENSE) for the full terms and third-party rights notice.

Resident Evil / Biohazard and related intellectual property belong to CAPCOM
CO., LTD. and/or their respective rights holders. This project is independent
and is not affiliated with, endorsed by, or approved by CAPCOM or Valve.
