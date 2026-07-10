using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service;

public class GitHubAutoUpdater
{
    private readonly ILogger<GitHubAutoUpdater> _logger = App.GetLogger<GitHubAutoUpdater>();

    /// <summary>
    /// 下载构建产物（nightly.link 返回 zip 包，内含安装程序 exe）
    /// </summary>
    public async Task<string> DownloadArtifactAsync(
        string downloadUrl,
        string targetDir,
        string? githubToken,
        IProgress<(long downloaded, long total)>? progress = null,
        CancellationToken ct = default)
    {
        using var httpClient = CreateHttpClient(githubToken);

        using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        var zipPath = Path.Combine(targetDir, "artifact.zip");

        Directory.CreateDirectory(targetDir);

        await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
        await using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
        {
            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            var lastReport = DateTime.MinValue;

            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;

                // 限制进度更新频率：每秒最多一次
                var now = DateTime.UtcNow;
                if ((now - lastReport).TotalSeconds >= 1)
                {
                    progress?.Report((totalRead, totalBytes));
                    lastReport = now;
                }
            }

            // 最终报告一次确保 100%
            progress?.Report((totalRead, totalBytes));
        }

        // 从 zip 中提取安装程序 exe
        var exePath = ExtractInstallerFromZip(zipPath, targetDir);

        // 清理 zip
        try { File.Delete(zipPath); } catch { /* best effort */ }

        return exePath;
    }

    /// <summary>
    /// 从下载的 zip 中提取安装程序 exe
    /// </summary>
    private static string ExtractInstallerFromZip(string zipPath, string extractDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var exeEntry = zip.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        if (exeEntry == null)
        {
            throw new InvalidOperationException("下载的构建产物中未找到安装程序 (.exe)");
        }

        var targetPath = Path.Combine(extractDir, exeEntry.Name);
        exeEntry.ExtractToFile(targetPath, overwrite: true);
        return targetPath;
    }

    /// <summary>
    /// 创建配置好的 HttpClient
    /// </summary>
    private static HttpClient CreateHttpClient(string? githubToken)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30) // Longer timeout for large file downloads
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BetterGenshinImpact");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        if (!string.IsNullOrWhiteSpace(githubToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);
        }

        return client;
    }
}
