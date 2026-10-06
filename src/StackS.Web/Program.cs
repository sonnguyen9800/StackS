using System.Runtime.InteropServices.JavaScript;
using StackS.Kernel;

Console.WriteLine("StackS C# runtime loaded");

namespace StackS.Web
{
    /// <summary>The only surface the editor sees. Everything goes through the kernel's command bus.</summary>
    public static partial class Bridge
    {
        static readonly Runtime R = new();

        [JSExport] public static string Exec(string name, string argsJson) => R.Exec(name, argsJson);
        [JSExport] public static void Tick(int input) => R.Tick(input);
        [JSExport] public static string View() => R.ViewJson();
    }
}
