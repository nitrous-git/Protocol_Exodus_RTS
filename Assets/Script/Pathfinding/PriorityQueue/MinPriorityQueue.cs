using System;
using System.Collections.Generic;

/// <summary>
/// Generic binary min-heap priority queue.
///
/// The element with the lowest priority is dequeued first.
///
/// Enqueue: O(log n)
/// Dequeue: O(log n)
/// </summary>
public sealed class MinPriorityQueue<TElement, TPriority>
{
    private struct Entry
    {
        public TElement Element;
        public TPriority Priority;

        public Entry(
            TElement element,
            TPriority priority)
        {
            Element = element;
            Priority = priority;
        }
    }

    private readonly List<Entry> heap = new();
    private readonly IComparer<TPriority> comparer;

    public int Count => heap.Count;

    public MinPriorityQueue()
    {
        comparer = Comparer<TPriority>.Default;
    }

    public MinPriorityQueue(IComparer<TPriority> comparer)
    {
        this.comparer = comparer ?? Comparer<TPriority>.Default;
    }

    public void Enqueue(TElement element, TPriority priority)
    {
        Entry entry = new Entry(element, priority);

        heap.Add(entry);

        SiftUp(heap.Count - 1);
    }

    public TElement Dequeue()
    {
        if (heap.Count == 0)
        {
            throw new InvalidOperationException("Cannot dequeue from an empty priority queue.");
        }

        TElement minimumElement = heap[0].Element;

        int lastIndex = heap.Count - 1;

        Swap(0, lastIndex);

        heap.RemoveAt(lastIndex);

        if (heap.Count > 0)
        {
            SiftDown(0);
        }

        return minimumElement;
    }

    public void Clear()
    {
        heap.Clear();
    }

    private int Parent(int index)
    {
        return (index - 1) / 2;
    }

    private int Left(int index)
    {
        return index * 2 + 1;
    }

    private int Right(int index)
    {
        return index * 2 + 2;
    }

    private bool HasLeft(int index)
    {
        return Left(index) < heap.Count;
    }

    private bool HasRight(int index)
    {
        return Right(index) < heap.Count;
    }

    private void SiftUp(int index)
    {
        while (index > 0)
        {
            int parentIndex = Parent(index);

            if (Compare(heap[index], heap[parentIndex]) >= 0)
            {
                break;
            }

            Swap(index, parentIndex);

            index = parentIndex;
        }
    }

    private void SiftDown(int index)
    {
        while (HasLeft(index))
        {
            int leftIndex = Left(index);

            int smallestChildIndex = leftIndex;

            if (HasRight(index))
            {
                int rightIndex = Right(index);

                if (Compare(heap[rightIndex], heap[leftIndex]) < 0)
                {
                    smallestChildIndex = rightIndex;
                }
            }

            if (Compare(heap[smallestChildIndex], heap[index]) >= 0)
            {
                break;
            }

            Swap(index, smallestChildIndex);

            index = smallestChildIndex;
        }
    }

    private int Compare(Entry first, Entry second)
    {
        return comparer.Compare(first.Priority, second.Priority);
    }

    private void Swap(int firstIndex, int secondIndex)
    {
        Entry temporary = heap[firstIndex];

        heap[firstIndex] = heap[secondIndex];

        heap[secondIndex] = temporary;
    }
}