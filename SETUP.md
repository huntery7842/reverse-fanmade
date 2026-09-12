# ReVerse community setup

This project provides a small local relay for each player and a community
backend that the host runs for the group. The game connects to the local relay
on `127.0.0.1:5080`; the relay connects to the host through ZeroTier.

## Components

| Component | Windows package | Linux / Steam Deck package |
| --- | --- | --- |
| Backend | `ReVerse-backend-win-x64.zip` | `ReVerse-backend-linux-x64.zip` |
| Player relay | `ReVerseRelay-win-x64.zip` | `ReVerseRelay-linux-x64.zip` |

The published packages are self-contained, so a player does not need to
install .NET. Extract the entire package before running it. Source builds
require the .NET 10 SDK.

The Avalonia relay runs on Windows and Linux. Its **Host backend** panel is
available on Windows, where it can detect ZeroTier through `ipconfig` and open
the Windows backend launcher. Linux and Steam Deck hosts start the backend
from a terminal using the commands below.

## Get the packages

Packages are built by the repository's manually started GitHub Actions
workflow. Download the Windows or Linux artifacts that match the machine:

- Windows backend: `ReVerse-backend-win-x64.zip`
- Linux backend: `ReVerse-backend-linux-x64.zip`
- Windows relay: `ReVerseRelay-win-x64.zip`
- Linux relay: `ReVerseRelay-linux-x64.zip`

Do not run an executable directly from inside the ZIP file.

## ZeroTier network

1. Install ZeroTier on the host and every player computer.
2. Create or choose one private ZeroTier network.
3. Give every player the network ID.
4. Authorize every member in the ZeroTier controller.
5. Find the host's managed IPv4 address.

On Windows, run `ipconfig` and use the IPv4 address under the adapter whose
name contains `ZeroTier`. On Linux, use the address shown by the ZeroTier
client or inspect the managed interface with `ip -4 addr`.

The address normally looks like `10.x.x.x`. Call it `HOST_ZT_IP` below. A
player must use this address in the backend URL; `127.0.0.1` is only for the
game-to-relay connection on that same computer.

## Network ports

The host must allow these inbound ports on the ZeroTier interface:

| Port | Protocol | Purpose |
| --- | --- | --- |
| 6080 | TCP | Backend HTTP API |
| 5070 | UDP | Signaling |

On Windows, run PowerShell as administrator:

```powershell
New-NetFirewallRule -DisplayName "ReVerse backend HTTP" -Direction Inbound -Protocol TCP -LocalPort 6080 -Action Allow
New-NetFirewallRule -DisplayName "ReVerse signaling UDP" -Direction Inbound -Protocol UDP -LocalPort 5070 -Action Allow
```

On Linux or Steam Deck, allow TCP `6080` and UDP `5070` in the active firewall.
The exact firewall command depends on the distribution.

## Start the backend on Windows

### Through the Avalonia app

1. Extract the backend package to a folder.
2. Extract and start `Relay.Avalonia.exe` from the relay package.
3. In **Host backend**, click **Browse** and select the folder containing
   `Start-Backend.cmd`.
4. Click **Detect** and verify the ZeroTier IPv4 address.
5. Review the generated command. The default ends with `-Players 2`.
6. Edit the value after `-Players` if needed. Valid match sizes are 2 through
   10.
7. Click **Start backend**. A separate command window opens for the backend.
8. Use **Stop backend** in the app to close the backend process when finished.

The app displays and can copy the player URL, for example:

```text
http://10.205.138.26:6080
```

Use that URL for the host's relay and give it to every remote player.

### Direct launcher

From the folder containing `Start-Backend.cmd`, run:

```powershell
.\Start-Backend.cmd -PublicHost 10.205.138.26 -HttpUrl http://0.0.0.0:6080 -Players 2
```

Replace the address and change `-Players` to a value from 2 through 10. The
`-PublicHost` value must be the host's ZeroTier address. Binding HTTP to
`0.0.0.0` makes the service listen on all interfaces; binding `-HttpUrl` to
the explicit ZeroTier address is also supported.

## Start the backend on Linux or Steam Deck

