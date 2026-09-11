"""Black-box verification of the Re:Verse capture backend, using synthetic traffic."""
import argparse
import base64
import concurrent.futures
import datetime
import hashlib
import http.client
import ipaddress
import json
import os
from pathlib import Path
import socket
import ssl
import struct
import subprocess
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]


def port():
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        return sock.getsockname()[1]


def certificate(folder):
    from cryptography import x509
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import rsa
    from cryptography.hazmat.primitives.serialization import pkcs12
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    subject = x509.Name([x509.NameAttribute(x509.NameOID.COMMON_NAME, 'localhost')])
    now = datetime.datetime.now(datetime.timezone.utc)
    cert = (x509.CertificateBuilder().subject_name(subject).issuer_name(subject)
            .public_key(key.public_key()).serial_number(x509.random_serial_number())
            .not_valid_before(now - datetime.timedelta(minutes=1))
            .not_valid_after(now + datetime.timedelta(days=1))
            .add_extension(x509.SubjectAlternativeName([
                x509.DNSName('localhost'), x509.IPAddress(ipaddress.ip_address('127.0.0.1'))]), False)
            .add_extension(x509.BasicConstraints(ca=False, path_length=None), True)
            .add_extension(x509.ExtendedKeyUsage([x509.oid.ExtendedKeyUsageOID.SERVER_AUTH]), False)
            .add_extension(x509.KeyUsage(digital_signature=True, content_commitment=False,
                key_encipherment=True, data_encipherment=False, key_agreement=False,
                key_cert_sign=False, crl_sign=False, encipher_only=False, decipher_only=False), True)
            .sign(key, hashes.SHA256()))
    password = uuid.uuid4().hex
    pfx = folder / 'test-only.pfx'
    pfx.write_bytes(pkcs12.serialize_key_and_certificates(b'test-only', key, cert, None,
                    serialization.BestAvailableEncryption(password.encode())))
    pem = folder / 'test-only.pem'
    pem.write_bytes(cert.public_bytes(serialization.Encoding.PEM))
    return pfx, password, ssl.create_default_context(cafile=str(pem))


def frame(opcode, payload, final=True):
    mask = os.urandom(4)
    header = bytes([(0x80 if final else 0) | opcode])
    if len(payload) < 126:
        header += bytes([0x80 | len(payload)])
    else:
        header += bytes([0x80 | 126]) + struct.pack('!H', len(payload))
    return header + mask + bytes(x ^ mask[i % 4] for i, x in enumerate(payload))


