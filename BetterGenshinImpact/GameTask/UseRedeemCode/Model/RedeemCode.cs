namespace BetterGenshinImpact.GameTask.UseRedeemCode.Model;

public class RedeemCode
{
    public string Code { get; set; }

    public string? Items { get; set; }

    /// <summary>
    /// 兑换码有效日期（过期日期），格式 yyyy-MM-dd
    /// </summary>
    public string? Valid { get; set; }

    public RedeemCode(string code, string? items)
    {
        Code = code;
        Items = items;
    }

    public RedeemCode(string code, string? items, string? valid)
    {
        Code = code;
        Items = items;
        Valid = valid;
    }
}