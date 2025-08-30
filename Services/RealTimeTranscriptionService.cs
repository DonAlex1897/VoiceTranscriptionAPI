using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace VoiceTranscriptionAPI.Services;

public class RealTimeTranscriptionService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RealTimeTranscriptionService> _logger;

    public RealTimeTranscriptionService(IConfiguration configuration, ILogger<RealTimeTranscriptionService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task HandleWebSocketConnection(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var apiKey = _configuration["AssemblyAI:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            await SendError(webSocket, "AssemblyAI API key not configured", cancellationToken);
            return;
        }

        try
        {
            // For now, we'll implement a simplified real-time approach
            // that uses the regular AssemblyAI transcription API in smaller chunks
            _logger.LogInformation("WebSocket connection established for real-time transcription");

            // Send ready status to client
            await SendTranscriptionResult(webSocket, new { type = "ready" }, cancellationToken);

            // Buffer to accumulate audio data
            var audioBuffer = new List<byte>();
            var lastTranscriptionTime = DateTime.UtcNow;
            var transcriptionInterval = TimeSpan.FromSeconds(3); // Transcribe every 3 seconds

            // Handle incoming audio data from client
            var buffer = new byte[4096];
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // Accumulate audio data
                    var audioData = new byte[result.Count];
                    Array.Copy(buffer, audioData, result.Count);
                    audioBuffer.AddRange(audioData);

                    // Check if it's time to transcribe
                    if (DateTime.UtcNow - lastTranscriptionTime >= transcriptionInterval && audioBuffer.Count > 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var audioToTranscribe = audioBuffer.ToArray();
                                audioBuffer.Clear();
                                lastTranscriptionTime = DateTime.UtcNow;

                                // Convert PCM data back to WAV format for transcription
                                var wavData = CreateWavFromPcm(audioToTranscribe, 16000, 1, 16);
                                var transcript = await TranscribeAudioChunk(wavData);
                                
                                if (!string.IsNullOrWhiteSpace(transcript))
                                {
                                    await SendTranscriptionResult(webSocket, new
                                    {
                                        type = "final",
                                        text = transcript,
                                        confidence = 0.8 // Placeholder confidence
                                    }, cancellationToken);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error in chunk transcription");
                            }
                        }, cancellationToken);
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    // Handle control messages from client
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await HandleControlMessage(message);
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("WebSocket connection closed by client");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in real-time transcription");
            await SendError(webSocket, $"Internal error: {ex.Message}", cancellationToken);
        }
        finally
        {
            if (webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session ended", cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error closing WebSocket");
                }
            }
        }
    }

    private async Task<string> TranscribeAudioChunk(byte[] wavData)
    {
        try
        {
            var apiKey = _configuration["AssemblyAI:ApiKey"];
            var client = new AssemblyAI.AssemblyAIClient(apiKey!);

            // Save audio chunk to temporary file
            var tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".wav");
            await File.WriteAllBytesAsync(tempFilePath, wavData);

            try
            {
                // Upload the audio file
                using var audioStream = File.OpenRead(tempFilePath);
                var uploadedFile = await client.Files.UploadAsync(audioStream);

                // Create transcription request
                var request = new AssemblyAI.Transcripts.TranscriptParams
                {
                    AudioUrl = uploadedFile.UploadUrl,
                    LanguageCode = AssemblyAI.Transcripts.TranscriptLanguageCode.En
                };

                var transcript = await client.Transcripts.TranscribeAsync(request);
                transcript = await client.Transcripts.WaitUntilReadyAsync(transcript.Id);

                if (transcript.Status == AssemblyAI.Transcripts.TranscriptStatus.Completed && transcript.Text != null)
                {
                    return transcript.Text;
                }
            }
            finally
            {
                // Clean up temp file
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transcribing audio chunk");
        }

        return string.Empty;
    }

    private static byte[] CreateWavFromPcm(byte[] pcmData, int sampleRate, int channels, int bitsPerSample)
    {
        var wavHeaderSize = 44;
        var wavData = new byte[wavHeaderSize + pcmData.Length];

        // WAV header
        Array.Copy(Encoding.ASCII.GetBytes("RIFF"), 0, wavData, 0, 4);
        BitConverter.GetBytes(wavData.Length - 8).CopyTo(wavData, 4);
        Array.Copy(Encoding.ASCII.GetBytes("WAVE"), 0, wavData, 8, 4);
        Array.Copy(Encoding.ASCII.GetBytes("fmt "), 0, wavData, 12, 4);
        BitConverter.GetBytes(16).CopyTo(wavData, 16); // PCM format chunk size
        BitConverter.GetBytes((short)1).CopyTo(wavData, 20); // PCM format
        BitConverter.GetBytes((short)channels).CopyTo(wavData, 22);
        BitConverter.GetBytes(sampleRate).CopyTo(wavData, 24);
        BitConverter.GetBytes(sampleRate * channels * bitsPerSample / 8).CopyTo(wavData, 28);
        BitConverter.GetBytes((short)(channels * bitsPerSample / 8)).CopyTo(wavData, 32);
        BitConverter.GetBytes((short)bitsPerSample).CopyTo(wavData, 34);
        Array.Copy(Encoding.ASCII.GetBytes("data"), 0, wavData, 36, 4);
        BitConverter.GetBytes(pcmData.Length).CopyTo(wavData, 40);

        // PCM data
        Array.Copy(pcmData, 0, wavData, wavHeaderSize, pcmData.Length);

        return wavData;
    }

    private async Task HandleControlMessage(string message)
    {
        try
        {
            var controlMessage = JsonSerializer.Deserialize<JsonElement>(message);
            
            if (controlMessage.TryGetProperty("command", out var command))
            {
                switch (command.GetString())
                {
                    case "start":
                        _logger.LogInformation("Real-time transcription started");
                        break;
                    case "stop":
                        _logger.LogInformation("Real-time transcription stopped");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse control message: {Message}", message);
        }
        
        await Task.CompletedTask;
    }

    private async Task SendTranscriptionResult(WebSocket webSocket, object result, CancellationToken cancellationToken)
    {
        if (webSocket.State != WebSocketState.Open) return;

        try
        {
            var json = JsonSerializer.Serialize(result);
            var bytes = Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending transcription result");
        }
    }

    private async Task SendError(WebSocket webSocket, string errorMessage, CancellationToken cancellationToken)
    {
        await SendTranscriptionResult(webSocket, new
        {
            type = "error",
            message = errorMessage
        }, cancellationToken);
    }
}
