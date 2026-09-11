# ReVerse Community Backend

An unofficial, experimental replacement backend and Windows relay for Resident
Evil Re:Verse, intended to let players connect their existing game installations
to a community-hosted server and play together.

## Components

- **Backend:** account management, save-storage APIs, matchmaking, and a
  signaling and packet-relay service for multiplayer connections.
- **Windows relay:** a local .NET application that forwards game service requests
  to a backend address supplied by the player.
- **Tests:** automated checks for backend, relay, and protocol behavior.

The project does not include the game, game assets, or a license to use them.
Players must obtain their own lawful copy and any required permissions.
Compatibility is experimental; bugs, incomplete features, and connection issues
may occur.

## License and ownership

Original project code is provided under the MIT License, without warranty.
See [LICENSE](LICENSE) for the full terms and third-party rights notice.

Resident Evil / Biohazard and related intellectual property belong to CAPCOM
CO., LTD. and/or their respective rights holders. This project is independent
and is not affiliated with, endorsed by, or approved by CAPCOM or Valve.
