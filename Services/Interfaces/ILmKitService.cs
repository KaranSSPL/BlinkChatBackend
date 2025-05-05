using BlinkChatBackend.Models;

namespace BlinkChatBackend.Services.Interfaces;

public interface ILmKitService
{
    Task GenerateResponseAsync(UserRequest request, CancellationToken cancellationToken = default);

    #region [Load Models]

    Task LoadModelsFromConfigurationAsync();

    #endregion [Load Models]
}
