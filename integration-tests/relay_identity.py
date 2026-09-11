"""Account match-or-create, SQLite storage, and game session binding regression."""
import concurrent.futures
import hashlib
import http.client
import json
import os
from pathlib import Path
import socket
import sqlite3
import subprocess
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]

def main():
    subprocess.run(['dotnet', 'build', '-c', 'Debug', '--nologo', '--no-restore'], cwd=ROOT / 'backend', check=True)
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        port = sock.getsockname()[1]
    folder = ROOT / 'integration-tests' / 'artifacts' / ('accounts-' + uuid.uuid4().hex)
    folder.mkdir(parents=True)
    database = folder / 'accounts.db'
    env = dict(os.environ, Relay__Enabled='true', Steam__Mode='fallback', Relay__DatabasePath=str(database),
               ASPNETCORE_URLS=f'http://127.0.0.1:{port}', Capture__LogDirectory=str(folder))
    output = (folder / 'backend.log').open('w')
    def start():
        return subprocess.Popen(['dotnet', str(ROOT / 'backend/bin/Debug/net10.0/ReVerse.Capture.dll')],
                                cwd=ROOT / 'backend', env=env, stdout=output, stderr=output,
                                creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
    def request(method, path, headers=None, body=None):
        connection = http.client.HTTPConnection('127.0.0.1', port, timeout=30)
        try:
            connection.request(method, path, body=body, headers=headers or {})
            response = connection.getresponse()
            payload = response.read()
            return response.status, json.loads(payload) if payload else {}
        finally: connection.close()
    def ready():
        deadline = time.monotonic() + 20
        while True:
            try:
                assert request('GET', '/ready')[0] == 401
                return
            except OSError:
                assert process.poll() is None
                if time.monotonic() >= deadline: raise
                time.sleep(0.1)
    def login(name, secret):
        status, body = request('POST', '/relay/account/login', {'Content-Type': 'application/json'},
                               json.dumps({'username': name, 'secretKey': secret}))
        assert status == 200, (status, body)
        return body
    def headers(account): return {'X-Relay-Session-Token': account['sessionToken']}
    process = start()
    secret_a, secret_b = 'test-secret-A-' + uuid.uuid4().hex, 'test-secret-B-' + uuid.uuid4().hex
    try:
        ready()
        first = login('Alex', secret_a)
        second = login('Alex', secret_b)
        again = login('Alex', secret_a)
        assert first['created'] and second['created'] and not again['created']
        assert first['accountId'] == again['accountId'] != second['accountId']
        assert login('Other', secret_a)['accountId'] != first['accountId']
        unicode_account = login('Боб 🎮', ' secret with spaces ')
        assert login('Боб 🎮', ' secret with spaces ')['accountId'] == unicode_account['accountId']
        with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
            parallel = list(pool.map(lambda _: login('Concurrent', secret_b), range(3)))
        assert len({a['accountId'] for a in parallel}) == 1 and sum(a['created'] for a in parallel) == 1
        tokens = []
        for account in [first, second, unicode_account]:
            status, body = request('POST', '/v1/steam-steam/sign/RVS-B-WW', headers(account))
            assert status == 200, body
            tokens.append(body['rebe_token'])
        assert len(set(tokens)) == 3
        for account, token in zip([first, second, unicode_account], tokens):
            session = dict(headers(account), Authorization='Bearer ' + token)
            status, body = request('GET', '/v1/verify/test', session)
            assert status == 200 and body['id_token'] == {
                'sub': account['accountId'], 'sub_nickname': account['username']}, body
        assert request('GET', '/v1/verify/test', dict(headers(first), Authorization='Bearer ' + tokens[1]))[0] == 401
        assert request('POST', '/v1/steam-steam/sign/RVS-B-WW', {'X-Relay-Player-Id': first['accountId']})[0] == 401
        assert request('GET', '/ready', {'X-Relay-Session-Token': 'F' * 64})[0] == 401
        assert request('POST', '/relay/account/login', {'Content-Type': 'application/json'}, '{')[0] == 400
        assert request('POST', '/relay/account/login', {'Content-Type': 'application/json'}, '{"username":"Alex","secretKey":""}')[0] == 400
        assert request('POST', '/relay/account/login', {'Content-Type': 'application/json'}, 'x' * 9000)[0] == 413
        with sqlite3.connect(database) as db:
            rows = db.execute('SELECT salt, hash, algorithm, iterations FROM accounts WHERE username=?', ('Alex',)).fetchall()
            assert len(rows) == 2 and rows[0][0] != rows[1][0]
            for salt, hashed, algorithm, iterations in rows:
                assert len(salt) == len(hashed) == 32 and algorithm == 'PBKDF2-SHA256' and iterations >= 600000
                assert any(hashlib.pbkdf2_hmac('sha256', s.encode(), salt, iterations) == hashed for s in (secret_a, secret_b))
            db.execute('UPDATE relay_sessions SET expires_at=0 WHERE token_hash=?', (hashlib.sha256(second['sessionToken'].encode()).digest(),))
        assert request('GET', '/ready', headers(second))[0] == 401
        process.terminate(); process.wait(timeout=10)
        process = start(); ready()
        assert login('Alex', secret_a)['accountId'] == first['accountId']
        assert request('POST', '/v1/steam-steam/sign/RVS-B-WW', headers(first))[0] == 200
        for path in folder.iterdir():
            if path.is_file():
                data = path.read_bytes()
                assert secret_a.encode() not in data and secret_b.encode() not in data, path
                assert first['sessionToken'].encode() not in data, path
        print('PASS duplicate usernames, match-or-create, concurrency, Unicode, salted hashes, persistence, expiry, session isolation, no credential capture')
    finally:
        process.terminate()
        try: process.wait(timeout=10)
        except subprocess.TimeoutExpired: process.kill(); process.wait()
        output.close()

if __name__ == '__main__': main()
