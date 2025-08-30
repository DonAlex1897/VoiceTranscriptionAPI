using Microsoft.AspNetCore.Mvc;
using System.Net.WebSockets;
using VoiceTranscriptionAPI.Services;

namespace VoiceTranscriptionAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RealTimeTranscriptionController : ControllerBase
{
    private readonly RealTimeTranscriptionService _transcriptionService;
    private readonly ILogger<RealTimeTranscriptionController> _logger;

    public RealTimeTranscriptionController(
        RealTimeTranscriptionService transcriptionService,
        ILogger<RealTimeTranscriptionController> logger)
    {
        _transcriptionService = transcriptionService;
        _logger = logger;
    }

    [HttpGet("ws")]
    public async Task<IActionResult> StartRealTimeTranscription()
    {
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            _logger.LogInformation("WebSocket connection established for real-time transcription");
            
            await _transcriptionService.HandleWebSocketConnection(webSocket, HttpContext.RequestAborted);
            return new EmptyResult();
        }
        else
        {
            return BadRequest("This endpoint only accepts WebSocket connections");
        }
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new { status = "Real-time transcription service is running", timestamp = DateTime.UtcNow });
    }
}
