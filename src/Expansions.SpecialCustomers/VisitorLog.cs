using Expansions.Core.Logging;

namespace Expansions.SpecialCustomers;

/// <summary>
/// Logger for the code S1API calls into.
/// <para>
/// <c>ConfigurePrefab</c> and <c>OnCreated</c> run on S1API's schedule, including while the module
/// is disabled and after its lifetime has been torn down, so they cannot borrow the module's
/// context-owned logger. This is the same sink under the same prefix, with no lifetime attached.
/// </para>
/// </summary>
internal static class VisitorLog
{
    internal static ModuleLogger Instance { get; } = new(SpecialCustomersModule.ModuleId);
}
