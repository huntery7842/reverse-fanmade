"""Captured stock join shape, with parser assertions recovered from Reverse.exe.

HTTP: 0x14220574a -> 0x1422007e0 (nonempty players; sessionId/key/keyword strings).
Event: 0x142255ebe -> 0x142203610 (data.data.member.players[0]).
customData1: 0x142204234 -> 0x142203350 (JSON integer version and nonce).
This checks recovered parser requirements, not native execution or playable networking.
"""
import json
from matchmaking_flow import Harness, ticket, event, offer


def join_body(account, nonce=123):
    return {'players': [{'accountId': account['id'], 'joinState': 'JOINED',
                         'customData1': json.dumps({'version': 1, 'nonce': nonce}, separators=(',', ':'))}],
            'useCrossPlay': False}


def expected_profiles(account):
    return [
        {'encryptedUserId': account['id'], 'service': 'capcom', 'nickname': account['username']},
        {'encryptedUserId': account['id'], 'service': 'steam', 'nickname': account['username']},
    ]


def validate_response(body, session, account):
    assert body.get('sessionId') == session, 'Join HTTP callback requires root sessionId'
    assert isinstance(body.get('players'), list) and body['players'], 'Join HTTP callback rejects empty/missing players'
    assert body['players'][0]['serviceProfiles'] == expected_profiles(account), \
        'serviceProfiles supplies both primary and platform display names'
    assert isinstance(body.get('serviceEncryptionKey'), str), 'Missing serviceEncryptionKey'
    assert isinstance(body.get('keyword'), str), 'Missing keyword'


def validate_member(frame, account, nonce):
    data = frame['data']['data']
    assert 'member' in data, 'Membership parser requires data.data.member.players, not data.data.players'
    player = data['member']['players'][0]
    assert player['accountId'] == account['id']
    assert isinstance(player['platform'], str)
    assert player['serviceProfiles'] == expected_profiles(account), \
        'Member event supplies both primary and platform display names'
    assert player['joinState'] == 'JOINED'
    assert type(player['joinTimestamp']) is int
    assert json.loads(player['customData1']) == {'version': 1, 'nonce': nonce}


def main():
    h = Harness()
    try:
        a, b = h.account('name-a'), h.account('name-b')
        wa, wb = h.connect(a), h.connect(b)
        h.request('POST', '/v1/matchmaking/ticket', a, ticket(a)); event(wa, 'tickets:submitted')
        h.request('POST', '/v1/matchmaking/ticket', b, ticket(b)); event(wb, 'tickets:submitted')
        session = offer(wa); assert offer(wb) == session
        headers = {'X-Be-Session-Id': session}
        result = h.request('POST', '/v1/gameSession/member/players', a, join_body(a), headers=headers)
        validate_response(result, session, a)
        validate_member(event(wa, 'players:created'), a, 123)
        validate_member(event(wb, 'players:created'), a, 123)
        assert h.request('POST', '/v1/gameSession/member/players', a, join_body(a), headers=headers) == result
        for malformed, status in ((join_body(b), 403), ({'players': []}, 400), ({}, 400),
                                  ({'players': [{'accountId': a['id'], 'joinState': 'JOINED', 'customData1': '{}'}]}, 400)):
            h.request('POST', '/v1/gameSession/member/players', a, malformed, headers=headers, expected=status)
        result_b = h.request('POST', '/v1/gameSession/member/players', b, join_body(b, 456), headers=headers)
        validate_response(result_b, session, b)
        validate_member(event(wa, 'players:created'), b, 456)
        validate_member(event(wb, 'players:created'), b, 456)
        assert len(a['id']) == len(b['id']) == 32, 'Game-facing GUIDs must fit the observed 36-character copy'


        fresh = h.request('POST', '/v1/matchmaking/ticket', a, ticket(a))
        event(wa, 'offers:failed'); event(wa, 'tickets:failed'); event(wa, 'tickets:submitted')
        event(wb, 'offers:failed'); event(wb, 'tickets:failed')
        h.request('GET', '/v1/gameSession', b, headers={'X-Be-Session-Ids': session}, expected=404)
        h.request('POST', '/v1/matchmaking/ticket', b, ticket(b)); event(wb, 'tickets:submitted')
        replacement = offer(wa); assert offer(wb) == replacement and replacement != session
        assert h.request('POST', '/v1/matchmaking/ticket', a, ticket(a)) == fresh
        for who in (a, b):
            h.request('POST', '/v1/gameSession/member/players', who, join_body(who), headers={'X-Be-Session-Id': replacement})
            event(wa, 'players:created'); event(wb, 'players:created')

        for ws in (wa, wb):
            event(ws, 'offers:failed'); event(ws, 'tickets:failed')
        h.request('GET', '/v1/gameSession', a, headers={'X-Be-Session-Ids': replacement}, expected=404)
        print('PASS stock join response, member envelope, custom-data preservation, identity width, spoof rejection, idempotency')
    finally:
        h.close()


if __name__ == '__main__':
    main()
