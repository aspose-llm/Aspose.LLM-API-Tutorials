namespace Aspose.LLM.Tutorials.Common;

/// <summary>
/// Tiny console-formatting helpers so each tutorial's Program.cs stays focused
/// on the API surface rather than Write/WriteLine ceremony.
/// </summary>
public static class ConsoleHelpers
{
    public static void Banner(string title)
    {
        var bar = new string('=', Math.Max(4, title.Length + 4));
        WriteColored(ConsoleColor.Cyan, $"\n{bar}");
        WriteColored(ConsoleColor.Cyan, $"  {title}");
        WriteColored(ConsoleColor.Cyan, $"{bar}\n");
    }

    public static void Section(string title)
        => WriteColored(ConsoleColor.Yellow, $"\n── {title} ──");

    public static void UserLine(string text)
        => WriteColored(ConsoleColor.Green, $"You > {text}");

    public static void AssistantLine(string text)
    {
        var prev = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("Bot < ");
            Console.WriteLine(text);
        }
        finally { Console.ForegroundColor = prev; }
    }

    public static void Info(string text) => WriteColored(ConsoleColor.DarkGray, text);
    public static void Warn(string text) => WriteColored(ConsoleColor.DarkYellow, text);
    public static void Fail(string text) => WriteColored(ConsoleColor.Red, text);

    private static void WriteColored(ConsoleColor color, string text)
    {
        var prev = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
        }
        finally { Console.ForegroundColor = prev; }
    }
}
