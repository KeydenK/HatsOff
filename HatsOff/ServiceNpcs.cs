using Lumina.Excel.Sheets;

namespace HatsOff;

/// <summary>
/// Tells whether an NPC is a vendor or service NPC (shops, exchanges, service menus), from the game's own data.
/// Each NPC lists its event handlers in the ENpcBase sheet; the upper 16 bits of a handler id give its type
/// (values from FFXIVClientStructs' EventHandlerContent). One lookup per NPC, cached: practically free.
/// </summary>
internal static class ServiceNpcs
{
    private static readonly HashSet<uint> ServiceHandlerTypes =
    [
        4,  // Shop (gil)
        22, // GrandCompanyShop
        27, // SpecialShop (exchanges: tomestones, scrips, seals...)
        42, // FreeCompanyCreditShop
        49, // RetainerBuyback
        50, // TopicSelect (vendor / service menus)
        52, // LotteryExchangeShop
        53, // DisposalShop
        54, // PreHandler (usually wraps a shop or service menu)
        58, // InclusionShop
        59, // CollectablesShop
    ];

    private static readonly Dictionary<uint, bool> Cache = new();

    public static bool IsServiceNpc(uint baseId)
    {
        if (baseId == 0)
            return false;

        if (Cache.TryGetValue(baseId, out var known))
            return known;

        var result = false;
        try
        {
            if (Plugin.Data.GetExcelSheet<ENpcBase>().GetRowOrDefault(baseId) is { } npc)
            {
                foreach (var handler in npc.ENpcData)
                {
                    if (ServiceHandlerTypes.Contains(handler.RowId >> 16))
                    {
                        result = true;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Debug(ex, "Could not read NPC data, treating it as a normal NPC.");
        }

        Cache[baseId] = result;
        return result;
    }
}
