using System.Runtime.ExceptionServices;

namespace Packata.ResourceReaders;

internal static class ResourceDisposal
{
    internal static void Run(IEnumerable<Action> actions, bool suppressFailures = false)
    {
        Exception? failure = null;
        foreach (var dispose in actions)
        {
            try { dispose(); }
            catch (Exception error) { failure ??= error; }
        }
        if (!suppressFailures && failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
