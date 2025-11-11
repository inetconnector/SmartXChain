using System.Collections;

namespace SmartXChain.Utils;

/// <summary>
///     Provides a thread-safe list implementation that supports concurrent read and write operations.
/// </summary>
/// <typeparam name="T">The type of elements stored in the list.</typeparam>
public class ConcurrentList<T> : IEnumerable<T>
{
    private readonly List<T> _list = new();
    private readonly ReaderWriterLockSlim _lock = new();

    /// <summary>
    ///     Gets the number of items contained in the list.
    /// </summary>
    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _list.Count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <summary>
    ///     Returns an enumerator that iterates through a snapshot of the list.
    /// </summary>
    public IEnumerator<T> GetEnumerator()
    {
        _lock.EnterReadLock();
        try
        {
            return _list.ToList().GetEnumerator();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    ///     Adds a single item to the list.
    /// </summary>
    /// <param name="item">The item to add.</param>
    public void Add(T item)
    {
        _lock.EnterWriteLock();
        try
        {
            _list.Add(item);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    ///     Adds a collection of items to the list.
    /// </summary>
    /// <param name="items">The items to add.</param>
    public void AddRange(IEnumerable<T> items)
    {
        _lock.EnterWriteLock();
        try
        {
            _list.AddRange(items);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    ///     Attempts to remove and return the first item from the list.
    /// </summary>
    /// <param name="item">When this method returns, contains the removed item if the operation succeeded.</param>
    /// <returns><c>true</c> if an item was removed; otherwise, <c>false</c>.</returns>
    public bool TryTake(out T item)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_list.Count > 0)
            {
                item = _list[0];
                _list.RemoveAt(0);
                return true;
            }
            else
            {
                item = default!;
                return false;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    ///     Removes all items from the list.
    /// </summary>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _list.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    ///     Determines whether the list contains a specific value.
    /// </summary>
    /// <param name="item">The item to locate in the list.</param>
    /// <returns><c>true</c> if the item is found; otherwise, <c>false</c>.</returns>
    public bool Contains(T item)
    {
        _lock.EnterReadLock();
        try
        {
            return _list.Contains(item);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    ///     Retrieves the item at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the item to retrieve.</param>
    /// <returns>The item located at the specified index.</returns>
    public T Get(int index)
    {
        _lock.EnterReadLock();
        try
        {
            return _list[index];
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    ///     Creates a snapshot of the list as an array.
    /// </summary>
    /// <returns>An array containing the current elements of the list.</returns>
    public T[] ToArray()
    {
        _lock.EnterReadLock();
        try
        {
            return _list.ToArray();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    ///     Projects each element of the list into a new form.
    /// </summary>
    /// <param name="selector">A transform function to apply to each element.</param>
    /// <typeparam name="TResult">The type of the value returned by the selector.</typeparam>
    /// <returns>A snapshot containing the projected elements.</returns>
    public IEnumerable<TResult> Select<TResult>(Func<T, TResult> selector)
    {
        _lock.EnterReadLock();
        try
        {
            return _list.Select(selector).ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    ///     Filters the elements of the list based on a predicate.
    /// </summary>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>A snapshot containing elements that satisfy the predicate.</returns>
    public IEnumerable<T> Where(Func<T, bool> predicate)
    {
        _lock.EnterReadLock();
        try
        {
            return _list.Where(predicate).ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    ///     Sorts the elements of the list according to a key.
    /// </summary>
    /// <param name="keySelector">A function to extract a key from an element.</param>
    /// <typeparam name="TKey">The type of key returned by the selector.</typeparam>
    /// <returns>A snapshot of the list ordered by the specified key.</returns>
    public IEnumerable<T> OrderBy<TKey>(Func<T, TKey> keySelector)
    {
        _lock.EnterReadLock();
        try
        {
            return _list.OrderBy(keySelector).ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    ///     Determines whether any elements satisfy the specified predicate.
    /// </summary>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns><c>true</c> if any elements satisfy the predicate; otherwise, <c>false</c>.</returns>
    public bool Any(Func<T, bool> predicate)
    {
        _lock.EnterReadLock();
        try
        {
            return _list.Any(predicate);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    ///     Determines whether all elements satisfy the specified predicate.
    /// </summary>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns><c>true</c> if every element satisfies the predicate; otherwise, <c>false</c>.</returns>
    public bool All(Func<T, bool> predicate)
    {
        _lock.EnterReadLock();
        try
        {
            return _list.All(predicate);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    ///     Returns the first element that satisfies the specified predicate or a default value if none are found.
    /// </summary>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>The first matching element, or the default value for <typeparamref name="T" /> if no match is found.</returns>
    public T FirstOrDefault(Func<T, bool> predicate)
    {
        _lock.EnterReadLock();
        try
        {
            return _list.FirstOrDefault(predicate);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
}