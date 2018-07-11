using System.Collections.Generic;
using System.Threading;

namespace GZipTest
{
    /// <summary>
    /// Implementation of thread-safe dictionary with some extra functionality for taking indexed chunks out via trypopnext in strict order starting from 0 and going upwards with +1 step
    /// </summary>
    /// <typeparam name="Integer">represents index</typeparam>
    /// <typeparam name="TValue">represents indexed chunk of data</typeparam>
    internal class MySafeDictionary<Integer, TValue>
    {
        private Dictionary<int, TValue> _body = new Dictionary<int, TValue>();
        private ReaderWriterLockSlim _rwl = new ReaderWriterLockSlim();
        private int _currentIndexToPop = 0;

        public TValue this[int key]
        {
            get
            {
                _rwl.EnterReadLock();
                TValue temp = _body[key];
                _rwl.ExitReadLock();
                return temp;
            }
            set
            {
                _rwl.EnterWriteLock();
                _body[key] = value;
                _rwl.ExitWriteLock();
            }
        }

        public int Count
        {
            get
            {
                int temp;
                _rwl.EnterReadLock();
                temp = _body.Count;
                _rwl.ExitReadLock();
                return temp;
            }
        }

        /// <summary>
        /// Used to check if the chunk in index is ready to be taken, better than trypopnext because only needs reader access
        /// </summary>
        /// <param name="index">index that should be checked</param>
        /// <returns>true is item in index is next in line to pop and ready, false otherwise</returns>
        public bool IsNext(int index)
        {
            return (index == _currentIndexToPop && HasNext());
        }

        private bool HasNext()
        {
            bool temp;
            _rwl.EnterReadLock();
            if (_body.ContainsKey(_currentIndexToPop))
                temp = true;
            else
                temp = false;
            _rwl.ExitReadLock();
            return temp;
        }

        /// <summary>
        /// Returns Value that was supposed to be popped next, Default(TValue) if it's not ready
        /// </summary>
        /// <param name="index">Returns index of returned value, or -1 if Default(TValue) was returned</param>
        /// <returns>Value of the next key to be processed in order, starting from 0 and its index, if that value exists, otherwise returns default(TValue) and index -1 </returns>
        public TValue TryPopNext(out int index)
        {
            _rwl.EnterWriteLock();
            if (!_body.ContainsKey(_currentIndexToPop))
            {
                index = -1;
                _rwl.ExitWriteLock();
                return default(TValue);
            }
            TValue temp;
            temp = _body[_currentIndexToPop];
            index = _currentIndexToPop;
            _body.Remove(_currentIndexToPop);
            _currentIndexToPop++;
            _rwl.ExitWriteLock();
            return temp;
        }
    }
}