# ReVerse Community Backend

An unofficial, experimental, self-hosted community server backend and desktop
relay for Resident Evil Re:Verse. It is intended to let players connect their
existing game installations to a community-hosted server and play together
over a private ZeroTier network.

## Keywords and topics

Resident Evil Re:Verse community server, unofficial backend, self-hosted game
server, multiplayer matchmaking, local game relay, private server, ZeroTier
virtual LAN, Windows gaming, Linux gaming, Steam Deck, .NET, Avalonia UI,
signaling, persistent game saves, and experimental protocol compatibility.

## Components

- **Backend:** account management, save-storage APIs, matchmaking, and a
  signaling and packet-relay service for multiplayer connections.
- **Desktop relay:** a cross-platform Windows or Linux application built with
  Avalonia that forwards game service requests to a backend address supplied by
  the player.
- **Tests:** automated checks for backend, relay, and protocol behavior.

The backend is designed for a small private group rather than a public hosted
service. The host runs the backend, each player runs one local relay, and the
relays communicate with the host through the ZeroTier network.

The project does not include the game, game assets, or a license to use them.
Players must obtain their own lawful copy and any required permissions.
Compatibility is experimental; bugs, incomplete features, and connection issues
may occur.

## Quick starts

- [Host quick start](QUICKSTART-HOST.md)
- [Player quick start](QUICKSTART-PLAYER.md)
- [Complete setup guide](SETUP.md)

## Running the packages

The backend and relay packages include their .NET runtimes; no .NET installation
is required on the target machine. The backend is published for Windows x64 and
Linux x64. The relay is published for Windows x64 and Linux x64, including
Steam Deck desktop mode. Extract the appropriate package ZIP and follow the
[setup guide](SETUP.md). Run `Relay.Avalonia.exe` on Windows or
`Relay.Avalonia` on Linux. If Linux does not preserve the executable bit, run
`chmod +x Relay.Avalonia` once. The source checkout still requires the .NET
SDK.

The manually triggered GitHub Actions workflow builds the four self-contained
packages and attaches them to a tagged GitHub release:

Download the current packages from the [ReVerse v0.1.0 release](https://github.com/lannahirave/reverse-fanmade/releases/tag/v0.1.0).

- `ReVerse-backend-win-x64.zip`
- `ReVerse-backend-linux-x64.zip`
- `ReVerseRelay-win-x64.zip`
- `ReVerseRelay-linux-x64.zip`

## License and ownership

Original project code is provided under the MIT License, without warranty.
See [LICENSE](LICENSE) for the full terms and third-party rights notice.

Resident Evil / Biohazard and related intellectual property belong to CAPCOM
CO., LTD. and/or their respective rights holders. This project is independent
and is not affiliated with, endorsed by, or approved by CAPCOM or Valve.
