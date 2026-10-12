#!/usr/bin/env python3
"""Synthetic-only deployed smoke test. No passenger, employee or real flight data.
Usage: python scripts/e2e_smoke.py <ApiBaseUrl> [timeout_seconds]
Use only an authorized, isolated assessment/dev AWS stack.
"""
import json
import sys
import time
from datetime import datetime, timezone
from urllib.error import HTTPError
from urllib.parse import urlparse
from urllib.request import Request, urlopen

if len(sys.argv) < 2:
    raise SystemExit('Usage: python scripts/e2e_smoke.py <ApiBaseUrl> [timeout_seconds]')
base = sys.argv[1].rstrip('/')
parsed_url = urlparse(base)
if parsed_url.scheme != 'https' or not parsed_url.netloc:
    raise SystemExit('Refusing to send test data to a non-HTTPS URL')
timeout = int(sys.argv[2]) if len(sys.argv) > 2 else 90
# ZZ999 is an explicitly fictional flight identifier; RGN/BKK are public airport codes.
# Date is dynamically generated because the application infers year/month from receipt time.
now = datetime.now(timezone.utc)
raw = f'POS/ZZ999.FR RGN/TO BKK/{now:%d%H%M}/N1642.3E09612.5/450/12500/2800'

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
assert receipt.get('status') == 'RECEIVED' and flight_id == f'ZZ999{now:%Y%m%d}RGNBKK', receipt
print('Synthetic POST accepted:', flight_id)
end = time.monotonic() + timeout
while time.monotonic() < end:
    code, result = call('GET', '/status/' + flight_id)
    if code == 200 and 'remainingFlightTimeMinutes' in result:
        assert result['flightId'] == flight_id, result
        assert isinstance(result['lowFuelWarning'], bool), result
        assert isinstance(result['estimatedFuelAtArrivalKg'], (int, float)), result
        inputs = result.get('input')
        assert isinstance(inputs, dict), 'Missing self-contained calculation input'
        assert inputs['destination'] == 'BKK'
        assert inputs['groundSpeedKnots'] == 450
        assert inputs['fuelOnBoardKg'] == 12500
        assert inputs['fuelFlowKgPerHour'] == 2800
        assert abs(inputs['currentLatitude'] - 16.705) < 0.001
        assert abs(inputs['currentLongitude'] - 96.208333333) < 0.001
        print('Synthetic E2E PASS:', json.dumps(result, indent=2))
        break
    if code not in (200, 404):
        raise RuntimeError(f'Unexpected status {code}: {result}')
    print('Waiting for async pipeline...', result)
    time.sleep(3)
else:
    raise SystemExit('E2E FAILED: no completed result before timeout. Check Lambda logs and SQS DLQs.')

code, invalid = call('POST', '/pos-reports', b'INVALID-SYNTHETIC-TEST-DATA')
assert code == 400, f'Expected 400 for invalid report, got {code}: {invalid}'
print('Validation PASS: synthetic malformed POS returns HTTP 400')
