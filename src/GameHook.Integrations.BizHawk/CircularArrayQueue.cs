namespace GameHook.Integrations.BizHawk
{
    public class CircularArrayQueue<T> where T : struct
    {
        private readonly T[] _array;
        private int _front;
        private int _rear;
        private readonly int _capacity;

        public CircularArrayQueue(int capacity)
        {
            _array = new T[capacity];
            _front = -1;
            _rear = -1;
            _capacity = capacity;
        }
        public CircularArrayQueue(T[] array, int startIdx, int endIdx)
        {
            _array = array;
            _front = startIdx;
            _rear = endIdx;
            _capacity = array.Length;
        }

        public bool Enqueue(T value)
        {
            if ((_rear + 1) % _capacity == _front)
            {
                return false;
            }
            else if (_front == -1)
            {
                _front = 0;
                _rear = 0;
                _array[_rear] = value;
                return true;
            }
            else
            {
                _rear = (_rear + 1) % _capacity;
                _array[_rear] = value;
                return true;
            }
        }

        public T? Dequeue()
        {
            if (_front == -1)
            {
                return null;
            }
            else
            {
                T ret = _array[_front];
                if (_front == _rear)
                {
                    _front = -1;
                    _rear = -1;
                }
                else
                {
                    _front = (_front + 1) % _capacity;
                }
                return ret;
            }
        }

        public int Front { get { return _front; } }
        public int Rear { get { return _rear; } }
        public T[] Array { get { return _array; } }
    }
}
