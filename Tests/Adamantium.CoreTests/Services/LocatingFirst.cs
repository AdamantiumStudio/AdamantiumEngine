using Adamantium.Core.DependencyInjection;

namespace Adamantium.CoreTests.Services
{
    public class LocatingFirst
    {
        public LocatingFirst(IDependencyResolver resolver)
        {
            Second = resolver.Resolve<LocatingSecond>();
        }

        public LocatingSecond Second { get; }
    }
}
