using System;
using System.Collections.Concurrent;
using Statiq.Common;

namespace Thirty25.Statiq.Helpers;

public static class ExtensionMethods
{
    private static readonly object _executionCacheLock = new();
    private static readonly ConcurrentDictionary<string, object> _executionCache = new();
    private static Guid _lastExecutionId = Guid.Empty;

    public static T GetExecutionCache<T>(this IExecutionContext context, string key, Func<IExecutionContext, T> getter)
    {
        lock (_executionCacheLock)
        {
            if (_lastExecutionId != context.ExecutionId)
            {
                _executionCache.Clear();
                _lastExecutionId = context.ExecutionId;
            }

            return (T)_executionCache.GetOrAdd(key, valueFactory: _ => getter.Invoke(context));
        }
    }
}
