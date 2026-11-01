namespace Adamantium.CoreTests.Services
{
    public class CircularFirst
    {
        public CircularFirst(CircularSecond second)
        {
            Second = second;
        }

        public CircularSecond Second { get; }
    }
}
