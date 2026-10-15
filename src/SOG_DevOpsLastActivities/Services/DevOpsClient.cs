using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SOG_DevOpsLastActivities.Models;

namespace SOG_DevOpsLastActivities.Services;

public sealed class DevOpsApiException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;

    /// <summary>The PAT itself was refused: every further call would fail the same way.</summary>
    public bool IsAuthenticationFailure => StatusCode == HttpStatusCode.Unauthorized;
}

/// <summary>
/// The REST calls the features make, for one configuration. Plain HttpClient and System.Text.Json:
/// the official client libraries are heavy, and the comment endpoints are preview APIs they lag on.
/// </summary>
public sealed class DevOpsClient
{
    // One HttpClient for the whole process; the PAT goes on each request instead, so sessions on
    // different configurations share the connections.
    private static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        Timeout = TimeSpan.FromSeconds(100),
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string ApiVersion = "7.1";
    private const string CommentsApiVersion = "7.1-preview.4";
    private const string RecentActivityApiVersion = "7.1-preview.2";

    /// <summary>Azure DevOps answers 429 when it throttles and 503 when busy; both pass.</summary>
    private const int MaxAttempts = 4;

    private const int PageSize = 200;

    private readonly string _baseUrl;
    private readonly string _projectUrl;
    private readonly AuthenticationHeaderValue _authorization;

