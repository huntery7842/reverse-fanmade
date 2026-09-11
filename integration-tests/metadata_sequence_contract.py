"""Real HTTP/WS metadata revisions; native acceptance still needs field testing."""
from matchmaking_flow import Harness, ticket, event, offer
from game_join_contract import join_body
from signaling_provider_contract import quiet_barrier


def main():
    h = Harness(extra_env={'Matchmaking__TicketLifetimeSeconds': '60',
                          'Matchmaking__JoinLifetimeSeconds': '60',
                          'Logging__EventLog__LogLevel__Default': 'None'})
    try:
        a, b, outsider = [h.account() for _ in range(3)]
        sockets = [h.connect(a), h.connect(b)]
        for account, ws in zip((a, b), sockets):
            h.request('POST', '/v1/matchmaking/ticket', account, ticket(account))
            event(ws, 'tickets:submitted')
        sid = offer(sockets[0])
        assert offer(sockets[1]) == sid
        headers = {'X-Be-Session-Id': sid}
        for i, account in enumerate((a, b)):
            h.request('POST', '/v1/gameSession/member/players', account, join_body(account, i + 100), headers=headers)
            for ws in sockets:
                event(ws, 'players:created')
        revision = 3

        def snapshots():
            return [h.request('GET', '/v1/gameSession', account,
                              headers={'X-Be-Session-Ids': sid})['gameSessions'][0] for account in (a, b)]

        def quiet():
            for account, ws in zip((a, b), sockets):
                quiet_barrier(ws, account)
            assert all(s['gameSessionSequenceNo'] == revision for s in snapshots())

        def patch(path, body, suffix=None, account=a, expected=200, extra=None):
            nonlocal revision
            before = snapshots()
            h.request('PATCH', path, account, body, headers={**headers, **(extra or {})}, expected=expected)
            if suffix:
                revision += 1
                for ws in sockets:
                    payload = event(ws, suffix)['data']['data']
                    assert payload['sessionId'] == sid and payload['gameSessionSequenceNo'] == revision
                    if suffix == 'operateSequenceNo':
                        assert set(payload) == {'sessionId', 'gameSessionSequenceNo', 'skip'}
                        assert type(payload['skip']) is int and payload['skip'] == 0
            else:
                assert snapshots() == before, 'No-op/rejected PATCH changed state or revision'
            quiet()

        room, member = '/v1/gameSession', '/v1/gameSession/members'
        patch(room, {'customData1': 'room'}, 'operateSequenceNo')
        assert snapshots()[0]['customData1'] == 'room'
        patch(member, {'customData2': 'player'}, 'operateSequenceNo', account=b)
        assert next(p for p in snapshots()[0]['member']['players'] if p['accountId'] == b['id'])['customData2'] == 'player'
        for path, body, who in ((room, {}, a), (room, {'customData1': 'room'}, a),
                                (member, {}, b), (member, {'customData2': 'player'}, b),
                                (member, {'customData1': join_body(b, 101)['players'][0]['customData1']}, b)):
            patch(path, body, account=who)
        patch(room, {'gameSession': {'joinDisabled': True, 'customData2': 'mixed'}}, 'joinDisabled:updated')
        patch(room, {'joinDisabled': True, 'customData2': 'mixed'})
        patch(room, {'joinDisabled': True, 'customData2': 'changed'}, 'operateSequenceNo')
        for path in (room, member):
            for skip in (-1, 0, 1, True):
                patch(path, {'skip': skip, 'customData2': 'must-not-apply'}, expected=400)
            patch(path, {'customData2': 42}, expected=400)
            patch(path, {'customData2': 'forbidden'}, account=outsider, expected=404)
        patch(room, {'customData2': 'forbidden'}, account=b, expected=403)
        patch(room, {'gameSession': {'customData2': 'forbidden'}, 'skip': -1}, expected=400)
        patch(room, {'gameSession': {'customData2': 'forbidden', 'skip': 0}}, expected=400)
        patch(member, {'customData2': 'forbidden'}, account=b, expected=403, extra={'X-Be-Account-Id': a['id']})
        patch(member, {'customData1': join_body(b, 999)['players'][0]['customData1'], 'customData2': 'forbidden'}, account=b, expected=409)
        h.request('POST', room + '/sessionMessage', a, {'content': 'next'}, headers=headers)
        revision += 1
        for ws in sockets:
            assert event(ws, 'sessionMessage:created')['data']['data']['gameSessionSequenceNo'] == revision
        quiet()
        print('PASS metadata events for both recipients, no-ops, mixed PATCH, zero skip, rejection atomicity, next revision')
    finally:
        h.close()


if __name__ == '__main__':
    main()
