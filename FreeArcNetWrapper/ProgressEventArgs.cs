using System;
using System.Collections.Generic;

namespace FreeArcNetWrapper
{
    public delegate void ProgressEventDelegate(object sender, ProgressEventArgs e);

    public enum ProgressStates
    {       
        None,
        Start,
        Done,
    };

    public class ProgressEventArgs : EventArgs,
        IComparable<ProgressEventArgs>, IComparable,
        IEquatable<ProgressEventArgs>, IEqualityComparer<ProgressEventArgs>
    {
        #region Fields

        protected ProgressStates _status = ProgressStates.None;
        protected float _percentDone = 0;

        #endregion

        #region Properties

        public ProgressStates Status
        {
            get { return _status; }
            set { _status = value; }
        }

        public int PercentDone
        {
            get { return (int)Math.Min(_percentDone, 100); }
            set { _percentDone = value; }
        }

        #endregion

        #region Construction

        public ProgressEventArgs(float percentDone)
            : this((int)percentDone)
        {
        }

        public ProgressEventArgs(int percentDone)
        {
            _percentDone = percentDone;
        }

        public ProgressEventArgs(ProgressStates status)
        {
            _status = status;
            switch (status)
            {
                case ProgressStates.Start:
                    _percentDone = 0;
                    break;
                case ProgressStates.Done:
                    _percentDone = 100;
                    break;
            }
        }

        #endregion

        #region Overrides

        public override string ToString()
        {
            return PercentDone.ToString() + "%";
        }
            
        #endregion

        #region IComparable<ProgressEventArgs> Members

        public int CompareTo(ProgressEventArgs other)
        {
            if (PercentDone != other.PercentDone)
                return PercentDone.CompareTo(other.PercentDone);

            if (Status != other.Status)
                return Status.CompareTo(other.Status);          
            else
                return 1;
        }

        #endregion

        #region IComparable Members

        public int CompareTo(object obj)
        {
            return CompareTo((ProgressEventArgs)obj);
        }

        #endregion

        #region IEquatable<ProgressEventArgs> Members

        public bool Equals(ProgressEventArgs other)
        {
            return CompareTo(other) == 0;
        }

        #endregion

        #region IEqualityComparer<ProgressEventArgs> Members

        public bool Equals(ProgressEventArgs x, ProgressEventArgs y)
        {
            return x.CompareTo(y) == 0;
        }

        public int GetHashCode(ProgressEventArgs obj)
        {
            return obj.GetHashCode();
        }

        #endregion
    }
}
