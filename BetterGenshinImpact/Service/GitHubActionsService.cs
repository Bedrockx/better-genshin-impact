using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Model.GitHub;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service;

public class GitHubActionsService
{
    private const string Owner = "kaedelcb";
    private const string Repo = "better-genshin-impact";
    private const string WorkflowName = "BetterGI Publish";
    private const string ApiBase = "https://api.github.com";

    private readonly ILogger<GitHubActionsService> _logger = App.GetLogger<GitHubActionsService>();

    /// <summary>
    /// 查询最近成功的构建产物（最多3个）
    /// </summary>
    public async Task<List<BuildArtifactInfo>> GetLatestArtifactsAsync(
        string? githubToken = null,
        CancellationToken ct = default)
    {
        using var httpClient = CreateHttpClient(githubToken);

        // 直接使用已知的工作流 ID，省一次 API 调用
        const long knownWorkflowId = 161175465; // BetterGI Publish

        // 获取最近成功运行（多取一些，过滤后取前3）
        var runs = await GetSuccessfulRunsAsync(httpClient, knownWorkflowId, ct);
        if (runs.Count == 0)
        {
            return [];
        }

        // 从每个运行中提取 .7z 产物信息，同时标注版本类型
        var artifacts = new List<BuildArtifactInfo>();
        foreach (var run in runs)
        {
            var tag = BuildArtifactInfo.ParseVersionTag(run.DisplayTitle);

            // 使用 nightly.link 公开下载链接，无需逐个查询 artifacts API
            var downloadUrl = $"https://nightly.link/{Owner}/{Repo}/actions/runs/{run.Id}/BetterGI_Install.zip";

            artifacts.Add(new BuildArtifactInfo
            {
                Version = run.DisplayTitle,
                CreatedAt = run.CreatedAt,
                DownloadUrl = downloadUrl,
                ArtifactName = "BetterGI_Install",
                RunId = run.Id,
                VersionTag = tag
            });

            if (artifacts.Count >= 3)
                break;
        }

        return artifacts;
    }

    /// <summary>
    /// 创建配置好的 HttpClient，包含 User-Agent 和可选的 Token 认证
    /// </summary>
    internal static HttpClient CreateHttpClient(string? githubToken)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BetterGenshinImpact");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(githubToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);
        }

        return client;
    }

    private async Task<List<WorkflowRun>> GetSuccessfulRunsAsync(HttpClient httpClient, long workflowId, CancellationToken ct)
    {
        var url = $"{ApiBase}/repos/{Owner}/{Repo}/actions/workflows/{workflowId}/runs?status=success&per_page=5";
        var json = await SendRequestAsync(httpClient, url, ct);
        if (json == null) return [];

        var response = JsonConvert.DeserializeObject<WorkflowRunsResponse>(json);
        return response?.WorkflowRuns ?? [];
    }

    private async Task<string?> SendRequestAsync(HttpClient httpClient, string url, CancellationToken ct)
    {
        try
        {
            var response = await httpClient.GetAsync(url, ct);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(ct);
            }

            // 处理特定 HTTP 错误
            switch (response.StatusCode)
            {
                case HttpStatusCode.Forbidden:
                    _logger.LogWarning("GitHub API 速率限制 (HTTP 403)，请配置 GitHub Token 以提高速率限制");
                    throw new HttpRequestException("GitHub API 请求被限制 (HTTP 403)，请在设置中配置 GitHub Token 以提高速率限制。");

                case HttpStatusCode.Unauthorized:
                    _logger.LogWarning("GitHub Token 无效 (HTTP 401)");
                    throw new HttpRequestException("GitHub Token 无效 (HTTP 401)，请检查并重新配置 Token。");

                default:
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("GitHub API 请求失败: HTTP {StatusCode}, {ErrorBody}", (int)response.StatusCode, errorBody);
                    throw new HttpRequestException($"GitHub API 请求失败: HTTP {(int)response.StatusCode}");
            }
        }
        catch (HttpRequestException)
        {
            throw; // 重新抛出已处理的 HTTP 错误
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "GitHub API 请求超时");
            throw new HttpRequestException("GitHub API 请求超时，可能需要 VPN 才能访问 GitHub。也可使用第三方工具在线升级。");
        }
        catch (TaskCanceledException)
        {
            throw; // 用户取消操作，直接抛出
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GitHub API 请求发生未知错误");
            throw new HttpRequestException($"GitHub API 请求失败: {ex.Message}", ex);
        }
    }
}
