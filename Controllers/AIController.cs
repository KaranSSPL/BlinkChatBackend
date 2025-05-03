using Microsoft.AspNetCore.Mvc;

namespace BlinkChatBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AIController() : ControllerBase
{
    //[HttpPost("chat")]
    //public async Task GetResponse(AIPrompt prompt)
    //{
    //    if (prompt == null)
    //    {
    //        Response.StatusCode = StatusCodes.Status400BadRequest;
    //        await Response.WriteAsync("Invalid paramters.");
    //        return;
    //    }

    //    try
    //    {
    //        Response.ContentType = "text/event-stream; charset=utf-8";

    //        await aIService.GetChatResponse(prompt, Response.Body);
    //    }
    //    catch (Exception ex)
    //    {
    //        if (!Response.HasStarted)
    //        {
    //            Response.StatusCode = StatusCodes.Status500InternalServerError;
    //            await Response.WriteAsync(ex.Message);
    //        }
    //    }
    //}

    //[HttpPost("chat-rag-response")]
    //public async Task GetRAGResponse(string prompt)
    //{
    //    if (prompt == null || prompt == "")
    //    {
    //        Response.StatusCode = StatusCodes.Status400BadRequest;
    //        await Response.WriteAsync("Invalid paramters.");
    //        return;
    //    }

    //    try
    //    {
    //        Response.ContentType = "text/event-stream; charset=utf-8";
    //        aIService.GetRAGResponse(prompt, Response.Body);
    //    }
    //    catch (Exception ex)
    //    {
    //        if (!Response.HasStarted)
    //        {
    //            Response.StatusCode = StatusCodes.Status500InternalServerError;
    //            await Response.WriteAsync(ex.Message);
    //        }
    //    }
    //}

    //[HttpPost("chat-rag-response-vector")]
    //public async Task GetRAGResponseVector(string prompt)
    //{
    //    if (prompt == null || prompt == "")
    //    {
    //        Response.StatusCode = StatusCodes.Status400BadRequest;
    //        await Response.WriteAsync("Invalid paramters.");
    //        return;
    //    }

    //    try
    //    {
    //        Response.ContentType = "text/event-stream; charset=utf-8";
    //        await aIService.GetRAGResponseVector(prompt, Response.Body);
    //    }
    //    catch (Exception ex)
    //    {
    //        if (!Response.HasStarted)
    //        {
    //            Response.StatusCode = StatusCodes.Status500InternalServerError;
    //            await Response.WriteAsync(ex.Message);
    //        }
    //    }
    //}
}
