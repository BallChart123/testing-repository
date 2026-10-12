#!/usr/bin/env python3
"""Smoke-test a deployed POS pipeline using only Python standard library.
Usage: python scripts/e2e_smoke.py https://...execute-api.../prod/ [timeout_seconds]
"""
import json
import sys
import time
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

if len(sys.argv) < 2:
    raise SystemExit('Usage: python scripts/e2e_smoke.py <ApiBaseUrl> [timeout_seconds]')
base = sys.argv[1].rstrip('/')
timeout = int(sys.argv[2]) if len(sys.argv) > 2 else 90
raw = 'POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800'

def call(method, path, body=None):
    req = Request(base + path, data=body, method=method,
                  headers={'Content-Type': 'text/plain' if body is not None else 'application/json'})
    try:
        with urlopen(req, timeout=20) as res:
            return res.status, json.loads(res.read())
    except HTTPError as e:
        return e.code, json.loads(e.read())

code, receipt = call('POST', '/pos-reports', raw.encode('utf-8'))
assert code == 202, f'POST expected 202, got {code}: {receipt}'
flight_id = receipt.get('flightId')
assert receipt.get('status') == 'RECEIVED' and flight_id, receipt
print('POST accepted:', flight_id)
end = time.monotonic() + timeout
while time.monotonic() < end:
    code, result = call('GET', '/status/' + flight_id)
    if code == 200 and 'remainingFlightTimeMinutes' in result:
        assert result['flightId'] == flight_id, result
        assert isinstance(result['lowFuelWarning'], bool), result
        assert isinstance(result['estimatedFuelAtArrivalKg'], (int, float)), result
        print('E2E PASS:', json.dumps(result, indent=2))
        break
    if code not in (200, 404):
        raise RuntimeError(f'Unexpected status {code}: {result}')
    print('Waiting for async pipeline...', result)
    time.sleep(3)
else:
    raise SystemExit('E2E FAILED: no completed result before timeout. Check Lambda logs and SQS DLQs.')

code, invalid = call('POST', '/pos-reports', b'INVALID')
assert code == 400, f'Expected 400 for invalid report, got {code}: {invalid}'
print('Validation PASS: malformed POS returns HTTP 400')
