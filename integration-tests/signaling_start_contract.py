"""Captured signaling-start request; tests control-plane acceptance, not peer transport."""
import time
from matchmaking_flow import Harness, ticket, event, offer, join_body


def main():
    h = Harness()
    try:
        a, b, outsider = [h.account() for _ in range(3)]
        wa, wb = h.connect(a), h.connect(b)
        for who, ws in ((a, wa), (b, wb)):
            h.request('POST', '/v1/matchmaking/ticket', who, ticket(who)); event(ws, 'tickets:submitted')
        sid = offer(wa); assert offer(wb) == sid
        headers = {'X-Be-Session-Id': sid}
        reads = {'X-Be-Session-Ids': sid}
        path = '/v1/gameSession/signaling'
        h.request('PATCH', path, a, {'signalingTimeoutSeconds': 60}, headers=headers, expected=403)
        for who in (a, b):
            h.request('POST', '/v1/gameSession/member/players', who, join_body(who), headers=headers)
            event(wa, 'players:created'); event(wb, 'players:created')

        assert h.request('PATCH', path, a, {'signalingTimeoutSeconds': 60}, headers=headers) == {}
        state = h.request('GET', '/v1/gameSession', a, headers=reads)['gameSessions'][0]
        assert state['signaling'] == 'IN_PROGRESS'
        assert state['gameSessionSequenceNo'] == 3
        assert h.request('PATCH', path, a, {'signalingTimeoutSeconds': 60}, headers=headers) == {}
        assert h.request('GET', '/v1/gameSession', a, headers=reads)['gameSessions'][0] == state
        h.request('PATCH', path, b, {'signalingTimeoutSeconds': 60}, headers=headers, expected=403)
        h.request('PATCH', path, outsider, {'signalingTimeoutSeconds': 60}, headers=headers, expected=404)
        h.request('PATCH', path, a, {'signalingTimeoutSeconds': 1}, headers=headers, expected=409)
        for body in ({}, {'signalingTimeoutSeconds': 0}, {'signalingTimeoutSeconds': 601},
                     {'signalingTimeoutSeconds': '60'}, {'signalingTimeoutSeconds': True},
                     {'signalingTimeoutSeconds': 1.5}, {'signaling': {'endpoints': []}},
                     {'signalingTimeoutSeconds': 60, 'signaling': {}}):
            h.request('PATCH', path, a, body, headers=headers, expected=400)
        h.request('GET', path, a, headers=headers, expected=503)


        for ws in (wa, wb):
            event(ws, 'offers:failed'); event(ws, 'tickets:failed')
        h.request('GET', '/v1/gameSession', a, headers=reads, expected=404)

        for who, ws in ((a, wa), (b, wb)):
            h.request('POST', '/v1/matchmaking/ticket', who, ticket(who)); event(ws, 'tickets:submitted')
        sid = offer(wa); assert offer(wb) == sid
        headers = {'X-Be-Session-Id': sid}
        h.request('POST', '/v1/gameSession/member/players', a, join_body(a), headers=headers)
        event(wa, 'players:created'); event(wb, 'players:created')
        started = time.monotonic()
        h.request('PATCH', path, a, {'signalingTimeoutSeconds': 1}, headers=headers)
        for ws in (wa, wb):
            event(ws, 'offers:failed'); event(ws, 'tickets:failed')
        assert time.monotonic() - started < 2.5, 'Requested 1s timeout was ignored'
        print('PASS captured signaling-start acceptance, pending state, idempotency, authorization, validation and bounded timeout')
    finally:
        h.close()


if __name__ == '__main__':
    main()
