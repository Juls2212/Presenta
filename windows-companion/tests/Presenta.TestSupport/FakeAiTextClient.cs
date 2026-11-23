using Presenta.Ai;

namespace Presenta.TestSupport;

/// <summary>
/// A scripted <see cref="IAiTextClient"/>. It records every prompt, answers from a script (or with a fixed text), and can
/// hold a request open until the test releases it or the caller cancels it (to prove nothing waits for the provider).
/// </summary>
public sealed class FakeAiTextClient : IAiTextClient
{
    private readonly object _gate = new();
    private readonly List<AiPrompt> _prompts = new();

    /// <summary>The answer when no script is set.</summary>
    public AiTextResult Answer { get; set; } = AiTextResult.Success("Pick up the pace now");

    /// <summary>Decides the answer for each call (call number from 1, prompt). Overrides <see cref="Answer"/>.</summary>
    public Func<int, AiPrompt, AiTextResult>? Script { get; set; }

    /// <summary>When set, calls wait for this to complete (or for cancellation) before answering.</summary>
    public TaskCompletionSource? Hold { get; set; }

    public int Calls
    {
        get
        {
            lock (_gate)
            {
                return _prompts.Count;
            }
        }
    }

    public IReadOnlyList<AiPrompt> Prompts
    {
        get
        {
            lock (_gate)
            {
                return _prompts.ToArray();
            }
        }
    }

    public async Task<AiTextResult> GenerateAsync(AiPrompt prompt, CancellationToken cancellationToken)
    {
        int number;
        lock (_gate)
        {
            _prompts.Add(prompt);
            number = _prompts.Count;
        }

        if (Hold is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return Script is null ? Answer : Script(number, prompt);
    }
}
