using MsClaw.Models;

namespace MsClaw.Core;

public interface IBootstrapOrchestrator
{
    BootstrapResult? Run(string[] args);
}
