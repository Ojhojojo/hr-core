namespace HrCore.Infrastructure;

public interface IFileStore
{
    Task<string> SaveAsync(string storagePath, Stream content, string contentType, CancellationToken cancellationToken);
    Task<Stream?> OpenAsync(string storagePath, CancellationToken cancellationToken);
}

public sealed class LocalFileStore(IWebHostEnvironment environment) : IFileStore
{
    public async Task<string> SaveAsync(string storagePath, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var full = Path.Combine(environment.ContentRootPath, "App_Data", "files", storagePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var file = File.Create(full);
        await content.CopyToAsync(file, cancellationToken);
        return storagePath;
    }

    public Task<Stream?> OpenAsync(string storagePath, CancellationToken cancellationToken)
    {
        var full = Path.Combine(environment.ContentRootPath, "App_Data", "files", storagePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = File.OpenRead(full);
        return Task.FromResult<Stream?>(stream);
    }
}

public sealed class SupabaseStorageStore(
    IHttpClientFactory httpFactory,
    IConfiguration configuration,
    ILogger<SupabaseStorageStore> logger)
    : IFileStore
{
    public const string Bucket = "job-files";

    public async Task<string> SaveAsync(string storagePath, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var url = (Environment.GetEnvironmentVariable("SUPABASE_URL")
            ?? configuration["Supabase:Url"]
            ?? configuration["SUPABASE_URL"])?.TrimEnd('/');
        var key = Environment.GetEnvironmentVariable("SUPABASE_SECRET_KEY")
            ?? configuration["Supabase:SecretKey"]
            ?? configuration["SUPABASE_SECRET_KEY"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("SUPABASE_URL and SUPABASE_SECRET_KEY are required for file storage.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{url}/storage/v1/object/{Bucket}/{storagePath}");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {key}");
        request.Headers.TryAddWithoutValidation("apikey", key);
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);

        var client = httpFactory.CreateClient("supabase-storage");
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Supabase Storage upload failed {Status}: {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException("Could not store the file.");
        }

        return storagePath;
    }

    public async Task<Stream?> OpenAsync(string storagePath, CancellationToken cancellationToken)
    {
        var url = (Environment.GetEnvironmentVariable("SUPABASE_URL")
            ?? configuration["Supabase:Url"]
            ?? configuration["SUPABASE_URL"])?.TrimEnd('/');
        var key = Environment.GetEnvironmentVariable("SUPABASE_SECRET_KEY")
            ?? configuration["Supabase:SecretKey"]
            ?? configuration["SUPABASE_SECRET_KEY"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{url}/storage/v1/object/{Bucket}/{storagePath}");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {key}");
        request.Headers.TryAddWithoutValidation("apikey", key);
        var client = httpFactory.CreateClient("supabase-storage");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            return null;
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }
}
