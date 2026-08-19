namespace LumaTherm.Infrastructure.Sensors;

public interface IMahmMemoryReader
{
    bool TryRead(out byte[] snapshot);
}
