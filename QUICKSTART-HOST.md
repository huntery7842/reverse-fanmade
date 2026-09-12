# Host quick start

This guide is for the player who hosts the community backend. The backend is
reachable through the host's ZeroTier address; every player runs their own
local relay and points it at that address.

## 1. Join the ZeroTier network

Install ZeroTier on every computer, join the same network, and authorize every
member in the ZeroTier controller. Give the other players the network ID.

Find the host's managed IPv4 address. On Windows, open a terminal and run:

```text
ipconfig
```

Use the IPv4 address under the adapter whose name contains `ZeroTier`. It will
usually look like `10.x.x.x`. Do not give players `127.0.0.1` or a normal
home-LAN address.

## 2. Open the host ports

On Windows, run PowerShell as administrator on the host:

```powershell
New-NetFirewallRule -DisplayName "ReVerse backend HTTP" -Direction Inbound -Protocol TCP -LocalPort 6080 -Action Allow
New-NetFirewallRule -DisplayName "ReVerse signaling UDP" -Direction Inbound -Protocol UDP -LocalPort 5070 -Action Allow
```

On Linux or Steam Deck, allow TCP `6080` and UDP `5070` in the firewall used by
the host. ZeroTier must also be allowed to communicate on the managed network.

## 3. Start the backend on Windows

1. Download and extract both the backend and relay packages.
2. Start `Relay.Avalonia.exe` from the relay package.
3. In **Host backend**, choose the folder containing `Start-Backend.cmd`.
4. Click **Detect** next to the ZeroTier address and check that the detected
   address is the host's managed address.
5. Review the generated command. Change `-Players 2` to the number of players
   you want, from 2 through 10.
6. Click **Start backend**. It opens the backend in a separate command window.
   Use **Stop backend** in the relay app when the session is over.
7. Copy the **Players use backend address** URL. It should be similar to:

   ```text
   http://10.205.138.26:6080
   ```

The backend folder must contain the supplied `Start-Backend.cmd`. If the
command needs an unusual option, edit the command field before starting it.

## 4. Start the host's relay

In the same Avalonia app, fill in:

- **Username**: the name this player will use.
- **Secret key**: the shared password for this username. It is not saved by
  the relay.
- **Backend server**: the copied player backend URL, for example
  `http://10.205.138.26:6080`.

Click **Start relay**. Copy the launch option shown in the app:

```text
/Rebe/HjmUriStr:http://127.0.0.1:5080
```

Add that value to the game's Steam launch options before starting the game.

## 5. Give players the connection details

Send each player:

1. The ZeroTier network ID.
2. The copied backend URL, such as `http://10.205.138.26:6080`.
3. The launch option shown above.
4. The shared secret key and the username they should use.

Each player must choose their own username. The same username can be used by
multiple accounts when their secret keys differ.

## 6. Linux or Steam Deck host

The backend and Avalonia relay packages include their .NET runtime. The
Windows host panel is not available on Linux, so start the backend from a
terminal instead.

After extracting the Linux backend package, make the executable runnable:

```bash
chmod +x ReVerse.Capture
```

Replace `10.205.138.26` below with the host's ZeroTier IPv4 address, then run:

```bash
export Relay__Enabled=true
export Steam__Mode=fallback
export Matchmaking__ExperimentalSessionProtocol=true
export Matchmaking__Rulesets__match_master=2
export Matchmaking__IgnorePlayerAttributes=true
export ASPNETCORE_URLS=http://10.205.138.26:6080
export Signaling__Enabled=true
export Signaling__PeerDiagnostics=true
export Signaling__BindAddress=0.0.0.0
export Signaling__PublicHost=10.205.138.26
export Signaling__Port=5070
export Signaling__PublicPort=5070
export Signaling__ExperimentalReplies=true
./ReVerse.Capture
```

Change `Matchmaking__Rulesets__match_master=2` to a value from 2 through 10
when a different match size is needed. If binding to the managed address
fails, use `ASPNETCORE_URLS=http://0.0.0.0:6080`; keep
`Signaling__PublicHost` set to the ZeroTier address.

The Linux relay is started with:

```bash
chmod +x Relay.Avalonia
./Relay.Avalonia
```

Enter the same backend URL in its relay panel and use the same game launch
option.

## 7. Start together

Start the backend first, then all relays, then launch the game on every
computer with the local relay option. Have everyone search at roughly the
same time. Keep the backend and relay windows open while playing.

For remote players, the backend URL is the host's ZeroTier address on port
6080. The game launch option always points to the player's own computer at
`127.0.0.1:5080`.
