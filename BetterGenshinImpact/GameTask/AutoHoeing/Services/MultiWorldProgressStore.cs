using System;
using System.IO;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// 房主多世界轮换进度的本地读写（Newtonsoft.Json，存 records 目录，类比 CdManager）。
/// 文件路径：{dataDir}/records/multiworld_progress_{accountName}.json。
/// hoeing-multiworld-host-restart-resume-round Req 1 / 2。
/// </summary>
public class MultiWorldProgressStore
{
    private static readonly ILogger Logger = App.GetLogger<MultiWorldProgressStore>();
    private string _filePath = "";
    private MultiWorldProgress _progress = new();

    public MultiWorldProgress Current => _progress;

    public void Load(string dataDir, string accountName)
    {
        _filePath = Path.Combine(dataDir, "records", $"multiworld_progress_{accountName}.json");
        if (!File.Exists(_filePath)) { _progress = new(); return; }
        try
        {
            var json = File.ReadAllText(_filePath);
            _progress = JsonConvert.DeserializeObject<MultiWorldProgress>(json) ?? new();
        }
        catch (Exception ex)
        {
            // 读取失败按"无进度"处理：上层会因此上报空集合 → 全量序列（安全降级，Req 2.4）。
            Logger.LogWarning(ex, "[多世界进度] 加载失败，按无进度处理");
            _progress = new();
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            _progress.UpdatedAtUtc = DateTime.UtcNow;
            File.WriteAllText(_filePath, JsonConvert.SerializeObject(_progress, Formatting.Indented));
        }
        catch (Exception ex)
        {
            // 写入失败不影响主任务：下次重开拿不到这条进度，最坏退化为多试探一轮（非数据损坏）。
            Logger.LogWarning(ex, "[多世界进度] 保存失败（忽略，最坏多试探一轮）");
        }
    }

    /// <summary>记录某房主世界已锄完（每轮锄完后调用）。orderSignature 为当前轮换序列签名。</summary>
    public void MarkHostCompleted(string hostUid, string orderSignature)
    {
        if (string.IsNullOrEmpty(hostUid)) return;
        if (_progress.OrderSignature != orderSignature)
        {
            // 序列签名变了（玩家集合变化）→ 旧进度作废，从新签名重新累计。
            _progress = new MultiWorldProgress { OrderSignature = orderSignature };
        }
        if (!_progress.CompletedHostUids.Contains(hostUid))
            _progress.CompletedHostUids.Add(hostUid);
        Save();
    }

    /// <summary>整场全部完成后清空进度（下次为全新一场）。</summary>
    public void Clear()
    {
        _progress = new MultiWorldProgress();
        Save();
    }
}
