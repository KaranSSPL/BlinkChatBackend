using BlinkChatBackend.Models;
using BlinkChatBackend.Services;
using LMKit.Model;
using LMKit.TextGeneration.Sampling;
using LMKit.TextGeneration;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Text;
using LMKit.TextGeneration.Chat;

namespace BlinkChatBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AIController : ControllerBase
    {
        private readonly IAIService _aIService;
        public AIController(IAIService aIService)
        {
            _aIService = aIService;
        }

        [HttpPost("get-response")]
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
                Response.ContentType = "text/plain";
                Response.Headers.Add("Transfer-Encoding", "chunked");

                await _aIService.GetResponse(prompt, Response.Body);
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
}
