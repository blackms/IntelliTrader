using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace IntelliTrader.Core
{
    /// <summary>
    /// A thread-safe bounded stack that automatically removes oldest items when capacity is exceeded.
    /// Implements a circular buffer pattern using ConcurrentQueue internally for efficient FIFO removal.
    /// </summary>
    /// <typeparam name="T">The type of elements in the stack.</typeparam>
    public class BoundedConcurrentStack<T> : IEnumerable<T>, ICollection
    {
        private readonly ConcurrentQueue<T> _queue;
        private readonly int _maxCapacity;
        private readonly object _trimLock = new object();
        private int _count;

        /// <summary>
        /// Default maximum capacity for order history collections.
        /// </summary>
        public const int DefaultMaxCapacity = 10000;

        /// <summary>
        /// Initializes a new instance of the BoundedConcurrentStack with the specified maximum capacity.
        /// </summary>
        /// <param name="maxCapacity">The maximum number of items the stack can hold. Defaults to 10,000.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when maxCapacity is less than 1.</exception>
        public BoundedConcurrentStack(int maxCapacity = DefaultMaxCapacity)
        {
            if (maxCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCapacity), "Maximum capacity must be at least 1.");
            }

            _maxCapacity = maxCapacity;
            _queue = new ConcurrentQueue<T>();
            _count = 0;
        }

        /// <summary>
        /// Gets the maximum capacity of the stack.
        /// </summary>
        public int MaxCapacity => _maxCapacity;

        /// <summary>
        /// Gets the number of items currently in the stack.
        /// </summary>
        public int Count => Volatile.Read(ref _count);

        /// <summary>
        /// Gets a value indicating whether the stack is empty.
        /// </summary>
        public bool IsEmpty => Count == 0;

        /// <summary>
        /// Gets a value indicating whether the stack is at maximum capacity.
        /// </summary>
        public bool IsFull => Count >= _maxCapacity;

        /// <summary>
        /// Gets an object that can be used to synchronize access to the collection.
        /// </summary>
        public object SyncRoot => _trimLock;

        /// <summary>
        /// Gets a value indicating whether access to the collection is synchronized (thread-safe).
        /// </summary>
        public bool IsSynchronized => true;

        /// <summary>
        /// Pushes an item onto the top of the stack.
        /// If the stack is at capacity, the oldest item is removed first.
        /// </summary>
        /// <param name="item">The item to push onto the stack.</param>
        /// <returns>The number of items removed to make room (0 or 1).</returns>
        public int Push(T item)
        {
            int itemsRemoved = 0;

            // Add the item first
            _queue.Enqueue(item);
            Interlocked.Increment(ref _count);

            // Check if we need to trim
            if (Count > _maxCapacity)
            {
                itemsRemoved = TrimExcess();
            }

            return itemsRemoved;
        }

        /// <summary>
        /// Attempts to peek at the most recently added item without removing it.
        /// </summary>
        /// <param name="result">When this method returns, contains the item at the top of the stack, if found.</param>
        /// <returns>true if an item was found; otherwise, false.</returns>
        public bool TryPeek(out T result)
        {
            // For stack semantics, we need the last item (most recently added)
            // ConcurrentQueue doesn't support this directly, so we materialize
            var items = _queue.ToArray();
            if (items.Length > 0)
            {
                result = items[items.Length - 1];
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>
        /// Attempts to pop the most recently added item from the stack.
        /// Note: This operation is O(n) due to the underlying queue implementation.
        /// For frequent pop operations, consider using a different data structure.
        /// </summary>
        /// <param name="result">When this method returns, contains the item that was removed, if found.</param>
        /// <returns>true if an item was removed; otherwise, false.</returns>
        public bool TryPop(out T result)
        {
            // Pop from stack means removing the most recently added item
            // This is expensive with a queue, but OrderHistory primarily uses Push and enumeration
            lock (_trimLock)
            {
                var items = _queue.ToArray();
                if (items.Length == 0)
                {
                    result = default;
                    return false;
                }

                // Clear and re-add all but the last item
                while (_queue.TryDequeue(out _)) { }

                for (int i = 0; i < items.Length - 1; i++)
                {
                    _queue.Enqueue(items[i]);
                }

                Interlocked.Decrement(ref _count);
                result = items[items.Length - 1];
                return true;
            }
        }

        /// <summary>
        /// Removes all items from the stack.
        /// </summary>
        public void Clear()
        {
            lock (_trimLock)
            {
                while (_queue.TryDequeue(out _)) { }
                Interlocked.Exchange(ref _count, 0);
            }
        }

        /// <summary>
        /// Determines whether the stack contains a specific value.
        /// </summary>
        /// <param name="item">The item to locate in the stack.</param>
        /// <returns>true if item is found; otherwise, false.</returns>
        public bool Contains(T item)
        {
            return _queue.Contains(item);
        }

        /// <summary>
        /// Copies the stack elements to an array, starting at a particular array index.
        /// Elements are copied in LIFO order (most recent first).
        /// </summary>
        /// <param name="array">The destination array.</param>
        /// <param name="index">The zero-based index at which copying begins.</param>
        public void CopyTo(Array array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            // Copy in reverse order for stack semantics (most recent first)
            var items = _queue.ToArray();
            for (int i = items.Length - 1; i >= 0; i--)
            {
                array.SetValue(items[i], index++);
            }
        }

        /// <summary>
        /// Copies the stack elements to a typed array.
        /// Elements are copied in LIFO order (most recent first).
        /// </summary>
        /// <param name="array">The destination array.</param>
        /// <param name="arrayIndex">The zero-based index at which copying begins.</param>
        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            // Copy in reverse order for stack semantics (most recent first)
            var items = _queue.ToArray();
            for (int i = items.Length - 1; i >= 0; i--)
            {
                array[arrayIndex++] = items[i];
            }
        }

        /// <summary>
        /// Returns an array containing all items in the stack in LIFO order (most recent first).
        /// </summary>
        /// <returns>An array of all items.</returns>
        public T[] ToArray()
        {
            var items = _queue.ToArray();
            var result = new T[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                result[i] = items[items.Length - 1 - i];
            }
            return result;
        }

        /// <summary>
        /// Returns an enumerator that iterates through the stack in LIFO order (most recent first).
        /// </summary>
        /// <returns>An enumerator for the stack.</returns>
        public IEnumerator<T> GetEnumerator()
        {
            // Return items in reverse order (LIFO - most recent first)
            var items = _queue.ToArray();
            for (int i = items.Length - 1; i >= 0; i--)
            {
                yield return items[i];
            }
        }

        /// <summary>
        /// Returns an enumerator that iterates through the stack.
        /// </summary>
        /// <returns>An enumerator for the stack.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Gets the items that were archived (removed due to capacity limits).
        /// This is a hook for implementing archiving mechanisms.
        /// </summary>
        public event EventHandler<ArchivedItemsEventArgs<T>> ItemsArchived;

        /// <summary>
        /// Removes excess items when the capacity is exceeded.
        /// </summary>
        /// <returns>The number of items removed.</returns>
        private int TrimExcess()
        {
            int itemsRemoved = 0;
            var archivedItems = new List<T>();

            lock (_trimLock)
            {
                while (Count > _maxCapacity && _queue.TryDequeue(out T removedItem))
                {
                    Interlocked.Decrement(ref _count);
                    archivedItems.Add(removedItem);
                    itemsRemoved++;
                }
            }

            // Raise event for archiving if there are subscribers
            if (itemsRemoved > 0 && ItemsArchived != null)
            {
                ItemsArchived.Invoke(this, new ArchivedItemsEventArgs<T>(archivedItems));
            }

            return itemsRemoved;
        }
    }

    /// <summary>
    /// Event arguments for items that were archived (removed) from a BoundedConcurrentStack.
    /// </summary>
    /// <typeparam name="T">The type of archived items.</typeparam>
    public class ArchivedItemsEventArgs<T> : EventArgs
    {
        /// <summary>
        /// Gets the items that were archived.
        /// </summary>
        public IReadOnlyList<T> ArchivedItems { get; }

        /// <summary>
        /// Initializes a new instance of the ArchivedItemsEventArgs class.
        /// </summary>
        /// <param name="archivedItems">The archived items.</param>
        public ArchivedItemsEventArgs(IList<T> archivedItems)
        {
            ArchivedItems = archivedItems as IReadOnlyList<T> ?? new List<T>(archivedItems);
        }
    }
}
