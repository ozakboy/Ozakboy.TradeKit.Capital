namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// SKCOM 版本(<c>SKCenterLib_GetSKAPIVersionAndBit</c>,例如 <c>2.13.59_x64</c>)與本套件實測過的版本清單。
/// 宿主的 COM 介面依 SKCOM 2.13.59 x64 的型別庫產生:群益改版若動到方法順序,宿主會呼叫到錯的方法。升級 SKCOM 前請先跑宿主的
/// <c>probe-full</c> 探針並重跑 <c>tools/SkComInteropGen</c> 產生器。
/// The SKCOM version and the versions this package was measured against. The host's COM interfaces are generated from
/// the SKCOM 2.13.59 x64 type library. A new Capital build that reorders methods makes the host call the wrong method,
/// so run the host's probe-full probe and the tools/SkComInteropGen generator before you upgrade SKCOM.
/// </summary>
public static class CapitalSkcomVersion
{
    /// <summary>
    /// 實測過的版本(群益回的原文,比較時忽略大小寫與前後空白)。目前只有 <c>2.13.59_x64</c>。
    /// The measured versions, as Capital reports them; compared case-insensitively after trimming. Currently 2.13.59_x64 only.
    /// </summary>
    public static IReadOnlyList<string> Tested { get; } = ["2.13.59_x64"];

    /// <summary>
    /// 是否是實測過的版本。<see langword="null"/> 或空字串(宿主讀不到版本)回 <see langword="false"/>。
    /// Whether the version was measured; null or empty (the host could not read it) answers false.
    /// </summary>
    /// <param name="version">群益回的版本文字。The version text.</param>
    /// <returns>實測過時為 <see langword="true"/>。True when measured.</returns>
    public static bool IsTested(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        string trimmed = version.Trim();
        return Tested.Any(tested => string.Equals(tested, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
