using Amazon.S3;
using Amazon.S3.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using PosReports.Application;
using System.Text;

namespace PosReports.Infrastructure;
public sealed class S3ObjectStore(IAmazonS3 client, string bucket) : IObjectStore {
    public async Task<bool> PutIfAbsentAsync(string key, string text, string contentType, CancellationToken ct) {
        try {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            await client.PutObjectAsync(new PutObjectRequest { BucketName=bucket, Key=key, InputStream=stream, ContentType=contentType, IfNoneMatch="*" }, ct);
            return true;
        } catch (AmazonS3Exception e) when ((int)e.StatusCode == 412) { return false; }
    }
    public async Task<string> ReadAsync(string key, CancellationToken ct) {
        using var response = await client.GetObjectAsync(bucket,key,ct);
        using var reader = new StreamReader(response.ResponseStream, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }
}
public static class DbMap {
    public static Dictionary<string,AttributeValue> Key(string flightId, string timestamp) => new() { ["flightId"]=new(){S=flightId}, ["timestamp"]=new(){S=timestamp} };
    public static string S(Dictionary<string,AttributeValue> item,string key) => item.TryGetValue(key,out var v) ? v.S : "";
    public static async Task<bool> Put(IAmazonDynamoDB client,string table,Dictionary<string,AttributeValue> item,CancellationToken ct) {
        try { await client.PutItemAsync(new PutItemRequest { TableName=table, Item=item, ConditionExpression="attribute_not_exists(flightId) AND attribute_not_exists(#ts)", ExpressionAttributeNames=new(){["#ts"]="timestamp"} },ct); return true; }
        catch (ConditionalCheckFailedException) { return false; }
    }
    public static async Task<Dictionary<string,AttributeValue>?> Get(IAmazonDynamoDB client,string table,string flightId,string timestamp,CancellationToken ct) {
        var response=await client.GetItemAsync(new GetItemRequest{TableName=table,Key=Key(flightId,timestamp),ConsistentRead=true},ct);
        return response.IsItemSet ? response.Item : null;
    }
    public static async Task<Dictionary<string,AttributeValue>?> Latest(IAmazonDynamoDB client,string table,string flightId,CancellationToken ct) {
        var response=await client.QueryAsync(new QueryRequest {TableName=table,KeyConditionExpression="flightId = :id",ExpressionAttributeValues=new(){[":id"]=new(){S=flightId}},ScanIndexForward=false,Limit=1,ConsistentRead=true},ct);
        return response.Items.FirstOrDefault();
    }
}
public sealed class DynamoParsedRepository(IAmazonDynamoDB client,string table):IParsedRepository {
    private static ParsedRecord? Map(Dictionary<string,AttributeValue>? item)=>item is null?null:new(DbMap.S(item,"flightId"),DbMap.S(item,"timestamp"),DbMap.S(item,"attachment"),DbMap.S(item,"fingerprint"));
    public async Task<ParsedRecord?> GetAsync(string flightId,string timestamp,CancellationToken ct)=>Map(await DbMap.Get(client,table,flightId,timestamp,ct));
    public async Task<ParsedRecord?> LatestAsync(string flightId,CancellationToken ct)=>Map(await DbMap.Latest(client,table,flightId,ct));
    public Task<bool> PutIfAbsentAsync(ParsedRecord r,CancellationToken ct) {
        var item=DbMap.Key(r.FlightId,r.Timestamp);item["attachment"]=new(){S=r.Attachment};item["fingerprint"]=new(){S=r.Fingerprint};
        return DbMap.Put(client,table,item,ct);
    }
}
public sealed class DynamoResultRepository(IAmazonDynamoDB client,string table):IResultRepository {
    private static ResultRecord? Map(Dictionary<string,AttributeValue>? item)=>item is null?null:new(DbMap.S(item,"flightId"),DbMap.S(item,"timestamp"),DbMap.S(item,"attachment"),DbMap.S(item,"sourceReportTimestamp"));
    public async Task<ResultRecord?> LatestAsync(string flightId,CancellationToken ct)=>Map(await DbMap.Latest(client,table,flightId,ct));
    public Task<bool> PutIfAbsentAsync(ResultRecord r,CancellationToken ct) {
        var item=DbMap.Key(r.FlightId,r.Timestamp);item["attachment"]=new(){S=r.Attachment};item["sourceReportTimestamp"]=new(){S=r.SourceReportTimestamp};
        return DbMap.Put(client,table,item,ct);
    }
    public async Task<bool> HasSourceAsync(string flightId,string sourceTimestamp,CancellationToken ct) {
        Dictionary<string,AttributeValue>? start=null;
        do {
            var response=await client.QueryAsync(new QueryRequest {TableName=table,KeyConditionExpression="flightId = :id",ExpressionAttributeValues=new(){[":id"]=new(){S=flightId}},ConsistentRead=true,ExclusiveStartKey=start,ProjectionExpression="sourceReportTimestamp"},ct);
            if(response.Items.Any(x=>DbMap.S(x,"sourceReportTimestamp")==sourceTimestamp))return true;
            start=response.LastEvaluatedKey is {Count:>0}?response.LastEvaluatedKey:null;
        } while(start is not null);
        return false;
    }
}
public sealed class SqsPublisher(IAmazonSQS client,string url):IMessagePublisher {
    public async Task PublishAsync(CalculationMessage message,CancellationToken ct) {
        await client.SendMessageAsync(new SendMessageRequest { QueueUrl = url, MessageBody = PosJson.Serialize(message) }, ct);
    }
}
