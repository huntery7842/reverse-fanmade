"""Replays the exact startup sequence the RE:Verse client performs against the mock backend,
asserting the parser-level response contract from research/PROTOCOL_CONTRACT.md."""
import argparse
import datetime
import http.client
import json
import os
from pathlib import Path
import socket
import subprocess
import time

ROOT = Path(__file__).resolve().parents[1]

def port():
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        return sock.getsockname()[1]

def ok(name, data):
    print(f"  PASS {name}")
    return data

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--skip-build', action='store_true')
    args = parser.parse_args()
    if not args.skip_build:
        built = subprocess.run(['dotnet', 'build', '--nologo', '-c', 'Debug'], cwd=ROOT / 'backend', capture_output=True, text=True)
        assert built.returncode == 0, built.stdout + built.stderr
    candidates = list((ROOT / 'backend').glob('**/bin/Debug/net10.0/*.runtimeconfig.json'))
    assert len(candidates) == 1, candidates
    dll = Path(str(candidates[0]).replace('.runtimeconfig.json', '.dll'))
    folder = ROOT / 'integration-tests' / 'artifacts' / datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    folder.mkdir(parents=True)
    http_port = port()
    env = dict(os.environ)
    env.update({
        'ASPNETCORE_URLS': f'http://127.0.0.1:{http_port}',
        'Capture__LogDirectory': str(folder / 'requests'),
        'Capture__RedactSensitiveHeaders': 'true',


        'Steam__Mode': 'fallback',
    })
    output = (folder / 'server-output.log').open('w', encoding='utf-8')
    proc = subprocess.Popen(['dotnet', str(dll)], cwd=ROOT / 'backend', env=env,
                            stdout=output, stderr=subprocess.STDOUT,
                            creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
    connection = None
    def request(method, path, body=None, headers=None, inspect_response=False):
        nonlocal connection
        if connection is None:
            connection = http.client.HTTPConnection('127.0.0.1', http_port, timeout=10)
        connection.request(method, path, body=body, headers=headers or {})
        response = connection.getresponse()
        payload = response.read()
        if response.status != 200:
            raise AssertionError(f'{method} {path} -> HTTP {response.status}: {payload.decode(errors="replace")}')
        parsed = json.loads(payload) if payload else {}
        if inspect_response:
            assert response.getheader('Transfer-Encoding') is None, response.getheaders()
            assert response.getheader('Content-Length') == str(len(payload)), response.getheaders()
            assert payload == json.dumps(parsed, separators=(',', ':')).encode(), payload
        return parsed
    check = []
    try:
        deadline = time.monotonic() + 25
        while True:
            assert proc.poll() is None, 'Backend exited; inspect ' + str(folder / 'server-output.log')
            try:
                request('GET', '/capture-test/ready')
                break
            except (OSError, http.client.HTTPException):
                if time.monotonic() >= deadline:
                    raise
                time.sleep(0.1)

        bearer = {'Authorization': 'Bearer header.eyJpYXQiOjE3NTcxMDAwMDAsImV4cCI6MTc1NzE4NjQwMCwibGlua2VkIjp0cnVlfQ.signature'}


        boot = request('GET', '/systems/RVS-B-WW/40000/system.json')
        assert boot['json_ver'] == '1.0.0' and boot['working_state'] == 'alive', boot
        assert isinstance(boot['api_timeout'], int), boot
        assert boot['selector'].startswith('http://'), boot
        assert boot['tmr'].startswith('http://') and boot['mtm'].startswith('http://'), boot
        check.append('system.json bootstrap')


        sign = request('POST', '/v1/steam-steam/sign/RVS-B-WW', headers={'Authorization': 'raw-token'})
        token = sign['rebe_token']
        assert token.count('.') == 2, sign
        check.append('sign-in rebe_token')


        ref = request('GET', '/v1/token/refresh/', headers=bearer)
        assert ref['rebe_token'].count('.') == 2 and 'gcp_token' in ref, ref
        check.append('token refresh')


        sel = request('GET', '/v1/selector/destination?revision=40000', headers=bearer)
        ntp = sel['notificationTransportParameter']
        assert isinstance(ntp['reconnectRetryNum'], int) and isinstance(ntp['reconnectWaitTime'], int) and isinstance(ntp['reconnectCapTime'], int), ntp
        assert isinstance(sel['gameSessionParameter']['sureNotificationTimeout']['defaultTimeout'], int)
        assert isinstance(sel['gameSessionParameter']['abandonedTimeout'], int)
        assert isinstance(sel['matchmakingParameter']['sureNotificationTimeout']['defaultTimeout'], int)
        assert isinstance(sel['authsessionParameter']['gameServiceActivatedAfterOpenTime'], int)
        assert isinstance(sel['services'], list) and sel['services'], sel
        expected_services = ['mtms', 'notification', 'crossapp', 'presence', 'relationship',
                             'messaging', 'lastonemile', 'playarea', 'matchmaking', 'gamesession']
        assert [service['name'] for service in sel['services']] == expected_services, sel['services']
        services_by_name = {service['name']: service for service in sel['services']}
        assert services_by_name['notification']['endpoint'] == 'ws://127.0.0.1:5080/', sel['services']
        assert all(service['endpoint'] == 'http://127.0.0.1:5080'
                   for name, service in services_by_name.items() if name != 'notification'), sel['services']
        assert all(isinstance(service['apiTimeout'], int) for service in sel['services']), sel['services']
        check.append('selector destination (8 required paths and 10 consumer-mapped services)')


        bg = request('GET', '/v1/batch-get/service-id/', headers=bearer)
        assert bg['matched'][0]['service'] == 'steam' and bg['matched'][0]['sub'] != '' and bg['unmatched'] == [], bg
        check.append('batch-get service-id')


        bgr = request('GET', '/v1/batch-get/service-id/reverse/', headers=bearer)
        assert bgr['matched'][0]['sub'] != '', bgr
        check.append('batch-get service-id reverse')




        lifecycle_headers = {**bearer, 'Connection': 'close'}
        op = request('POST', '/v1/open/some-service', headers=lifecycle_headers, inspect_response=True)
        assert isinstance(op['expires_at'], int) and isinstance(op['session_key'], str), op
        check.append('open service')

        connection.close()
        connection = None
        ver = request('POST', '/v1/verify/some-service',
                      headers={**lifecycle_headers, 'Content-Type': 'application/json'},
                      body=json.dumps({'session_key': 'local-session-key'}), inspect_response=True)
        assert ver['id_token']['sub'] != '' and 'sub_nickname' in ver['id_token'], ver
        check.append('verify service')


        sp = request('GET', '/v1/service_profile/by_sub/local-player', headers=bearer)
        assert sp['users'][0]['sub'] == 'local-player' and sp['users'][0]['service_profiles'][0]['nickname'], sp
        check.append('service_profile by_sub')


        nick = request('GET', '/v1/nickname/by_match/local-player', headers=bearer)
        assert nick['users'][0]['sub'] == 'local-player' and nick['users'][0]['nicknames'][0]['nickname'], nick
        check.append('nickname by_match')


        ca = request('GET', '/v1/crossapp/apps', headers=bearer)
        assert ca['apps'][0]['name'] == 'reverse', ca
        check.append('crossapp apps')


        pres = request('GET', '/v1/presence/presence/get', headers=bearer)
        p0 = pres['presences'][0]
        assert p0['accountId'] != '' and p0['online'] is True and isinstance(p0['lastOfflineChangedAt'], int), p0
        a0 = p0['apps'][0]
        assert a0['app'] and a0['platform'] and a0['service'] and a0['userId'] and a0['gamesessionIds'] == [], a0
        check.append('presence get')





        pa = request('GET', '/v1/playarea/user', headers=bearer)
        assert pa['set']['area'] != '' and pa['set']['region'] != '' and pa['auto']['area'] != '', pa
        assert pa['areas'][0]['name'] != '' and pa['areas'][0]['regions'][0]['name'] != '', pa
        assert pa['areas'][0]['regions'][0]['state'] == 'up', pa
        assert isinstance(pa['areas'][0]['regions'][0]['latency'], int), pa


        saved = request('POST', '/v1/playarea/user', headers={**bearer, 'Content-Type': 'application/json'},
                        body=json.dumps({'auto': {'area': 'RVS-B-WW', 'region': 'RVS-B-WW'}}))
        for selection in ('set', 'auto'):
            assert any(area['name'] == saved[selection]['area'] and
                       region['name'] == saved[selection]['region'] and region['state'] == 'up'
                       for area in saved['areas'] for region in area['regions']), saved
        check.append('playarea GET+POST retains selected usable region')
        ok('playarea user/auto', request('POST', '/v1/playarea/user/auto', headers={**bearer, 'Content-Type': 'application/json'}))
        ok('playarea user/set', request('POST', '/v1/playarea/user/set', headers={**bearer, 'Content-Type': 'application/json'}, body=json.dumps({'playarea_id': 'local'})))


        auto = request('POST', '/v1/playarea/user/auto', headers={**bearer, 'Content-Type': 'application/json'},
                       body=json.dumps({'area': 'RVS-B-WW', 'region': 'RVS-B-WW'}))
        assert auto['set']['area'] == 'RVS-B-WW' and auto['areas'][0]['regions'][0]['name'] == 'RVS-B-WW', auto
        assert auto['areas'][0]['regions'][0]['state'] == 'up', auto
        assert isinstance(auto['areas'][0]['regions'][0]['latency'], int), auto
        check.append('playarea user/auto+set parser shape')
        lom = request('GET', '/v1/lastonemile/playarea/local', headers=bearer)
        assert lom['set']['area'] == 'local', lom
        assert lom.get('playarea') == 'local' and lom.get('region') == 'local', lom
        assert lom['areas'][0]['regions'][0]['state'] == 'up', lom
        assert isinstance(lom['areas'][0]['regions'][0]['latency'], int), lom
        check.append('lastonemile playarea')



        pg = request('POST', '/v1/presence/gamesession', headers={**bearer, 'Content-Type': 'application/json'})
        assert isinstance(pg.get('presences'), list) and pg['presences'], pg
        check.append('presence gamesession')



        rel = request('GET', '/v1/relationship/1stparty', headers=bearer)
        assert 'requestList' in rel and 'relations' in rel, rel
        check.append('relationship 1stparty')
        rel2 = request('GET', '/v2/relationship/relation?relationshipType=FRIEND', headers=bearer)
        assert isinstance(rel2.get('amount'), int), rel2
        assert isinstance(rel2.get('relations'), list) and rel2['relations'], rel2
        r0 = rel2['relations'][0]
        assert isinstance(r0.get('accountId'), str) and r0['accountId'] != '', r0
        assert isinstance(r0.get('createdAt'), int), r0
        check.append('relationship v2 relation (shared relations parser)')


        fr = request('GET', '/v2/relationship/friend/request?requestType=FRIEND_REQ', headers=bearer)
        assert isinstance(fr.get('amount'), int), fr
        assert isinstance(fr.get('requestType'), str) and fr['requestType'] != '', fr
        assert isinstance(fr.get('requestList'), list) and fr['requestList'], fr
        f0 = fr['requestList'][0]
        assert isinstance(f0.get('accountId'), str) and f0['accountId'] != '', f0
        assert isinstance(f0.get('createdAt'), int), f0
        check.append('relationship v2 friend/request (requestList parser)')

        ok('relationship v2 relation POST', request('POST', '/v2/relationship/relation',
            headers={**bearer, 'Content-Type': 'application/json'}, body=json.dumps({'target': 'local-player'})))
        ok('relationship v2 friend/request POST', request('POST', '/v2/relationship/friend/request',
            headers={**bearer, 'Content-Type': 'application/json'}, body=json.dumps({'target': 'local-player', 'requestType': 'FRIEND_REQ'})))



        connection.request('POST', '/v1/matchmaking/ticket', headers=bearer)
        rejected = connection.getresponse()
        assert rejected.status == 401, rejected.status
        rejected.read()
        check.append('matchmaking rejects legacy unauthenticated mock flow')


        msg_post = request('POST', '/v1/messaging/message', headers={**bearer, 'Content-Type': 'application/json'},
                           body=json.dumps({'type': 'chat', 'text': 'hello'}))
        assert msg_post['messageId'], msg_post
        msg_get = request('GET', '/v1/messaging/message?messageType=chat', headers=bearer)
        assert 'messages' in msg_get, msg_get
        check.append('messaging post/get')


        r = request('POST', '/v1/refresh/reverse', headers=bearer)
        assert r['rebe_token'].count('.') == 2, r
        ok('close service', request('POST', '/v1/close/some-service', headers=bearer))




        import base64 as _b64
        import hashlib as _hashlib
        import os as _os
        import struct as _struct
        ws_sock = socket.create_connection(('127.0.0.1', http_port), timeout=5)
        try:
            _nonce = _b64.b64encode(_os.urandom(16)).decode()
            ws_sock.sendall(('GET / HTTP/1.1\r\nHost: 127.0.0.1\r\nUpgrade: websocket\r\n'
                             'Connection: Upgrade\r\n'
                             f'Sec-WebSocket-Key: {_nonce}\r\nSec-WebSocket-Version: 13\r\n\r\n').encode())
            _hs = b''
            while b'\r\n\r\n' not in _hs:
                _chunk = ws_sock.recv(4096)
                assert _chunk, 'WebSocket handshake closed prematurely'
                _hs += _chunk
            assert b' 101 ' in _hs.split(b'\r\n')[0], _hs
            _cmd = b'{"command":"session_assign","options":[],"args":{"sessionKey":"local-session-key","topicKeep":false}}'
            _mask = _os.urandom(4)
            ws_sock.sendall(bytes([0x81, 0x80 | len(_cmd)]) + _mask +
                            bytes(x ^ _mask[i % 4] for i, x in enumerate(_cmd)))
            _mask = _os.urandom(4)
            _close = _struct.pack('!H', 1000)
            ws_sock.sendall(bytes([0x88, 0x80 | len(_close)]) + _mask +
                            bytes(x ^ _mask[i % 4] for i, x in enumerate(_close)))
            _frames = []
            _saw_close = False
            _deadline = time.monotonic() + 5
            while (not _saw_close or not _frames) and time.monotonic() < _deadline:
                _head = ws_sock.recv(2)
                if not _head:
                    break
                _opcode = _head[0] & 0x0F
                _length = _head[1] & 0x7F
                if _length == 126:
                    _length = _struct.unpack('!H', ws_sock.recv(2))[0]
                elif _length == 127:
                    _length = _struct.unpack('!Q', ws_sock.recv(8))[0]
                _payload = b''
                while len(_payload) < _length:
                    _more = ws_sock.recv(_length - len(_payload))
                    assert _more, 'Server closed mid-frame'
                    _payload += _more
                if _opcode == 8:
                    _saw_close = True
                elif _opcode == 1:
                    _frames.append(_payload)
            assert _frames, 'no text frames after session_assign (expected CMD_RESPONSE ack)'
            assert any(b'"CMD_RESPONSE"' in f and b'session_assign' in f for f in _frames), _frames
            assert not any(f == _cmd for f in _frames), 'client command was echoed back'
            check.append('websocket CMD_RESPONSE ack (no command echo)')
        finally:
            ws_sock.close()


        fallback = request('GET', '/some/unknown/path?x=1')
        assert fallback == {}, fallback
        check.append('catch-all {}')



        records = [json.loads(line) for path in (folder / 'requests').glob('*.jsonl')
                   for line in path.read_text(encoding='utf-8').splitlines() if line.strip()]
        sends = [r for r in records if r.get('type') == 'webSocketSend']
        assert any('"CMD_RESPONSE"' in r['body']['data'] and 'session_assign' in r['body']['data']
                   for r in sends), sends
        check.append('websocket ack capture (webSocketSend)')
        by_path = {record.get('path'): record for record in records if record.get('type') == 'httpRequest'}
        for path, expected in (
                ('/v1/open/some-service', op),
                ('/v1/verify/some-service', ver)):
            record = by_path[path]
            assert record['responseSource'] == 'dynamic-route', record
            assert record['serverResponseCompleted'] is True, record
            if record.get('responseBodyRedacted'):
                sanitized = dict(expected, session_key='[REDACTED]')
                assert json.loads(record['responseBody']['data']) == sanitized, record
                assert expected['session_key'] not in record['responseBody']['data'], record
            else:
                assert record['responseContentLength'] == record['responseBody']['totalBytes'], record
                assert record['responseBody']['data'] == json.dumps(expected, separators=(',', ':')), record
        check.append('lifecycle response capture source/body/completion')

        summary = {'result': 'PASS', 'endpoints': check, 'artifacts': str(folder)}
        (folder / 'result.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
        print('protocol_flow: PASS')
        print(json.dumps(summary, indent=2))
    finally:
        if connection is not None:
            connection.close()
        proc.terminate()
        try:
            proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait(timeout=5)
        output.close()

if __name__ == '__main__':
    main()
