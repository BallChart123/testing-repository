#!/usr/bin/env python3
"""Dependency-free structural preflight; does NOT replace dotnet build, CDK synth or AWS E2E."""
from pathlib import Path
import re
root = Path(__file__).resolve().parents[1]
required = [
 'PosReports.sln', 'README.md', 'infra/package.json', 'infra/lib/pos-pipeline-stack.ts',
 'src/PosReports.Domain/Parsing/PosReportParser.cs',
 'src/PosReports.Domain/Parsing/CoordinateConverter.cs',
 'src/PosReports.Domain/Calculations/FlightCalculator.cs',
 'src/PosReports.Domain/Airports/InMemoryAirportCatalog.cs',
 'src/PosReports.Application/Pipeline.cs',
 'src/PosReports.Infrastructure/AwsAdapters.cs',
 'src/PosReports.Functions/Handlers.cs',
 'tests/PosReports.Domain.Tests/ParserTests.cs',
 'tests/PosReports.Domain.Tests/CalculatorTests.cs',
 'tests/PosReports.Application.Tests/PipelineTests.cs',
]
missing = [p for p in required if not (root / p).is_file()]
assert not missing, f'Missing files: {missing}'
stack = (root / 'infra/lib/pos-pipeline-stack.ts').read_text()
for marker in ('OBJECT_CREATED', "prefix: 'pos/'", 'SqsEventSource', 'CalculatorDLQ',
               'ParserFailureQueue', 'ReceiverFunction', 'ParserFunction',
               'CalculatorFunction', 'StatusFunction', 'ParsedReports', 'CalculationResults'):
    assert marker in stack, f'Missing CDK marker: {marker}'
app = (root / 'src/PosReports.Application/Pipeline.cs').read_text()
for marker in ('ReceivePosReport', 'ProcessPosReport', 'CalculateFlight', 'GetFlightStatus',
               'PutIfAbsentAsync', 'HasSourceAsync'):
    assert marker in app, f'Missing application marker: {marker}'
print(f'STRUCTURAL PREFLIGHT PASS: {len(required)} required files and key architecture markers')
print('NOT VERIFIED: C# compilation, .NET tests, npm/CDK build, CDK synth, AWS deployment')
