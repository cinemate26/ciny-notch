namespace CinyNotch.Services;

/// <summary>
/// Where the island keeps its things. Portable: everything lives in the project folder
/// (the one holding "Start Ciny Notch.cmd"), in its "data" subfolder. A copy of the app
/// placed anywhere else keeps its data in a "data" folder next to the .exe.
/// </summary>
internal static class Paths
{
    private const string Marker = "Start Ciny Notch.cmd";

    public static readonly string Root = FindRoot();
    public static readonly string Data = Path.Combine(Root, "data");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var d = dir; d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, Marker))) return d.FullName;
        }
        return dir.FullName;
    }
}
