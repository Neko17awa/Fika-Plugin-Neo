using EFT;
using Fika.Core.Main.Utils;

namespace Fika.Core.Main.Components;

/// <summary>
/// 参观建造 UI：标题、按钮、配方都带主人昵称，避免当成自己的藏身处。
/// </summary>
public static class HideoutCoopVisitUi
{
    public static readonly Color TitleColor = new(1f, 0.78f, 0.35f, 1f);

    public static string OwnerLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(FikaHideoutCoop.VisitOwnerName))
            {
                return FikaHideoutCoop.VisitOwnerName;
            }

            return Translate(LocaleUtils.UI_HIDEOUT_VISIT_HOST, "Host");
        }
    }

    public static string Title()
    {
        return string.Format(Translate(LocaleUtils.UI_HIDEOUT_VISIT_TITLE, "{0}'s Hideout"), OwnerLabel);
    }

    public static string Action(string verb)
    {
        return string.Format(Translate(LocaleUtils.UI_HIDEOUT_VISIT_ACTION, "{0} · {1}"), OwnerLabel, verb);
    }

    public static string Requirements(string original)
    {
        return string.Format(Translate(LocaleUtils.UI_HIDEOUT_VISIT_REQUIREMENTS, "{0} · {1}"), OwnerLabel, original);
    }

    public static string Tooltip()
    {
        return string.Format(
            Translate(LocaleUtils.UI_HIDEOUT_VISIT_TOOLTIP, "Items are taken from your stash. This upgrades {0}'s hideout."),
            OwnerLabel);
    }

    public static string AreaName(string areaName)
    {
        return string.Format(Translate(LocaleUtils.UI_HIDEOUT_VISIT_ACTION, "{0} · {1}"), OwnerLabel, areaName);
    }

    private static string Translate(string key, string fallback)
    {
        var text = key.Localized();
        return string.IsNullOrEmpty(text) || text == key ? fallback : text;
    }
}
