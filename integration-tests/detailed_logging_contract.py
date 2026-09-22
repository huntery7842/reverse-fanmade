"""Full traffic logging can be enabled and disabled while the backend is running."""
import base64
import json
import socket
import tempfile
import time
from pathlib import Path

from matchmaking_flow import Harness, event, join_body
from private_match_contract import create_body


def records(folder):
    return [json.loads(line) for path in folder.glob('detailed-traffic-*.jsonl')
            for line in path.read_text(encoding='utf-8').splitlines()]


def main():
    with tempfile.TemporaryDirectory(prefix='reverse-detailed-') as directory:
        marker = Path(directory) / 'detailed-logs.enabled'
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as port_probe:
            port_probe.bind(('127.0.0.1', 0))
            signaling_port = port_probe.getsockname()[1]
        h = Harness(extra_env={'Capture__DetailedLogsControlFile': str(marker),
                               'Matchmaking__JoinLifetimeSeconds': '30',
                               'Signaling__Enabled': 'true',
                               'Signaling__BindAddress': '127.0.0.1',
                               'Signaling__Port': str(signaling_port),
                               'Signaling__PublicHost': '127.0.0.1',
                               'Signaling__PublicPort': str(signaling_port)})
        try:
            host, guest = h.account('detail-host'), h.account('detail-guest')
            assert not records(h.folder)

            marker.write_text('enabled', encoding='utf-8')
            login = h.request('POST', '/relay/account/login',
                              body={'username': 'detail-secret', 'secretKey': 'visible-secret'})
            wh, wg = h.connect(host), h.connect(guest)
            info = h.request('POST', '/v1/gameSession', host, create_body(host))['gameSessions'][0]
            event(wh, 'players:created')
            headers = {'X-Be-Session-Id': info['sessionId'],
                       'X-Be-Session-Keyword': info['keyword']}
            h.request('POST', '/v1/gameSession/member/players', guest, join_body(guest), headers=headers)
            event(wh, 'players:created'); event(wg, 'players:created')
            h.request('DELETE', '/v1/gameSession/members?reason=', host,
                      headers={**headers, 'X-Be-Account-Id': guest['id']})
            event(wh, 'players:deleted'); event(wg, 'players:deleted')
            udp_probe = b'detailed-udp-probe'
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as udp:
                udp.sendto(udp_probe, ('127.0.0.1', signaling_port))
            deadline = time.monotonic() + 2
            while not any(r['type'] == 'udpDatagram' for r in records(h.folder)):
                assert time.monotonic() < deadline, 'UDP datagram was not recorded'
                time.sleep(.02)

            logged = records(h.folder)
            requests = [r['data'] for r in logged if r['type'] == 'httpRequestStart']
            assert any(r['method'] == 'DELETE' and r['rawTarget'] == '/v1/gameSession/members?reason='
                       and r['headers']['X-Relay-Session-Token'] == [host['sessionToken']]
                       and r['remoteAddress'] == '127.0.0.1' for r in requests)
            assert any(r['type'] == 'httpRequestBody' and 'maxPlayers' in (r['data']['payload']['Utf8'] or '')
                       for r in logged)
            assert any(r['type'] == 'httpRequestBody' and 'visible-secret' in (r['data']['payload']['Utf8'] or '')
                       for r in logged)
            assert any(r['type'] == 'httpResponseBody' and login['sessionToken'] in (r['data']['payload']['Utf8'] or '')
                       for r in logged)
            assert any(r['type'] == 'httpResponseBody' and info['sessionId'] in (r['data']['payload']['Utf8'] or '')
                       for r in logged)
            assert any(r['type'] == 'udpDatagram' and r['data']['direction'] == 'clientToBackend'
                       and r['data']['remoteAddress'] == '127.0.0.1'
                       and base64.b64decode(r['data']['payload']['Base64']) == udp_probe
                       for r in logged)
            outgoing = [r['data'] for r in logged if r['type'] == 'webSocketMessage'
                        and r['data']['direction'] == 'backendToClient'
                        and r['data']['account'] == guest['id']]
            sent = [json.loads(r['payload']['Utf8']) for r in outgoing if r['payload']['Utf8']]
            assert any(message.get('dataType') == 'gameSession:member:players:deleted'
                       and message['data']['data']['member']['players'][0]['joinState'] == 'CLIENT_KILLED'
                       and message['data']['data']['member']['players'][0]['customData1'] ==
                       join_body(guest)['players'][0]['customData1']
                       for message in sent)

            marker.unlink()
            count = len(logged)
            h.request('GET', '/v1/gameSession', host, headers={'X-Be-Session-Ids': info['sessionId']})
            assert len(records(h.folder)) == count
            print('PASS detailed traffic includes raw HTTP and kick WebSocket payload, then stops live')
        finally:
            h.close()


if __name__ == '__main__':
    main()
