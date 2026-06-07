namespace SteamHibernate.Core.Tiering;

public sealed record TieringResult(bool Success, string? Error = null)
{
    public static TieringResult Ok() => new(true);
    public static TieringResult Fail(string error) => new(false, error);
}
