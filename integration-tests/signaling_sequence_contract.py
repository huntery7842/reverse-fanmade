"""Native-derived session revision gate, exercised over real HTTP/WebSockets.

Uses isolated backend/ports/database; never connects to the field-test service.
Models 0x142255966..0x142255a12, not native execution or playable multiplayer.
"""
from matchmaking_flow import ticket, event, offer
from game_join_contract import join_body
from signaling_provider_contract import ProviderHarness, free_udp_port, quiet_barrier


def check(delayed):
    port = free_udp_port()
    h = ProviderHarness(extra_env={
        'Signaling__Enabled': 'true', 'Signaling__BindAddress': '127.0.0.1',
        'Signaling__Port': str(port), 'Signaling__PublicHost': '127.0.0.1',
        'Signaling__PublicPort': str(port),
        'Matchmaking__TicketLifetimeSeconds': '60', 'Matchmaking__JoinLifetimeSeconds': '60',
        'Logging__EventLog__LogLevel__Default': 'None',
    })
    try:
        accounts = [h.account(), h.account()]
        sockets = [h.connect(a) for a in accounts]
        for a, ws in zip(accounts, sockets):
            h.request('POST', '/v1/matchmaking/ticket', a, ticket(a))
            event(ws, 'tickets:submitted')
        sid = offer(sockets[0])
        assert offer(sockets[1]) == sid
        headers = {'X-Be-Session-Id': sid}
        current = [1, 1]

        def receive(suffix):
            for i, ws in enumerate(sockets):
                payload = event(ws, suffix)['data']['data']
                assert payload['sessionId'] == sid
                revision = payload['gameSessionSequenceNo']
                assert revision == current[i] + 1, (
                    f'Native session gate blocks {suffix}: current={current[i]}, incoming={revision}')
                current[i] = revision

        def read(a):
            return h.request('GET', '/v1/gameSession', a,
                             headers={'X-Be-Session-Ids': sid})['gameSessions'][0]

        def start():
            h.request('PATCH', '/v1/gameSession/signaling', accounts[0],
                      {'signalingTimeoutSeconds': 60}, headers=headers)

        for i, a in enumerate(accounts):
            body = join_body(a, i + 100)
            h.request('POST', '/v1/gameSession/member/players', a, body, headers=headers)
            receive('players:created')

            h.request('POST', '/v1/gameSession/member/players', a, body, headers=headers)
            if delayed and i == 0:
                start()
                start()
                assert read(a)['signaling'] == 'IN_PROGRESS'
                for member, ws in zip(accounts, sockets):
                    quiet_barrier(ws, member)
        if not delayed:

            for a in accounts:
                assert read(a)['gameSessionSequenceNo'] == 3
            start()
        receive('signaling:created')
        for i, a in enumerate(accounts):
            assert read(a)['gameSessionSequenceNo'] == current[i] == 4
        start()
        for a, ws in zip(accounts, sockets):
            h.request('GET', '/v1/gameSession/signaling', a, headers=headers)
            quiet_barrier(ws, a)
            assert read(a)['gameSessionSequenceNo'] == 4
        print('PASS contiguous session events, both recipients, retries:', 'delayed' if delayed else 'immediate')
    finally:
        h.close()


if __name__ == '__main__':
    check(False)
    check(True)
