using System;
using System.Threading;
using Cysharp.Threading.Tasks;

public interface IChatProvider
{
    bool IsAvailable { get; }
    string ProviderName { get; }

    UniTask ChatAsync(
        string userMessage,
        Action<string> onStream,
        Action onComplete,
        CancellationToken cancellationToken = default
    );
}