    public DevOpsClient(DevOpsConfig config)
    {
        _baseUrl = config.BaseUrl;
        Project = config.Project.Trim();
        _projectUrl = $"{_baseUrl}/{Uri.EscapeDataString(Project)}";
        _authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + config.Pat.Trim())));
    }

    public string Project { get; }

    public string WorkItemWebUrl(int id) => $"{_projectUrl}/_workitems/edit/{id}";

    // ------------------------------------------------------------------ calls

    public async Task<Identity> GetCurrentUserAsync(CancellationToken ct)
    {
        var data = await SendAsync<JsonElement>(HttpMethod.Get, $"{_baseUrl}/_apis/connectionData", null, ct).ConfigureAwait(false);

        if (!data.TryGetProperty("authenticatedUser", out var user)
            || !user.TryGetProperty("id", out var id)
            || id.GetString() is not { Length: > 0 } userId)
        {
            throw new DevOpsApiException("Azure DevOps didn't say who the PAT belongs to.");
        }

        var displayName = user.TryGetProperty("providerDisplayName", out var name) ? name.GetString() : null;
        string? account = null;
        if (user.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty("Account", out var accountProperty)
            && accountProperty.ValueKind == JsonValueKind.Object
            && accountProperty.TryGetProperty("$value", out var accountValue))
        {
            account = accountValue.GetString();
        }

        // Some refusals come back as a successful answer about the anonymous user instead of a 401.
        if (string.Equals(displayName, "Anonymous", StringComparison.OrdinalIgnoreCase))
            throw new DevOpsApiException(RefusedMessage, HttpStatusCode.Unauthorized);

        return new Identity(userId, displayName ?? account ?? userId, account);
    }

    /// <summary>Runs a flat WIQL query in the project and returns the work item ids it finds.</summary>
    public async Task<IReadOnlyList<int>> QueryIdsAsync(string wiql, CancellationToken ct, int? top = null)
    {
        var url = $"{_projectUrl}/_apis/wit/wiql?api-version={ApiVersion}" + (top is { } n ? $"&$top={n}" : "");
        var result = await SendAsync<WiqlResultDto>(HttpMethod.Post, url, new { query = wiql }, ct).ConfigureAwait(false);
        return result.WorkItems.Select(w => w.Id).ToList();
    }

    /// <summary>
    /// The "My activity" list of the Work Items hub, for the whole organisation: the user's latest
    /// activity on each work item they viewed, edited, deleted or restored in about 30 days.
    /// </summary>
    public async Task<IReadOnlyList<RecentActivityDto>> GetMyRecentActivityAsync(CancellationToken ct)
    {
        var root = await SendAsync<JsonElement>(
            HttpMethod.Get, $"{_baseUrl}/_apis/work/accountmyworkrecentactivity?api-version={RecentActivityApiVersion}", null, ct).ConfigureAwait(false);

        // Documented as a bare array; most list endpoints wrap theirs in { count, value }.
        var list = root.ValueKind == JsonValueKind.Array ? root
                 : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out var value) ? value
                 : default;

        return list.ValueKind == JsonValueKind.Array
            ? list.Deserialize<List<RecentActivityDto>>(JsonOptions) ?? []
            : [];
    }

    /// <summary>The given fields of the given work items. Deleted or unreadable ones are left out.</summary>
    public async Task<IReadOnlyList<WorkItemDto>> GetWorkItemsAsync(IEnumerable<int> ids, IReadOnlyList<string> fields, CancellationToken ct)
    {
        var all = new List<WorkItemDto>();
        foreach (var chunk in ids.Distinct().Chunk(PageSize))
        {
            var body = new { ids = chunk, fields, errorPolicy = "omit" };
            var page = await SendAsync<ListResponse<WorkItemDto?>>(
                HttpMethod.Post, $"{_projectUrl}/_apis/wit/workitemsbatch?api-version={ApiVersion}", body, ct).ConfigureAwait(false);
            all.AddRange(page.Value.OfType<WorkItemDto>());
        }
        return all;
    }

    /// <summary>Every revision of a work item, oldest first, each with what it changed and who did it.</summary>
    public async Task<IReadOnlyList<WorkItemUpdateDto>> GetUpdatesAsync(int id, CancellationToken ct)
    {
        var all = new List<WorkItemUpdateDto>();
        while (true)
        {
            var url = $"{_projectUrl}/_apis/wit/workItems/{id}/updates?$top={PageSize}&$skip={all.Count}&api-version={ApiVersion}";
            var page = await SendAsync<ListResponse<WorkItemUpdateDto>>(HttpMethod.Get, url, null, ct).ConfigureAwait(false);
            all.AddRange(page.Value);
            if (page.Value.Count < PageSize) return all;
        }
    }

    /// <summary>Every comment on a work item, with its reactions.</summary>
    public async Task<IReadOnlyList<CommentDto>> GetCommentsAsync(int id, CancellationToken ct)
    {
        var all = new List<CommentDto>();
        string? token = null;
        do
        {
            var url = $"{_projectUrl}/_apis/wit/workItems/{id}/comments?$top={PageSize}&$expand=reactions&api-version={CommentsApiVersion}"
                      + (token is null ? "" : "&continuationToken=" + Uri.EscapeDataString(token));
            var page = await SendAsync<CommentListDto>(HttpMethod.Get, url, null, ct).ConfigureAwait(false);
            all.AddRange(page.Comments);
            token = page.ContinuationToken;
        }
        while (!string.IsNullOrEmpty(token));
        return all;
    }

    // -------------------------------------------------------------- plumbing

    private const string RefusedMessage =
        "The PAT was refused: it is wrong, has expired, or has no access to this organisation.";

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = _authorization;
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // Serialised up front rather than streamed: a streamed body goes out chunked, which some
            // proxies and older servers refuse, where one with a Content-Length always passes.
            if (body is not null)
                request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

            HttpResponseMessage sent;
            try
            {
                sent = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts && !ct.IsCancellationRequested)
            {
                // A dropped connection (VPN, proxy, Wi-Fi) is worth another try like a 503 is;
                // every call here only reads, so repeating one is harmless.
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct).ConfigureAwait(false);
                continue;
            }
            catch (HttpRequestException ex)
            {
                // The bare message is "An error occurred while sending the request"; the cause
                // (refused, name not resolved, reset) is in the inner exception.
                throw new HttpRequestException($"Couldn't reach {request.RequestUri?.Host}: {Innermost(ex).Message}", ex);
            }

            using var response = sent;

            if (IsTransient(response.StatusCode) && attempt < MaxAttempts)
            {
                await Task.Delay(RetryDelay(response, attempt), ct).ConfigureAwait(false);
                continue;
            }

            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct).ConfigureAwait(false)
                   ?? throw new DevOpsApiException("Azure DevOps sent an empty answer.");
        }
    }

    private static Exception Innermost(Exception ex)
    {
        while (ex.InnerException is not null) ex = ex.InnerException;
        return ex;
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta
                    ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null)
                    ?? TimeSpan.FromSeconds(2 * attempt);
        return delay < TimeSpan.Zero ? TimeSpan.Zero
             : delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30)
             : delay;
    }

    /// <summary>
    /// A refused PAT doesn't always come back as a 401: Azure DevOps may answer 203 with its sign-in
    /// page, or redirect to it, which HttpClient follows to a 200 holding HTML.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = response.StatusCode;
        var isHtml = response.Content.Headers.ContentType?.MediaType == "text/html";

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.NonAuthoritativeInformation
            || (response.IsSuccessStatusCode && isHtml))
        {
            throw new DevOpsApiException(RefusedMessage, HttpStatusCode.Unauthorized);
        }

        if (response.IsSuccessStatusCode) return;

        var message = await ReadErrorMessageAsync(response, ct).ConfigureAwait(false)
                      ?? response.ReasonPhrase
                      ?? "Request failed";
        throw new DevOpsApiException($"{message} ({(int)status})", status);
    }

    /// <summary>Azure DevOps errors carry their explanation in a JSON "message".</summary>
    private static async Task<string?> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(text);
            return document.RootElement.TryGetProperty("message", out var message) ? message.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