The Linux backend package contains the self-contained `ReVerse.Capture`
executable. The Windows `Start-Backend.cmd` is not used on Linux.

After extracting the package, run:

```bash
chmod +x ReVerse.Capture
```

Replace the example address, then start the backend:

```bash
HOST_ZT_IP=10.205.138.26
export Relay__Enabled=true
export Steam__Mode=fallback
export Matchmaking__ExperimentalSessionProtocol=true
export Matchmaking__Rulesets__match_master=2
export Matchmaking__IgnorePlayerAttributes=true
export ASPNETCORE_URLS="http://${HOST_ZT_IP}:6080"
export Signaling__Enabled=true
export Signaling__PeerDiagnostics=true
export Signaling__BindAddress=0.0.0.0
export Signaling__PublicHost="${HOST_ZT_IP}"
export Signaling__Port=5070
export Signaling__PublicPort=5070
export Signaling__ExperimentalReplies=true
./ReVerse.Capture
```

Change `Matchmaking__Rulesets__match_master=2` to the desired size from 2
through 10. If the backend cannot bind to the managed address, set
`ASPNETCORE_URLS="http://0.0.0.0:6080"` and leave
`Signaling__PublicHost` set to the ZeroTier address.

Stop the backend with `Ctrl+C` in its terminal.

## Start a relay

Run one relay on every computer, including the host:

- Windows: run `Relay.Avalonia.exe`.
- Linux or Steam Deck:

  ```bash
  chmod +x Relay.Avalonia
  ./Relay.Avalonia
  ```

Fill in:

1. **Username** — the name for this player.
2. **Secret key** — the shared password for the account. It is not saved by
   the relay.
3. **Backend server** — the host URL, such as
   `http://10.205.138.26:6080`.

Click **Start relay** and keep the app running. The relay listens locally for
the game on port `5080`; this port should not be exposed to the network.

The same username may be used by multiple accounts when their secret keys are
different.

## Configure the game

Click **Copy** beside **Launch options** in the relay app and paste the value
into the game's Steam launch options:

```text
/Rebe/HjmUriStr:http://127.0.0.1:5080
```

This option is the same on every computer. Do not replace `127.0.0.1` with the
host's ZeroTier address: the game must talk to its own local relay.

Start the relay before opening the game. Have all players search at roughly
the same time.

## Connection checks

From a Windows player:

```powershell
ping 10.205.138.26
Test-NetConnection 10.205.138.26 -Port 6080
```

From Linux or Steam Deck:

```bash
ping 10.205.138.26
nc -vz 10.205.138.26 6080
```

Replace the address with the host's actual ZeroTier address. A failed TCP
check means the backend URL, backend process, ZeroTier authorization, binding,
or host firewall needs attention. A successful TCP check does not replace the
UDP `5070` firewall rule required for signaling.

## Troubleshooting

### The host app cannot start the backend

The selected backend folder must contain `Start-Backend.cmd`. Re-extract the
backend ZIP if that file is missing. The command shown in the app is editable;
check the address, `-HttpUrl`, and `-Players` value before clicking **Start
backend**.

### ZeroTier address is not detected

On Windows, click **Detect** after ZeroTier is connected. The adapter name must
contain `ZeroTier`. You can enter the managed IPv4 address manually if the
adapter has an unusual name.

### A remote player cannot connect

Confirm that the player is authorized in ZeroTier and is using the host's
ZeroTier URL on TCP `6080`. Check the host firewall and verify that the backend
terminal is still running. Do not use `localhost` or `127.0.0.1` as a remote
player's backend address.

### Everyone remains in “searching for players”

Confirm that every relay is running, every game uses the local launch option,
all players use the same backend URL, and the backend `-Players` value matches
the intended group size. After a backend restart, stop and restart the relays
and games before searching again so stale sessions are not reused.

### Where to look for backend evidence

The backend writes request logs under its `logs` directory, including files
named like `requests-YYYY-MM-DD.jsonl`. The backend terminal also shows startup,
matchmaking, signaling, and error messages. Keep a copy of the relevant log
period when reporting a failed search.

## Legal notice

This is an unofficial community project. Use it at your own risk. See
[LICENSE](LICENSE) for the project's disclaimer and third-party rights notice.
