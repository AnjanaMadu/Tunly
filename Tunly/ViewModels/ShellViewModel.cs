using Tunly.Core;
using Tunly.Services;

namespace Tunly.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    public ConnectionService Connection { get; } = AppServices.Connection;
}
