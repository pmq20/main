/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Microsoft Public License. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the  Microsoft Public License, please send an email to 
 * dlr@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Microsoft Public License.
 *
 * You must not remove this notice, or any other, from this software.
 *
 *
 * ***************************************************************************/

using System;
using System.Collections;
using IronPython.Runtime.Operations;
using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime {
    [PythonType("slice")]
    public sealed class Slice : ICodeFormattable, IComparable, IValueEquality, ISlice {
        private readonly object _start, _stop, _step;

        public Slice(object stop) : this(null, stop, null) { }

        public Slice(object start, object stop) : this(start, stop, null) { }

        public Slice(object start, object stop, object step) {
            _start = start;
            _stop = stop;
            _step = step;
        }

        #region Python Public API Surface

        public object start {
            get { return _start; }
        }

        public object stop {
            get { return _stop; }
        }

        public object step {
            get { return _step; }
        }

        public int __cmp__(Slice obj) {
            return PythonOps.CompareArrays(new object[] { _start, _stop, _step }, 3,
                new object[] { obj._start, obj._stop, obj._step }, 3);
        }

        public void indices(int len, out int ostart, out int ostop, out int ostep) {
            int count;
            PythonOps.FixSlice(len, _start, _stop, _step, out ostart, out ostop, out ostep, out count);
        }

        public void indices(object len, out int ostart, out int ostop, out int ostep) {
            int count;
            PythonOps.FixSlice(Converter.ConvertToIndex(len), _start, _stop, _step, out ostart, out ostop, out ostep, out count);
        }

        public static bool operator >(Slice self, Slice other) {
            return self.__cmp__(other) > 0;
        }

        public static bool operator <(Slice self, Slice other) {
            return self.__cmp__(other) < 0;
        }

        #endregion

        #region Object overrides

        public override bool Equals(object obj) {
            Slice s = obj as Slice;
            if (s == null) return false;

            return (PythonOps.Compare(_start, s._start) == 0) &&
                (PythonOps.Compare(_stop, s._stop) == 0) &&
                (PythonOps.Compare(_step, s._step) == 0);
        }

        public override int GetHashCode() {
            int hash = 0;
            if (_start != null) hash ^= _start.GetHashCode();
            if (_stop != null) hash ^= _stop.GetHashCode();
            if (_step != null) hash ^= _step.GetHashCode();
            return hash;
        }

        #endregion

        #region IComparable Members

        int IComparable.CompareTo(object obj) {
            Slice other = obj as Slice;
            if (other == null) throw new ArgumentException("expected slice");
            return __cmp__(other);
        }

        #endregion

        #region IValueEquality Members

        int IValueEquality.GetValueHashCode() {
            throw PythonOps.TypeErrorForUnhashableType("slice");
        }

        /// <summary>
        /// slice is sealed so equality doesn't need to be virtual and can be the IValueEquality
        /// interface implementation
        /// </summary>
        bool IValueEquality.ValueEquals(object other) {
            return Equals(other);
        }

        #endregion

        #region ISlice Members

        object ISlice.Start {
            get { return start; }
        }

        object ISlice.Stop {
            get { return stop; }
        }

        object ISlice.Step {
            get { return step; }
        }

        #endregion

        #region ICodeFormattable Members

        public string/*!*/ __repr__(CodeContext/*!*/ context) {
            return string.Format("slice({0}, {1}, {2})", PythonOps.Repr(context, _start), PythonOps.Repr(context, _stop), PythonOps.Repr(context, _step));
        }

        #endregion
        
        #region Internal Implementation details

        internal static void FixSliceArguments(int size, ref int start, ref int stop) {
            start = start < 0 ? 0 : start > size ? size : start;
            stop = stop < 0 ? 0 : stop > size ? size : stop;
        }

        /// <summary>
        /// Gets the indices for the deprecated __getslice__, __setslice__, __delslice__ functions
        /// 
        /// This form is deprecated in favor of using __getitem__ w/ a slice object as an index.  This
        /// form also has subtly different mechanisms for fixing the slice index before calling the function.
        /// 
        /// If an index is negative and __len__ is not defined on the object than an AttributeError
        /// is raised.
        /// </summary>
        internal void DeprecatedFixed(object self, out int newStart, out int newStop) {
            bool calcedLength = false;  // only call __len__ once, even if we need it twice
            int length = 0;

            if (_start != null) {
                newStart = Converter.ConvertToIndex(_start);
                if (newStart < 0) {
                    calcedLength = true;
                    length = PythonOps.Length(self);

                    newStart += length;
                }
            } else {
                newStart = 0;
            }

            if (_stop != null) {
                newStop = Converter.ConvertToIndex(_stop);
                if (newStop < 0) {
                    if (!calcedLength) length = PythonOps.Length(self);

                    newStop += length;
                }
            } else {
                newStop = Int32.MaxValue;
            }

        }

        internal delegate void SliceAssign(int index, object value);

        internal void DoSliceAssign(SliceAssign assign, int size, object value) {
            int ostart, ostop, ostep;
            indices(size, out ostart, out ostop, out ostep);

            if (this._step == null) throw PythonOps.ValueError("cannot do slice assignment w/ no step");

            DoSliceAssign(assign, ostart, ostop, ostep, value);
        }

        private static void DoSliceAssign(SliceAssign assign, int start, int stop, int step, object value) {
            int n = Math.Max(0, (step > 0 ? (stop - start + step - 1) : (stop - start + step + 1)) / step);
            // fast paths, if we know the size then we can
            // do this quickly.
            if (value is IList) {
                ListSliceAssign(assign, start, n, step, value as IList);
            } else if (value is ISequence) {
                SequenceSliceAssign(assign, start, n, step, value as ISequence);
            } else {
                OtherSliceAssign(assign, start, stop, step, value);
            }
        }

        private static void ListSliceAssign(SliceAssign assign, int start, int n, int step, IList lst) {
            if (lst.Count < n) throw PythonOps.ValueError("too few items in the enumerator. need {0} have {1}", n, lst.Count);
            else if (lst.Count != n) throw PythonOps.ValueError("too many items in the enumerator need {0} have {1}", n, lst.Count);

            for (int i = 0, index = start; i < n; i++, index += step) {
                assign(index, lst[i]);
            }
        }

        private static void SequenceSliceAssign(SliceAssign assign, int start, int n, int step, ISequence lst) {
            if (lst.__len__() < n) throw PythonOps.ValueError("too few items in the enumerator. need {0}", n);
            else if (lst.__len__() != n) throw PythonOps.ValueError("too many items in the enumerator need {0}", n);

            for (int i = 0, index = start; i < n; i++, index += step) {
                assign(index, lst[i]);
            }

        }

        private static void OtherSliceAssign(SliceAssign assign, int start, int stop, int step, object value) {
            // get enumerable data into a list, and then
            // do the slice.
            IEnumerator enumerator = PythonOps.GetEnumerator(value);
            List sliceData = new List();
            while (enumerator.MoveNext()) sliceData.AddNoLock(enumerator.Current);

            DoSliceAssign(assign, start, stop, step, sliceData);
        }

        #endregion
    }
}