using Amazon.S3;
using Amazon.S3.Model;
using Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;


namespace Infrastructure.Storage;

/// <summary>
/// AWS S3 implementation of IFileStorage.
/// NACHA files are written here by participant banks (or their middleware)
/// and read here by the ingestion worker. Files are never deleted — S3
/// lifecycle policies manage long-term archiving to Glacier.
/// </summary>
public sealed class S3FileStorage(
    IAmazonS3 s3,
    IConfiguration configuration,
    ILogger<S3FileStorage> logger)
    : IFileStorage
{
    private string BucketName =>
        configuration["AWS:S3:BucketName"]
        ?? throw new InvalidOperationException("AWS S3 bucket name is not configured");

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        logger.LogDebug("Reading S3 object s3://{Bucket}/{Key}", BucketName, key);

        var response = await s3.GetObjectAsync(new GetObjectRequest
        {
            BucketName = BucketName,
            Key = key
        }, ct);

        return response.ResponseStream;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await s3.GetObjectMetadataAsync(BucketName, key, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async IAsyncEnumerable<string> ListKeysAsync(
        string prefix,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string? continuationToken = null;

        do
        {
            var request = new ListObjectsV2Request
            {
                BucketName = BucketName,
                Prefix = prefix,
                ContinuationToken = continuationToken
            };

            var response = await s3.ListObjectsV2Async(request, ct);

            foreach (var obj in response.S3Objects)
                yield return obj.Key;

            continuationToken = (bool)response.IsTruncated ? response.NextContinuationToken : null;
        }
        while (continuationToken is not null);
    }
}