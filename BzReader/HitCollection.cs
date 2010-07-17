using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace BzReader
{
    /// <summary>
    /// The class encapsulates the list of the index hits
    /// </summary>
    public class HitCollection : CollectionBase, IEnumerable, IEnumerator
    {
        /// <summary>
        /// The current position for IEnumerable
        /// </summary>
        private int index = -1;
        /// <summary>
        /// Whether there were more hits in search that is being returned
        /// </summary>
        private bool hadMoreHits = false;
        /// <summary>
        /// The error messages which were returned by Lucene while searching the indices
        /// </summary>
        private string errorMessages = String.Empty;

        /// <summary>
        /// The error messages which were returned by Lucene while searching the indices
        /// </summary>
        public string ErrorMessages
        {
            get { return errorMessages; }
            set { errorMessages = value; }
        }

        /// <summary>
        /// Whether there were more hits in search that is being returned
        /// </summary>
        public bool HadMoreHits
        {
            get { return hadMoreHits; }
            set { hadMoreHits = true; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HitCollection"/> class.
        /// </summary>
        public HitCollection()
        {
            this.index = -1;
        }

        /// <summary>
        /// Adds the specified hit.
        /// </summary>
        /// <param name="hit">The hit.</param>
        public void Add(PageInfo hit)
        {
            if (hit == null)
            {
                throw new ArgumentNullException("hit");
            }

            this.List.Add(hit);
        }

        /// <summary>
        /// Removes the specified hit.
        /// </summary>
        /// <param name="hit">The hit.</param>
        public void Remove(PageInfo hit)
        {
            this.List.Remove(hit);
        }

        /// <summary>
        /// Gets or sets the <see cref="PageInfo"/> at the specified index.
        /// </summary>
        /// <value></value>
        public PageInfo this[int index]
        {
            get { return (PageInfo)this.List[index]; }
            set { this.List[index] = value; }
        }

        #region IEnumerable Members

        /// <summary>
        /// Returns an enumerator that iterates through the <see cref="T:System.Collections.CollectionBase"></see> instance.
        /// </summary>
        /// <returns>
        /// An <see cref="T:System.Collections.IEnumerator"></see> for the <see cref="T:System.Collections.CollectionBase"></see> instance.
        /// </returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return this;
        }

        #endregion

        #region IEnumerator Members

        /// <summary>
        /// Gets the current element in the collection.
        /// </summary>
        /// <value></value>
        /// <returns>The current element in the collection.</returns>
        /// <exception cref="T:System.InvalidOperationException">The enumerator is positioned before the first element of the collection or after the last element. </exception>
        public Object Current
        {
            get
            {
                return this.List[index];
            }
        }

        /// <summary>
        /// Advances the enumerator to the next element of the collection.
        /// </summary>
        /// <returns>
        /// true if the enumerator was successfully advanced to the next element; false if the enumerator has passed the end of the collection.
        /// </returns>
        /// <exception cref="T:System.InvalidOperationException">The collection was modified after the enumerator was created. </exception>
        public bool MoveNext()
        {
            this.index++;
            return (this.index < this.List.Count);
        }

        /// <summary>
        /// Sets the enumerator to its initial position, which is before the first element in the collection.
        /// </summary>
        /// <exception cref="T:System.InvalidOperationException">The collection was modified after the enumerator was created. </exception>
        public void Reset()
        {
            this.index = -1;
        }

        #endregion
    }
}
