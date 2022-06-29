namespace EcoInSpace
{
    using Eco.Shared.Math;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;

    public class MultiBounds<T> : IList<T>, IBounds where T : IBounds
    {
        public List<T> Bounds { get; } = new List<T>();
        public int Count => ((ICollection<T>)Bounds).Count;
        public bool IsReadOnly => ((ICollection<T>)Bounds).IsReadOnly;
        public MultiBounds(IEnumerable<T> collection1, params IEnumerable<T>[] others)
        {
            Bounds = new List<T>(collection1);
            foreach (IEnumerable<T> other in others)
            {
                Bounds.AddRange(other);
            }
        }
        public MultiBounds()
        {
        }
        public MultiBounds(T bounds1, params T[] others)
        {
            Bounds.Add(bounds1);
            Bounds.AddRange(others);
        }
        public void Add(T item)
        {
            ((ICollection<T>)Bounds).Add(item);
        }
        public void Clear()
        {
            ((ICollection<T>)Bounds).Clear();
        }
        public bool Contains(T item)
        {
            return ((ICollection<T>)Bounds).Contains(item);
        }
        public void CopyTo(T[] array, int arrayIndex)
        {
            ((ICollection<T>)Bounds).CopyTo(array, arrayIndex);
        }
        public IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)Bounds).GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)Bounds).GetEnumerator();
        }
        public bool InBounds(Vector3 position)
        {
            return Bounds.Any(bounds => bounds.InBounds(position));
        }
        public int IndexOf(T item)
        {
            return ((IList<T>)Bounds).IndexOf(item);
        }
        public void Insert(int index, T item)
        {
            ((IList<T>)Bounds).Insert(index, item);
        }
        public bool Remove(T item)
        {
            return ((ICollection<T>)Bounds).Remove(item);
        }
        public void RemoveAt(int index)
        {
            ((IList<T>)Bounds).RemoveAt(index);
        }
        public T this[int index] { get => ((IList<T>)Bounds)[index]; set => ((IList<T>)Bounds)[index] = value; }
    }
}