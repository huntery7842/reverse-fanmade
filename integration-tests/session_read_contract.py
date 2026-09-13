"""Recovered session GET contract; synthetic HTTP, not native gameplay validation."""
import json
from urllib.parse import urlencode
from matchmaking_flow import Harness, ticket, event
from game_join_contract import join_body

FIELDS = ('sessionId,createdTimestamp,maxPlayers,maxSpectators,member(players),member(spectators),'
          'member(players(joinState)),member(spectators(joinState)),member(players(customData1)),'
          'member(spectators(customData1)),serviceProfiles,serviceEncryptionKey,joinDisabled,'
          'supportedServices,representative,customData1,customData2,usePlayerSession,useCrossPlay,matchmaking,signaling')


def main():
    h = Harness()
    try:
        a, b, outsider = [h.account() for _ in range(3)]
        wa, wb = h.connect(a), h.connect(b)
        for who, ws in ((a, wa), (b, wb)):
            search = ticket(who)
            search['useCrossPlay'] = True
            h.request('POST', '/v1/matchmaking/ticket', who, search)
            event(ws, 'tickets:submitted')
        oa = event(wa, 'offers:created')['data']['data']['offers'][0]
        ob = event(wb, 'offers:created')['data']['data']['offers'][0]
        assert oa == ob
        sid = oa['location']['gameSessionId']
        headers = {'X-Be-Session-Ids': sid}
        query = urlencode({'fields': FIELDS, 'joinStateFilter': 'RESERVED,JOINED', 'usePlayerSessionFilter': 'false'})
        path = '/v1/gameSession?' + query

        def read(who=a, target=path, expected=200, hdr=headers):
            return h.request('GET', target, who, headers=hdr, expected=expected)


        first = read()['gameSessions'][0]
        assert first['sessionId'] == sid and first['gameSessionSequenceNo'] == 1
        assert first['representative'] == {'accountId': a['id']}
        assert first['signaling'] == 'NONE'
        assert first['matchmaking']['offerId'] == oa['offerId']
        assert first['usePlayerSession'] is False and first['useCrossPlay'] is True
        assert first['supportedService'] == ['steam']
        assert first['maxPlayers'] == 2 and first['maxSpectators'] == 0
        assert first['member']['spectators'] == []
        assert {p['accountId'] for p in first['member']['players']} == {a['id'], b['id']}
        assert all(p['joinState'] == 'RESERVED' and p['customData1'] == '' for p in first['member']['players'])
        assert read()['gameSessions'][0] == first
        for who, nonce in ((a, 123), (b, 456)):
            admission = join_body(who, nonce)
            admission['useCrossPlay'] = True
            reply = h.request('POST', '/v1/gameSession/member/players', who, admission,
                              headers={'X-Be-Session-Id': sid})
            event(wa, 'players:created'); event(wb, 'players:created')
            current = read()['gameSessions'][0]
            assert current['serviceEncryptionKey'] == reply['serviceEncryptionKey']
            members = {p['accountId']: p for p in current['member']['players']}
            assert members[who['id']]['customData1'] == join_body(who, nonce)['players'][0]['customData1']
            assert members[who['id']]['serviceProfiles'] == [
                {'encryptedUserId': who['id'], 'service': 'steam', 'nickname': who['username']}]
            if who is a:
                assert members[b['id']]['joinState'] == 'RESERVED'
        assert current['gameSessionSequenceNo'] == 3 and current['signaling'] == 'NONE'
        assert read(b)['gameSessions'][0] == current
        assert read(target='/v1/gameSession')['gameSessions'][0] == current
        assert read(target='/v1/gameSession?fields=')['gameSessions'][0] == current
        assert len(read(hdr={'X-Be-Session-Ids': sid + ',' + sid})['gameSessions']) == 1
        assert read(target='/v1/gameSession?fields=%40default')['gameSessions'][0] == current
        assert read(target='/v1/gameSession?joinStateFilter=RESERVED')['gameSessions'][0]['member']['players'] == []
        assert len(read(target='/v1/gameSession?joinStateFilter=JOINED')['gameSessions'][0]['member']['players']) == 2
        partial = read(target='/v1/gameSession?fields=sessionId')['gameSessions'][0]
        assert partial['sessionId'] == sid and 'gameSessionSequenceNo' in partial
        assert 'customData2' not in partial and 'representative' in partial
        read(outsider, expected=404)
        read(outsider, target='/v1/gameSession?usePlayerSessionFilter=true', expected=404)
        read(target='/v1/gameSession?usePlayerSessionFilter=true', expected=404)
        read(hdr={}, expected=400)
        read(hdr={'X-Be-Session-Ids': ','.join([sid] * 11)}, expected=400)
        read(hdr={**headers, 'X-Auth-Session-Key': outsider['gameKey']}, expected=401)
        read(hdr={'X-Be-Session-Ids': sid + ',missing'}, expected=404)
        for suffix in ('joinStateFilter=BAD', 'joinStateFilter=', 'joinStateFilter=JOINED,,RESERVED',
                       'usePlayerSessionFilter=0', 'usePlayerSessionFilter=', 'fields=unknown',
                       'fields=member%2528players%2529', 'fields=member(players',
                       'fields=sessionId&fields=maxPlayers', 'joinStateFilter=JOINED&joinStateFilter=RESERVED',
                       'usePlayerSessionFilter=false&usePlayerSessionFilter=true', 'fields=' + 'x' * 4097):
            read(target='/v1/gameSession?' + suffix, expected=400)

        read(target='/v1/gameSession?token=never-log-this-query-token', expected=400)
        assert read()['gameSessions'][0] == current

        for ws in (wa, wb):
            event(ws, 'offers:failed'); event(ws, 'tickets:failed')
        read(expected=404)
        records = [json.loads(line) for file in h.folder.glob('requests-*.jsonl') for line in file.read_text().splitlines()]
        reads = [r for r in records if r.get('path') == '/v1/gameSession' and r.get('method') == 'GET']
        assert any(r.get('sessionRead', {}).get('query', {}).get('joinStateFilter') == 'RESERVED,JOINED' for r in reads)
        assert any(r.get('sessionRead', {}).get('sessionIds') == [sid] for r in reads)
        assert 'never-log-this-query-token' not in json.dumps(records)
        print('PASS session-read filters, native shapes, reserved/joined members, projection, authorization, expiry and bounded audit')
    finally:
        h.close()


if __name__ == '__main__':
    main()
