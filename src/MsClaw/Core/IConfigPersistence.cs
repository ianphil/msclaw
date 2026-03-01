using MsClaw.Models;

namespace MsClaw.Core;

public interface IConfigPersistence
{
    MsClawConfig? Load();
    void Save(MsClawConfig config);
    void Clear();
}
