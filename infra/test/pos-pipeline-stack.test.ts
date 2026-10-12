import * as cdk from 'aws-cdk-lib';
import { Template, Match } from 'aws-cdk-lib/assertions';
import { PosPipelineStack } from '../lib/pos-pipeline-stack';
test('full pipeline provisions secure storage, queues, Lambdas and API', () => {
  const app = new cdk.App();
  const stack = new PosPipelineStack(app, 'TestStack', { environmentName: 'test' });
  const template = Template.fromStack(stack);
  template.resourceCountIs('AWS::DynamoDB::Table', 2);
  template.resourceCountIs('AWS::SQS::Queue', 3);
  template.resourceCountIs('AWS::S3::Bucket', 1);
  // CDK also synthesizes helper Lambdas for bucket auto-delete and notifications.
  expect(Object.keys(template.findResources('AWS::Lambda::Function')).length).toBeGreaterThanOrEqual(4);
  template.resourceCountIs('AWS::ApiGateway::RestApi', 1);
  template.hasResourceProperties('AWS::DynamoDB::Table', {
    KeySchema: Match.arrayWith([{ AttributeName: 'flightId', KeyType: 'HASH' }, { AttributeName: 'timestamp', KeyType: 'RANGE' }])
  });
  template.hasResourceProperties('AWS::S3::Bucket', { PublicAccessBlockConfiguration: {
    BlockPublicAcls: true, BlockPublicPolicy: true, IgnorePublicAcls: true, RestrictPublicBuckets: true
  }});
});
