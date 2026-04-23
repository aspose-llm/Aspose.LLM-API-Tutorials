using Aspose.LLM;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// Interactive chat REPL.
//
// Type a message, hit enter, read the reply, repeat. Ctrl+C or /quit exits.
// Special commands:
//   /quit        — exit the program.
//   /new         — abandon the current session and start a fresh one.
//   /cancel      — demonstrated via Ctrl+C during a generation (see README).
//
// The facade remembers the currently-active session internally, so after the
// first SendMessageAsync all subsequent calls stay in the same conversation.
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — Simple Chat");

TutorialEnvironment.ApplyLicense();
using var api = new AsposeLLMApi(new SmallModelPreset());

// Support Ctrl+C mid-generation: the first press cancels the current
// SendMessageAsync (via the CancellationToken passed in); the second exits.
var cts = new CancellationTokenSource();
var cancelsSeen = 0;
Console.CancelKeyPress += (_, e) =>
{
    cancelsSeen++;
    if (cancelsSeen == 1)
    {
        ConsoleHelpers.Warn("\n[cancel requested — press Ctrl+C again to quit]");
        cts.Cancel();
        e.Cancel = true; // don't terminate yet
    }
    // second press: let the runtime terminate.
};

ConsoleHelpers.Info("Type a message and press Enter. /new resets, /quit exits.\n");

while (true)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("You > ");
    Console.ResetColor();

    var input = Console.ReadLine();
    if (input is null) break;           // stdin closed (pipe, EOF)
    input = input.Trim();
    if (input.Length == 0) continue;

    if (input.Equals("/quit", StringComparison.OrdinalIgnoreCase)) break;

    if (input.Equals("/new", StringComparison.OrdinalIgnoreCase))
    {
        var newId = await api.StartNewChatAsync();
        ConsoleHelpers.Info($"[new session {newId}]");
        continue;
    }

    // Reset the token for each new user turn.
    if (cts.IsCancellationRequested)
    {
        cts.Dispose();
        cts = new CancellationTokenSource();
        cancelsSeen = 0;
    }

    try
    {
        var reply = await api.SendMessageAsync(input, cancellationToken: cts.Token);
        ConsoleHelpers.AssistantLine(reply);
    }
    catch (OperationCanceledException)
    {
        ConsoleHelpers.Warn("[generation cancelled]");
    }
    catch (Exception ex)
    {
        ConsoleHelpers.Fail($"[error] {ex.Message}");
    }
}

ConsoleHelpers.Info("\nGoodbye.");
