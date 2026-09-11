"""Isolated, opt-in provider allocation and credential revocation contracts.

Build from backend (pinned SDK):
    dotnet build ../provider-tests/Provider.Tests.csproj -p:NuGetAudit=false
Then run: python integration-tests/signaling_provider_contract.py
Uses only fresh loopback HTTP/UDP ports, a temporary database and artifact logs.
The DTLS helper proves the HTTP-issued key works before cancellation and fails after it.
Synthetic protocol coverage does not establish stock-client gameplay compatibility.
"""
import json
import os
from pathlib import Path
import queue
import socket
import subprocess
import threading

from matchmaking_flow import Harness, ROOT, cancel, event, offer, ticket
from game_join_contract import join_body

SIGNALING = '/v1/gameSession/signaling'
PROBE = Path(os.environ.get('REVERSE_TEST_PROVIDER_DLL', str(ROOT / 'provider-tests/bin/Debug/net10.0/Provider.Tests.dll')))


class ProviderHarness(Harness):
    def start(self):

        if self.env['Signaling__Port'] == str(self.port):
            replacement = free_udp_port()
            while replacement == self.port:
                replacement = free_udp_port()
            self.env['Signaling__Port'] = self.env['Signaling__PublicPort'] = str(replacement)
        try:
            super().start()
        except BaseException:
            if hasattr(self, 'proc') and self.proc.poll() is None:
                self.proc.terminate()
                self.proc.wait(timeout=10)
            self.output.close()
            raise


def free_udp_port():
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        sock.bind(('127.0.0.1', 0))
        return sock.getsockname()[1]


def quiet_barrier(ws, account):
    """The socket writer must deliver all prior events before this command ACK."""
    ws.send(json.dumps({'command': 'session_refresh', 'args': {'sessionKey': account['gameKey']}}))
    frame = json.loads(ws.recv())
    assert frame == {'dataFormatType': 'CMD_RESPONSE', 'command': 'session_refresh'}, 'Unexpected extra notification'


def descriptor_contract(descriptor, port):
    assert set(descriptor) == {'endpoints', 'secret'}, 'Signaling GET must return the descriptor at its root'
    assert isinstance(descriptor['secret'], str) and len(descriptor['secret'].encode('ascii')) == 64
    assert len(descriptor['endpoints']) == 1
    endpoint = descriptor['endpoints'][0]
    assert set(endpoint) == {'protocol', 'identityHint', 'psk', 'server'}
    assert endpoint['protocol'] == 'DTLS'
    assert 0 < len(endpoint['identityHint'].encode('ascii')) <= 39
    assert all(0x21 <= ord(char) <= 0x7e for char in endpoint['identityHint'])
    assert len(bytes.fromhex(endpoint['psk'])) == 32
    assert endpoint['server'] == {'host': '127.0.0.1', 'port': port}
    assert type(endpoint['server']['port']) is int


def probe_line(proc, expected):
    result = queue.Queue()
    threading.Thread(target=lambda: result.put(proc.stdout.readline().strip()), daemon=True).start()
    try:
        line = result.get(timeout=12)
    except queue.Empty as error:
        raise AssertionError('DTLS credential probe timed out') from error
    assert line == expected, 'DTLS credential probe did not reach ' + expected


def check_logs(folder, accounts, descriptors, metadata):
    paths = [folder / 'backend.log', *folder.glob('*.jsonl')]
    assert len(paths) > 1, 'No request audit logs were produced'
    private = [account[key] for account in accounts for key in ('sessionToken', 'gameKey')]
    private += metadata
    for descriptor in descriptors:
        private += [descriptor['secret'], descriptor['endpoints'][0]['psk'], descriptor['endpoints'][0]['identityHint']]
    for path in paths:
        contents = path.read_text(encoding='utf-8')
        for value in private:
            assert value not in contents, 'Credential/member metadata leaked into ' + str(path)
    records = [json.loads(line) for path in folder.glob('*.jsonl') for line in path.read_text(encoding='utf-8').splitlines()]
    successful = [r for r in records if r.get('type') == 'matchmakingRequest' and r.get('path') == SIGNALING
                  and r.get('method') == 'GET' and r.get('responseStatusCode') == 200]
    assert successful, 'No successful credential GET was audited'
    for record in successful:
        descriptor = record['response']
        assert descriptor['secret'] == '[REDACTED]'
        assert descriptor['endpoints'][0]['psk'] == '[REDACTED]'
        assert descriptor['endpoints'][0]['identityHint'] == '[REDACTED]'
    notifications = [r for r in records if r.get('type') == 'matchmakingNotification'
                     and r.get('body', {}).get('dataType') == 'gameSession:signaling:created']
    assert len(notifications) == 3, 'Allocation must publish exactly once per actual member'
    for record in notifications:
        descriptor = record['body']['data']['data']['signaling']
        assert descriptor['secret'] == '[REDACTED]' and descriptor['endpoints'][0]['psk'] == '[REDACTED]'
        assert descriptor['endpoints'][0]['identityHint'] == '[REDACTED]'


