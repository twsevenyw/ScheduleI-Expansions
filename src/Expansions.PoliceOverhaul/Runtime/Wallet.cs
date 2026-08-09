using S1API.Money;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Reads the player's money without ever letting a missing manager throw into a per-frame
/// availability check. Both balances matter to this module: the shipped economy makes cash abundant
/// and the online balance scarce, so a penalty that only touches cash is not a penalty.
/// </summary>
internal static class Wallet
{
    internal static float Cash() => Read(Money.GetCashBalance);

    internal static float Online() => Read(Money.GetOnlineBalance);

    internal static float Total() => Cash() + Online();

    private static float Read(Func<float> get)
    {
        try
        {
            return get();
        }
        catch
        {
            return 0f;
        }
    }
}