def websocket(ws_port):
    with socket.create_connection(('127.0.0.1', ws_port), timeout=5) as sock:
        nonce = base64.b64encode(os.urandom(16)).decode()
        sock.sendall((f'GET /capture-test/socket HTTP/1.1\r\nHost: 127.0.0.1:{ws_port}\r\n'
                      'Upgrade: websocket\r\nConnection: Upgrade\r\n'
                      f'Sec-WebSocket-Key: {nonce}\r\nSec-WebSocket-Version: 13\r\n\r\n').encode())
        response = b''
        while b'\r\n\r\n' not in response:
            chunk = sock.recv(4096)
            assert chunk, 'WebSocket handshake closed prematurely'
            response += chunk
        assert b' 101 ' in response.split(b'\r\n')[0], response
        accept = base64.b64encode(hashlib.sha1((nonce + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').encode()).digest())
        assert accept.lower() in response.lower()
        sock.sendall(frame(1, b'capture-text'))
        sock.sendall(frame(2, b'\xff\x00\x01\xfe'))
        sock.sendall(frame(1, b'a' * 200, False) + frame(0, b'b' * 200))
        sock.sendall(frame(8, struct.pack('!H', 1000)))
        echoed = []
        close_seen = False
        deadline = time.monotonic() + 5
        while not close_seen and time.monotonic() < deadline:
            head = sock.recv(2)
            if not head:
                break
            opcode = head[0] & 0x0F
            length = head[1] & 0x7F
            if length == 126:
                length = struct.unpack('!H', sock.recv(2))[0]
            elif length == 127:
                length = struct.unpack('!Q', sock.recv(8))[0]
            payload = b''
            while len(payload) < length:
                chunk = sock.recv(length - len(payload))
                assert chunk, 'Server closed mid-frame'
                payload += chunk
            if opcode == 8:
                close_seen = True
            elif opcode == 1:
                echoed.append(payload)
            elif opcode == 2:
                echoed.append(payload)
        assert close_seen, 'Server did not finish WebSocket close handshake'
        assert b'capture-text' in echoed, echoed


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--skip-build', action='store_true')
    parser.add_argument('--http-only', action='store_true')
    parser.add_argument('--raw-headers', action='store_true')
    args = parser.parse_args()
    if not args.skip_build:
        built = subprocess.run(['dotnet', 'build', '--nologo'], cwd=ROOT / 'backend', capture_output=True, text=True)
        assert built.returncode == 0, built.stdout + built.stderr
    candidates = list((ROOT / 'backend').glob('**/bin/Debug/net10.0/*.runtimeconfig.json'))
    assert len(candidates) == 1, candidates
    dll = Path(str(candidates[0]).replace('.runtimeconfig.json', '.dll'))
    folder = ROOT / 'integration-tests' / 'artifacts' / datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    folder.mkdir(parents=True)
    http_port, https_port = port(), port()
    tls_context = None
    env = dict(os.environ)
    env.update({
        'ASPNETCORE_URLS': f'http://127.0.0.1:{http_port}',
        'Capture__LogDirectory': str(folder / 'requests'),
        'Capture__MaxCapturedBodyBytes': '256',
        'Capture__RedactSensitiveHeaders': 'false' if args.raw_headers else 'true',


        'Capture__ResponseStatusCode': '418',
        'Logging__LogLevel__Microsoft.AspNetCore.Server.Kestrel': 'Debug',
    })
    if not args.http_only:
        pfx, password, tls_context = certificate(folder)
        env.update({
            'ASPNETCORE_URLS': f'http://127.0.0.1:{http_port};https://127.0.0.1:{https_port}',
            'ASPNETCORE_Kestrel__Certificates__Default__Path': str(pfx),
            'ASPNETCORE_Kestrel__Certificates__Default__Password': password,
        })
    output = (folder / 'server-output.log').open('w', encoding='utf-8')
    proc = subprocess.Popen(['dotnet', str(dll)], cwd=ROOT / 'backend', env=env,
                            stdout=output, stderr=subprocess.STDOUT,
                            creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
    def request(method, path, body=None, headers=None, tls=False):
        connection = (http.client.HTTPSConnection('127.0.0.1', https_port, timeout=5, context=tls_context)
                      if tls else http.client.HTTPConnection('127.0.0.1', http_port, timeout=5))
        try:
            connection.request(method, path, body=body, headers=headers or {})
            response = connection.getresponse()
            payload = response.read()
            assert response.status == 418, (method, path, response.status, payload)
            assert payload == (b'' if method == 'HEAD' else b'{}'), (method, payload)
        finally:
            connection.close()
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
        verbs = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS', 'TRACE', 'PROPFIND', 'CUSTOM']
        for verb in verbs:
            request(verb, '/capture-test/verbs/' + verb)
        request('POST', '/capture-test/json?exact=a%2Fb&repeat=1&repeat=2', b'{"hello":"world"}', {
            'Content-Type': 'application/json', 'Authorization': 'Bearer synthetic-secret',
            'X-Auth-Session-Key': 'synthetic-session-secret', 'X-Capture-Test': 'preserved'})
        request('POST', '/capture-test/binary', b'\xff\x00\x01\xfe', {'Content-Type': 'application/octet-stream'})
        request('POST', '/capture-test/large', b'x' * 4096)
        request('POST', '/capture-test/chunked', iter([b'first', b'second']))
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
            list(executor.map(lambda i: request('POST', f'/capture-test/concurrent/{i}', f'body-{i}'.encode()), range(16)))
        if not args.http_only:
            request('GET', '/capture-test/tls', tls=True)
        websocket(http_port)
        time.sleep(0.5)
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait(timeout=5)
        output.close()
    records = [json.loads(line) for path in (folder / 'requests').glob('*.jsonl')
               for line in path.read_text(encoding='utf-8').splitlines() if line.strip()]
    http_records = {r['path']: r for r in records if r.get('type') == 'httpRequest'}
    for verb in verbs:
        assert http_records['/capture-test/verbs/' + verb]['method'] == verb
    record = http_records['/capture-test/json']
    assert record['rawQuery'] == '?exact=a%2Fb&repeat=1&repeat=2', record
    assert record['body']['data'] == '{"hello":"world"}', record
    headers = {k.lower(): v for k,v in record['headers'].items()}
    assert ('synthetic-secret' in str(headers['authorization'])) == args.raw_headers, headers
    assert ('synthetic-session-secret' in str(headers['x-auth-session-key'])) == args.raw_headers, headers
    assert 'preserved' in str(headers['x-capture-test']), headers
    binary = http_records['/capture-test/binary']['body']
    assert binary['encoding'] == 'base64' and base64.b64decode(binary['data']) == b'\xff\x00\x01\xfe', binary
    large = http_records['/capture-test/large']['body']
    assert large['capturedBytes'] == 256 and large['totalBytes'] == 4096 and large['truncated'], large
    assert http_records['/capture-test/chunked']['body']['data'] == 'firstsecond'
    for i in range(16):
        assert http_records[f'/capture-test/concurrent/{i}']['body']['data'] == f'body-{i}'
    if not args.http_only:
        assert '/capture-test/tls' in http_records
    ws = [r for r in records if r.get('type') == 'webSocketReceive']
    assert any(r['body']['data'] == 'capture-text' for r in ws), ws
    assert any(r['body']['encoding'] == 'base64' and base64.b64decode(r['body']['data']) == b'\xff\x00\x01\xfe' for r in ws), ws
    fragmented = [r for r in ws if r['messageIndex'] == 2]
    assert fragmented[-1]['endOfMessage'], fragmented
    assert fragmented[-1]['messageBytesReceived'] == 400 and fragmented[-1]['messageCapturedBytes'] == 256, fragmented
    retained = b''.join(r['body']['data'].encode() if r['body']['encoding'] == 'utf8'
                        else base64.b64decode(r['body']['data']) for r in fragmented)
    assert retained == b'a' * 200 + b'b' * 56, fragmented
    assert any(r['body']['truncated'] for r in fragmented), fragmented
    assert any(r.get('type') == 'webSocketClose' for r in records), records[-5:]
    summary = {'result': 'PASS', 'httpMethods': verbs, 'httpRequestCount': len(http_records), 'webSocketReceiveRecords': len(ws),
               'checks': ['arbitrary routes', 'raw query', 'JSON', 'binary', 'chunked transfer',
                          'raw headers' if args.raw_headers else 'header redaction',
                          'capture truncation', '16 concurrent requests', 'WebSocket text/binary/fragmentation/close']
                         + ([] if args.http_only else ['verified HTTPS']),
               'httpsTested': not args.http_only,
               'artifacts': str(folder)}
    (folder / 'result.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    main()
