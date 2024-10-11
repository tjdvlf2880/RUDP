using System;
using System.Threading;

namespace NetLibrary.Utils
{
    public class InterLockedVal
    {
        int Val;

        public int _Val
        {
            get => Val;
        }

        public InterLockedVal(int val)
        {
            Val = val;
        }

        public int Decrease(int val)
        {
            return Interlocked.Decrement(ref Val);
        }

        public int Increase()
        {
            return Interlocked.Increment(ref Val);
        }

        public void SetVal(int newVal)
        {
            int currentValue;
            do
            {
                currentValue = Val;
                if (newVal == currentValue) break;

            } while (Interlocked.CompareExchange(ref Val, newVal, currentValue) == currentValue);
        }

        public void SetVal(Func<int, int> newVal)
        {
            int currentValue;
            int NewVal;
            do
            {
                currentValue = Val;
                NewVal = newVal.Invoke(currentValue);
                if (currentValue == NewVal) break;
            } while (Interlocked.CompareExchange(ref Val, NewVal, currentValue) == currentValue);
        }

        public bool SetVal(int newVal, Func<int, bool> condi)
        {
            int currentValue;
            int NewVal;
            do
            {
                currentValue = Val;
                NewVal = newVal;
                bool b = condi.Invoke(currentValue);
                if (b)
                {
                    if (currentValue == NewVal) return true;
                    if (Interlocked.CompareExchange(ref Val, NewVal, currentValue) == currentValue)
                    {
                        return true;
                    }
                }
                else
                {
                    return false;
                }
            } while (true);
        }
        public bool SetVal(Func<int, int> newVal, Func<int, bool> condi)
        {
            int currentValue;
            int NewVal;
            do
            {
                currentValue = Val;
                NewVal = newVal.Invoke(currentValue);
                bool b = condi.Invoke(currentValue);
                if (b)
                {
                    if (currentValue == NewVal) return true;
                    if (Interlocked.CompareExchange(ref Val, NewVal, currentValue) == currentValue)
                    {
                        return true;
                    }
                }
                else
                {
                    return false;
                }
            } while (true);
        }
    }
}
