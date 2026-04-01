using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrashCollector.AI;

/// <summary>
/// GitHub REST API client for creating branches, committing files, and opening PRs.
/// Uses Personal Access Token (PAT) authentication.
/// </summary>
public sealed class GitHubClient : IDisposable
{
    private const string BaseUrl = "https://api.github.com";

    private readonly HttpClient _http;
    private readonly string _owner;
    private readonly string _repo;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GitHubClient(string token, string ownerSlashRepo, HttpClient? http = null)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("GitHub token is required", nameof(token));

        var parts = ownerSlashRepo.Split('/');
        if (parts.Length != 2)
            throw new ArgumentException("Expected format: owner/repo", nameof(ownerSlashRepo));

        _owner = parts[0];
        _repo = parts[1];

        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CrashAnalysisPOC", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    /// <summary>
    /// Gets the SHA of the latest commit on the given branch.
    /// </summary>
    public async Task<string?> GetBranchShaAsync(string branch = "main", CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/repos/{_owner}/{_repo}/git/refs/heads/{branch}";
        var response = await _http.GetAsync(url, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.GetProperty("object").GetProperty("sha").GetString();
    }

    /// <summary>
    /// Creates a new branch from the given base SHA.
    /// </summary>
    public async Task<bool> CreateBranchAsync(string branchName, string baseSha, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/repos/{_owner}/{_repo}/git/refs";
        var payload = new { @ref = $"refs/heads/{branchName}", sha = baseSha };

        var response = await _http.PostAsJsonAsync(url, payload, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Creates or updates a file in the repository on the given branch.
    /// </summary>
    public async Task<string?> CreateOrUpdateFileAsync(
        string branch, string filePath, string content, string commitMessage,
        CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/repos/{_owner}/{_repo}/contents/{filePath}";

        // Check if file already exists to get its SHA
        string? existingSha = null;
        var getResponse = await _http.GetAsync($"{url}?ref={branch}", ct).ConfigureAwait(false);
        if (getResponse.IsSuccessStatusCode)
        {
            using var doc = await JsonDocument.ParseAsync(
                await getResponse.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            existingSha = doc.RootElement.GetProperty("sha").GetString();
        }

        var payload = new Dictionary<string, string>
        {
            ["message"] = commitMessage,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
            ["branch"] = branch
        };
        if (existingSha is not null)
            payload["sha"] = existingSha;

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _http.PutAsync(url, jsonContent, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            System.Console.WriteLine($"[GitHubClient] Failed to create/update file: {(int)response.StatusCode} {body}");
            return null;
        }

        using var respDoc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return respDoc.RootElement.GetProperty("commit").GetProperty("sha").GetString();
    }

    /// <summary>
    /// Creates a Pull Request.
    /// </summary>
    public async Task<PullRequestResult?> CreatePullRequestAsync(
        string title, string body, string headBranch, string baseBranch = "main",
        CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/repos/{_owner}/{_repo}/pulls";
        var payload = new
        {
            title,
            body,
            head = headBranch,
            @base = baseBranch
        };

        var response = await _http.PostAsJsonAsync(url, payload, ct).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            System.Console.WriteLine($"[GitHubClient] Failed to create PR: {(int)response.StatusCode} {responseBody}");
            return null;
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        return new PullRequestResult
        {
            Number = root.GetProperty("number").GetInt32(),
            Url = root.GetProperty("html_url").GetString() ?? string.Empty,
            Title = root.GetProperty("title").GetString() ?? string.Empty,
            State = root.GetProperty("state").GetString() ?? "open"
        };
    }

    public void Dispose() => _http.Dispose();
}

public sealed class PullRequestResult
{
    public int Number { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string State { get; set; } = "open";
}
