using System.Collections;

namespace SmartXChain.Utils;

/// <summary>
///     Thread safe List
///     <typeparam name="T"></typeparam>
///     using System;using System;
public class ConcurrentList<T> : IEnumerable<T>
{
    private readonly List<T> _list = new();
    private readonly ReaderWriterLockSlim _lock = new();

    // Get the number of items
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

    // Enumerator for foreach loops
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

    // Add a single item
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

    // Add multiple items (AddRange)
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

    // Remove and return the first item (TryTake)
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

    // Clear all items
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

    // Check if the list contains an item
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

    // Get an item at a specific index
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

    // Convert the list to an array
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

    // LINQ methods

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