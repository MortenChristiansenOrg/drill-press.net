extern alias dep;
extern alias interop;

namespace Fixture;

public interface IContract { }

public class Consumer
{
    public IContract Generated => new Generated();
    public string Linked => dep::Linked.Shared.Value;
    public unsafe int* Pointer => null;

    public void Warning()
    {
        int unused;
    }

#if FIXTURE
    public string Value => string.Empty;
#endif
    public interop::External.IExternal? Interop => null;
}