def main():
    assert PROBE.is_file(), 'Build provider-tests/Provider.Tests.csproj from backend before running this contract'
    udp_port = free_udp_port()
    h = ProviderHarness(extra_env={
        'Signaling__Enabled': 'true', 'Signaling__BindAddress': '127.0.0.1',
        'Signaling__Port': str(udp_port), 'Signaling__PublicHost': '127.0.0.1',
        'Signaling__PublicPort': str(udp_port), 'Signaling__ExperimentalReplies': 'true',
        'Signaling__Reply19': '1', 'Signaling__Reply21': '1',
        'Signaling__RegistrationFieldB': '', 'Signaling__RegistrationFieldBIsPeerNumber': 'true',
        'Signaling__MaxConnections': '8', 'Signaling__HandshakeTimeoutSeconds': '2',
        'Signaling__IdleTimeoutSeconds': '30', 'Signaling__MaxSessionSeconds': '60',

        'Logging__EventLog__LogLevel__Default': 'None',
        'Matchmaking__Rulesets__test': '3', 'Matchmaking__TicketLifetimeSeconds': '30',
        'Matchmaking__JoinLifetimeSeconds': '30',
    })
    udp_port = int(h.env['Signaling__Port'])
    probe = None
    probe_log = None
    descriptors = []
    accounts = []
    metadata = []
    try:
        assert h.env['Signaling__Enabled'] == 'true'

        assert h.port != udp_port
        accounts = [h.account() for _ in range(4)]
        a, b, c, outsider = accounts
        sockets = [h.connect(account) for account in accounts]
        wa, wb, wc, wo = sockets
        members = list(zip(accounts[:3], sockets[:3]))
        tickets = []
        for account, ws in members:
            tickets.append(h.request('POST', '/v1/matchmaking/ticket', account, ticket(account)))
            event(ws, 'tickets:submitted')
        session = offer(wa)
        assert offer(wb) == offer(wc) == session

        def headers(account, **extra):
            return {'X-Be-Session-Id': session, 'X-Auth-Session-Key': account['gameKey'], **extra}

        def read_state():
            return h.request('GET', '/v1/gameSession', a,
                             headers={'X-Be-Session-Ids': session, 'X-Auth-Session-Key': a['gameKey']})['gameSessions'][0]


        h.request('GET', SIGNALING, headers=headers(a), expected=401)
        h.request('GET', SIGNALING, a, headers=headers(a, **{'X-Auth-Session-Key': outsider['gameKey']}), expected=401)
        h.request('GET', SIGNALING, outsider, headers=headers(outsider), expected=404)
        h.request('GET', SIGNALING, a, headers={'X-Auth-Session-Key': a['gameKey']}, expected=400)
        h.request('GET', SIGNALING, a, headers=headers(a), expected=403)
        h.request('PATCH', SIGNALING, a, {'signalingTimeoutSeconds': 30}, headers=headers(a), expected=403)
        admissions = [join_body(account, nonce) for account, nonce in zip(accounts[:3], (0x01020304, 0xaabbccdd, 0x10203040))]
        metadata = [body['players'][0]['customData1'] for body in admissions]
        for index, ((account, _), admission) in enumerate(zip(members, admissions)):
            before = read_state()['gameSessionSequenceNo']
            reply = h.request('POST', '/v1/gameSession/member/players', account, admission, headers=headers(account))
            for _, ws in members:
                event(ws, 'players:created')
            assert h.request('POST', '/v1/gameSession/member/players', account, admission, headers=headers(account)) == reply
            if index == 0:
                assert read_state()['gameSessionSequenceNo'] == before + 1
                assert h.request('PATCH', SIGNALING, a, {'signalingTimeoutSeconds': 30}, headers=headers(a)) == {}
            if index < 2:
                state = read_state()
                assert state['signaling'] == 'IN_PROGRESS'
                h.request('GET', SIGNALING, a, headers=headers(a), expected=503)
                h.request('GET', SIGNALING, c, headers=headers(c), expected=403)
                assert h.request('PATCH', SIGNALING, a, {'signalingTimeoutSeconds': 30}, headers=headers(a)) == {}
                assert read_state() == state, 'Pending retry changed sequence/state'
                for member, ws in members:
                    quiet_barrier(ws, member)
        state = read_state()
        assert state['signaling'] == 'COMPLETED' and state['gameSessionSequenceNo'] == 5
        for account, ws in members:
            frame = event(ws, 'signaling:created')
            payload = frame['data']['data']
            assert set(payload) == {'sessionId', 'gameSessionSequenceNo', 'signaling'}
            assert payload['sessionId'] == session and payload['gameSessionSequenceNo'] == state['gameSessionSequenceNo']
            descriptor = h.request('GET', SIGNALING, account, headers=headers(account))
            descriptor_contract(descriptor, udp_port)
            assert payload['signaling'] == descriptor, 'Notification and scoped GET credentials differ'
            for _ in range(3):
                assert h.request('GET', SIGNALING, account, headers=headers(account)) == descriptor
            descriptors.append(descriptor)
        for key in ('identityHint', 'psk'):
            assert len({d['endpoints'][0][key] for d in descriptors}) == 3, 'Accounts share credentials'
        assert len({d['secret'] for d in descriptors}) == 3

        assert h.request('GET', SIGNALING, a, headers=headers(a, **{'X-Be-Account-Id': b['id']})) == descriptors[0]
        h.request('GET', SIGNALING, a, headers=headers(a, **{'X-Be-Session-Id': session + ',foreign'}), expected=400)
        h.request('GET', SIGNALING, a, headers=headers(a, **{'X-Be-Session-Id': 'foreign'}), expected=404)
        h.request('GET', SIGNALING, outsider, headers=headers(outsider), expected=404)
        h.request('GET', SIGNALING, a, headers=headers(a, **{'X-Auth-Session-Key': b['gameKey']}), expected=401)
        for _ in range(3):
            assert h.request('PATCH', SIGNALING, a, {'signalingTimeoutSeconds': 30}, headers=headers(a)) == {}
        h.request('PATCH', SIGNALING, a, {'signalingTimeoutSeconds': 29}, headers=headers(a), expected=409)
        h.request('PATCH', SIGNALING, b, {'signalingTimeoutSeconds': 30}, headers=headers(b), expected=403)
        h.request('PATCH', SIGNALING, a, {'signaling': descriptors[0]}, headers=headers(a), expected=400)
        h.request('PATCH', SIGNALING, a, {'signalingTimeoutSeconds': 30, 'signaling': descriptors[0]}, headers=headers(a), expected=400)
        for account, admission in zip(accounts[:3], admissions):
            changed = join_body(account, 999)
            h.request('POST', '/v1/gameSession/member/players', account, changed, headers=headers(account), expected=409)
            h.request('PATCH', '/v1/gameSession/members', account,
                      {'customData1': changed['players'][0]['customData1']}, headers=headers(account), expected=409)
            h.request('PATCH', '/v1/gameSession/members', account,
                      {'customData1': '{"version":1,"nonce":"bad"}'}, headers=headers(account), expected=400)
        assert read_state() == state, 'Retry or rejected mutation changed allocation/member metadata'
        for account, ws in zip(accounts, sockets):
            quiet_barrier(ws, account)

        probe_log = (h.folder / 'dtls-probe.log').open('w', encoding='utf-8')
        probe = subprocess.Popen(['dotnet', str(PROBE), '--http-probe'], cwd=ROOT / 'backend',
                                 stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=probe_log, text=True,
                                 creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)

        probe.stdin.write(json.dumps(descriptors[0]) + '\n')
        probe.stdin.flush()
        probe_line(probe, 'CONNECTED')
        assert cancel(h, a, tickets[0]) == {}
        assert cancel(h, a, tickets[0]) == {}
        for account, ws in members:
            event(ws, 'offers:failed')
            event(ws, 'tickets:failed')
            if account is a:
                event(ws, 'tickets:canceled')
            h.request('GET', SIGNALING, account, headers=headers(account), expected=404)
            quiet_barrier(ws, account)
        quiet_barrier(wo, outsider)
        probe.stdin.write('VERIFY_REVOKED\n')
        probe.stdin.flush()
        probe_line(probe, 'REVOKED')
        assert probe.wait(timeout=8) == 0, 'Revoked DTLS identity/PSK was accepted'
        h.request('GET', '/v1/gameSession', a, headers={'X-Be-Session-Ids': session}, expected=404)
    finally:
        if probe is not None:
            if probe.poll() is None:
                probe.terminate()
                probe.wait(timeout=8)
            probe.stdin.close()
            probe.stdout.close()
        if probe_log is not None:
            probe_log.close()
        h.close()
    check_logs(h.folder, accounts, descriptors, metadata)
    print('PASS isolated enabled provider: all actual joins, scoped GET/root shape, per-account events/credentials,')
    print('     stable retries, immutable nonce metadata, cancellation/DTLS key revocation, complete log redaction')
    print('Artifacts:', h.folder)


if __name__ == '__main__':
    main()
