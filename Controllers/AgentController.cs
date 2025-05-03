using BlinkChatBackend.Models;
using BlinkChatBackend.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BlinkChatBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentController(ILmKitService lmKitService) : ControllerBase
{
    [HttpPost]
    public async Task GetAgentResponseAsync(UserRequest prompt, CancellationToken cancellationToken) =>
        await lmKitService.GenerateResponseAsync(prompt, cancellationToken);
}
