import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as s3n from 'aws-cdk-lib/aws-s3-notifications';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as sqs from 'aws-cdk-lib/aws-sqs';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as sources from 'aws-cdk-lib/aws-lambda-event-sources';
import * as destinations from 'aws-cdk-lib/aws-lambda-destinations';
import * as apigw from 'aws-cdk-lib/aws-apigateway';
import * as path from 'path';
import { Duration, RemovalPolicy } from 'aws-cdk-lib';

export interface PosPipelineStackProps extends cdk.StackProps { readonly environmentName: string; }
export class PosPipelineStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props: PosPipelineStackProps) {
    super(scope, id, props);
    const bucket = new s3.Bucket(this, 'ReportsBucket', {
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL, enforceSSL: true,
      encryption: s3.BucketEncryption.S3_MANAGED,
      removalPolicy: RemovalPolicy.DESTROY, autoDeleteObjects: true // Assessment/dev only
    });
    const parsed = this.table('ParsedReports');
    const results = this.table('CalculationResults');
    const dlq = new sqs.Queue(this, 'CalculatorDLQ', { retentionPeriod: Duration.days(14), enforceSSL: true });
    const queue = new sqs.Queue(this, 'CalculationQueue', {
      visibilityTimeout: Duration.seconds(180),
      deadLetterQueue: { queue: dlq, maxReceiveCount: 5 }, enforceSSL: true
    });
    const parserFailureQueue = new sqs.Queue(this, 'ParserFailureQueue', { retentionPeriod: Duration.days(14), enforceSSL: true });
const artifact = path.resolve(__dirname, '../../../artifacts/lambda');
    const fn = (name: string, handler: string, timeout: number, environment: Record<string,string>) =>
      new lambda.Function(this, name, {
        runtime: lambda.Runtime.DOTNET_8, architecture: lambda.Architecture.X86_64,
        code: lambda.Code.fromAsset(artifact), handler: `PosReports.Functions::PosReports.Functions.${handler}::Handler`,
        timeout: Duration.seconds(timeout), memorySize: 256, environment
      });
    const common = { BUCKET_NAME: bucket.bucketName };
    const receiver = fn('Receiver', 'ReceiverFunction', 10, common);
    const parser = fn('Parser', 'ParserFunction', 30, { ...common, PARSED_TABLE_NAME: parsed.tableName, CALCULATION_QUEUE_URL: queue.queueUrl });
    const calculator = fn('Calculator', 'CalculatorFunction', 30, { ...common, PARSED_TABLE_NAME: parsed.tableName, RESULT_TABLE_NAME: results.tableName });
    const status = fn('Status', 'StatusFunction', 10, { ...common, PARSED_TABLE_NAME: parsed.tableName, RESULT_TABLE_NAME: results.tableName });
    bucket.grantPut(receiver, 'pos/*');
    bucket.grantRead(parser, 'pos/*');
    bucket.grantPut(parser, 'attachment/*');
    bucket.grantRead(parser, 'attachment/*'); // compare existing immutable attachment on duplicate
    parsed.grantReadWriteData(parser);
    queue.grantSendMessages(parser);
    parsed.grantReadData(calculator);
    bucket.grantRead(calculator, 'attachment/*');
    bucket.grantPut(calculator, 'results/*');
    results.grantReadWriteData(calculator);
    parsed.grantReadData(status);
    results.grantReadData(status);
    bucket.grantRead(status, 'results/*');
    bucket.addEventNotification(s3.EventType.OBJECT_CREATED, new s3n.LambdaDestination(parser), { prefix: 'pos/' });
    // S3 invokes Lambda asynchronously; failed invocations go to a dedicated destination after retries.
    const asyncConfig = new lambda.EventInvokeConfig(this, 'ParserAsyncFailureConfig', {
      function: parser, retryAttempts: 2, maxEventAge: Duration.hours(2),
      onFailure: new destinations.SqsDestination(parserFailureQueue)
    });
    asyncConfig.node.addDependency(parser);
    calculator.addEventSource(new sources.SqsEventSource(queue, { batchSize: 1 }));
    const api = new apigw.RestApi(this, 'PosReportsApi', { restApiName: 'POS Reports Assessment' });
    api.root.addResource('pos-reports').addMethod('POST', new apigw.LambdaIntegration(receiver));
    api.root.addResource('status').addResource('{flightId}').addMethod('GET', new apigw.LambdaIntegration(status));
    new cdk.CfnOutput(this, 'ApiBaseUrl', { value: api.url });
    new cdk.CfnOutput(this, 'BucketName', { value: bucket.bucketName });
    new cdk.CfnOutput(this, 'ParsedTableName', { value: parsed.tableName });
    new cdk.CfnOutput(this, 'ResultsTableName', { value: results.tableName });
    new cdk.CfnOutput(this, 'CalculationQueueUrl', { value: queue.queueUrl });
    new cdk.CfnOutput(this, 'CalculatorDlqUrl', { value: dlq.queueUrl });
    new cdk.CfnOutput(this, 'ParserFailureQueueUrl', { value: parserFailureQueue.queueUrl });
  }
  private table(id: string): dynamodb.Table {
    return new dynamodb.Table(this, id, {
      partitionKey: { name: 'flightId', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'timestamp', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: RemovalPolicy.DESTROY // Assessment/dev only
    });
  }
}

