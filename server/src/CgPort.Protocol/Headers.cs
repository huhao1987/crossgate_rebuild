using System.Text.Json;

namespace CgPort.Protocol;

/// <summary>
/// 包头注册表。cgmsv.exe 里 nrproto_serv.c 附近有一张 72 项的混淆包头表（2–4 个字母），
/// 外加秋菟扩展的 SWITM 系列。每个包头对应什么操作要用封包日志对照后填进 headers.json。
/// </summary>
public sealed class HeaderRegistry
{
    public static readonly string[] KnownFromServerBinary =
    {
        "Cpa","QBt","jmb","aBS","OaHi","dAgg","LYIm","ELoy","PudO","vWpn","xmgQ","MTg","Mhbt","Ilz","IYo","lDuJ","gnxd","PhdQ",
        "aDn","znv","yuD","Kka","oRut","Rll","frQp","HNP","eZT","IUn","DpSY","cNB","DyAG","KyN","zdr","aPc","IsSy","TNT","nsY","JJQj",
        "uSB","cNE","Rspn","OBee","SkBD","sZWu","bNY","ysVc","nFV","JgO","Cxo","NdM","nUbJ","hJq","RwK","xEg","BHe","zzd","TFCu",
        "wiB","bJg","PVfz","murD","ftaN","TeD","GmNM","whV","hPUd","Daj","HMNy","cJzI","LOh","lSZ","PfM",
    };
    public static readonly string[] QiutuExtensions = { "SWITM", "SWITMP", "SWITMP2", "SWITMM" };

    public sealed record Entry(string Header, string Direction = "?", string Meaning = "unknown", string? Notes = null);

    private readonly Dictionary<string, Entry> _map = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, Entry> All => _map;

    public HeaderRegistry()
    {
        foreach (var h in KnownFromServerBinary) _map[h] = new Entry(h);
        foreach (var h in QiutuExtensions) _map[h] = new Entry(h, Meaning: "秋菟扩展（背包切换相关）");
    }

    public bool IsKnown(string header) => _map.ContainsKey(header);
    public string Describe(string header) => _map.TryGetValue(header, out var e) ? e.Meaning : "未登记";

    /// <summary>从 headers.json 合并对照结果（数组：[{ "Header":..., "Direction":"C2S|S2C", "Meaning":... }]）。</summary>
    public int MergeJson(string path)
    {
        var list = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? new();
        foreach (var e in list) _map[e.Header] = e;
        return list.Count;
    }

    public void SaveJson(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(_map.Values.OrderBy(e => e.Header), new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
}
