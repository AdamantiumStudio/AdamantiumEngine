namespace Adamantium.CoreTests.Services
{
    public class LocatingSecond
    {
        public LocatingSecond(LocatingFirst first)
        {
            First = first;
        }

        public LocatingFirst First { get; }
    }
}
