using System.Reflection;

namespace DynamicIsland;

// Single source of truth for the app version in outgoing HTTP User-Agent headers.
// The version comes from the assembly (DynamicIsland.csproj <Version>), so it never drifts again.
static class AppVersion
{
    static readonly Version Current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static string Product { get; } = $"DynamicIsland/{Current.Major}.{Current.Minor}.{Math.Max(Current.Build, 0)}";
}
