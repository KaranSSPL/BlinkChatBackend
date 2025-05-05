using BlinkChatBackend.Models;

namespace BlinkChatBackend.Services.Interfaces;

public interface ILmKitService
{
    Task GenerateResponseAsync(UserRequest request, CancellationToken cancellationToken = default);

    #region [Load Models]

    void LoadModelsFromConfiguration();

    #endregion [Load Models]
}
