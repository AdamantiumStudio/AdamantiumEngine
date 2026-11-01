namespace Adamantium.CoreTests.Services
{
    public class CircularSecond
    {
        public CircularSecond(CircularFirst first)
        {
            First = first;
        }

        public CircularFirst First { get; }
    }
}
