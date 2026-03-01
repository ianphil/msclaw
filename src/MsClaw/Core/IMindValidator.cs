using MsClaw.Models;

namespace MsClaw.Core;

public interface IMindValidator
{
    MindValidationResult Validate(string mindRoot);
}
