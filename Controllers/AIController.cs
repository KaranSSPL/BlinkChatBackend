using BlinkChatBackend.Models;
using BlinkChatBackend.Services;
using Microsoft.AspNetCore.Mvc;

namespace BlinkChatBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AIController(IAIService aIService) : ControllerBase
{
    [HttpPost("chat")]
    public async Task GetResponse(AIPrompt prompt)
    {
        if (prompt == null)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsync("Prompt cannot be null.");
            return;
        }

        try
        {
            Response.ContentType = "text/event-stream; charset=utf-8";

            await aIService.GetChatResponse(prompt, Response.Body);
        }
        catch (Exception)
        {
            if (!Response.HasStarted)
            {
                Response.StatusCode = StatusCodes.Status500InternalServerError;
                await Response.WriteAsync("An error occurred while processing the request.");
            }
        }
    }
}
