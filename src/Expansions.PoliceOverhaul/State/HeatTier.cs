namespace Expansions.PoliceOverhaul.State;

/// <summary>
/// Five bands of the 0-100 heat scalar. T1 is deliberately vanilla: the game shipped "patrols,
/// sentries and checkpoints assign 1-2 officers" in v0.4.6, so T1 reproduces the shipped upper bound
/// exactly and T0 the shipped lower bound. Everything above T1 extends the same ladder.
/// </summary>
internal enum HeatTier
{
    Calm = 0,
    Alert = 1,
    Crackdown = 2,
    TaskForce = 3,
    Federal = 4,
}

/// <summary>Latched law-enforcement reputation. Heat is the pressure; this is the phase change.</summary>
internal enum OutlawTier
{
    Clean = 0,
    Marked = 1,
    Hunted = 2,
}
