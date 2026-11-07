using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Yesly;

/// <summary>One page of a list. <see cref="AutoPagingAsync"/> walks this page and the ones after it.</summary>
public sealed class Page<T>
{
    private readonly string? _lastId;
    private readonly Func<string, CancellationToken, Task<Page<T>>> _fetchAfter;

    internal Page(string url, bool hasMore, IReadOnlyList<T> data, string? lastId, Func<string, CancellationToken, Task<Page<T>>> fetchAfter)
    {
        Url = url;
        HasMore = hasMore;
        Data = data;
        _lastId = lastId;
        _fetchAfter = fetchAfter;
    }

    public string Url { get; }

    public bool HasMore { get; }

    /// <summary>This page's items.</summary>
    public IReadOnlyList<T> Data { get; }

    public bool HasNextPage => HasMore && _lastId is not null;

    /// <summary>The page after this one. Check <see cref="HasNextPage"/> first.</summary>
    public Task<Page<T>> NextPageAsync(CancellationToken cancellationToken = default)
        => HasNextPage ? _fetchAfter(_lastId!, cancellationToken) : throw new InvalidOperationException("There is no next page.");

    /// <summary>Every item from this page on, fetching later pages as needed.</summary>
    public async IAsyncEnumerable<T> AutoPagingAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var page = this;
        while (true)
        {
            foreach (var item in page.Data) yield return item;
            if (!page.HasNextPage) yield break;
            page = await page.NextPageAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
