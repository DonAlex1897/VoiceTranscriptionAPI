using Microsoft.AspNetCore.Mvc;
using AssemblyAI;
using AssemblyAI.Transcripts;

namespace VoiceTranscriptionAPI.Controllers;

public class TranscriptionSegment
{
    public string Text { get; set; } = string.Empty;
    public string SpeakerId { get; set; } = string.Empty;
    public double StartTime { get; set; }
    public double EndTime { get; set; }
}

public class TranscriptionResult
{
    public string FullText { get; set; } = string.Empty;
    public List<TranscriptionSegment> Segments { get; set; } = new();
}

[ApiController]
[Route("api/[controller]")]
public class TranscriptionController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TranscriptionController> _logger;

    public TranscriptionController(IConfiguration configuration, ILogger<TranscriptionController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("transcribe")]
    public async Task<IActionResult> TranscribeAudio(IFormFile audioFile)
    {
        if (audioFile == null || audioFile.Length == 0)
        {
            return BadRequest("No audio file provided");
        }

        var allowedExtensions = new[] { ".wav", ".mp3", ".m4a", ".flac", ".ogg", ".aac" };
        var fileExtension = Path.GetExtension(audioFile.FileName).ToLowerInvariant();
        
        if (!allowedExtensions.Contains(fileExtension))
        {
            return BadRequest("Unsupported audio format. Supported formats: WAV, MP3, M4A, FLAC, OGG, AAC");
        }

        try
        {
            var tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + fileExtension);
            await using (var stream = new FileStream(tempFilePath, FileMode.Create))
            {
                await audioFile.CopyToAsync(stream);
            }

            var result = await TranscribeAudioWithAssemblyAI(tempFilePath);
            
            System.IO.File.Delete(tempFilePath);

            return Ok(new { 
                transcription = result.FullText,
                segments = result.Segments,
                fileName = audioFile.FileName 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transcribing audio file");
            return StatusCode(500, "An error occurred while transcribing the audio");
        }
    }

    private async Task<TranscriptionResult> TranscribeAudioWithAssemblyAI(string audioFilePath)
    {
        var apiKey = _configuration["AssemblyAI:ApiKey"];

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("AssemblyAI API key configuration is missing");
        }

        var fileInfo = new FileInfo(audioFilePath);
        _logger.LogInformation($"Starting AssemblyAI transcription with speaker diarization for file: {audioFilePath}, Size: {fileInfo.Length} bytes");

        try
        {
            var client = new AssemblyAIClient(apiKey);

            // Upload the audio file
            using var audioStream = System.IO.File.OpenRead(audioFilePath);
            var uploadedFile = await client.Files.UploadAsync(audioStream);

            // Create transcription request with speaker diarization
            var request = new TranscriptParams
            {
                AudioUrl = uploadedFile.UploadUrl,
                SpeakerLabels = true, // Enable speaker diarization
                LanguageCode = TranscriptLanguageCode.En
            };

            var transcript = await client.Transcripts.TranscribeAsync(request);

            // Wait for completion
            transcript = await client.Transcripts.WaitUntilReadyAsync(transcript.Id);

            if (transcript.Status == TranscriptStatus.Error)
            {
                throw new InvalidOperationException($"Transcription failed: {transcript.Error}");
            }

            var result = new TranscriptionResult();
            var segments = new List<TranscriptionSegment>();
            var fullTextBuilder = new List<string>();

            if (transcript.Utterances != null)
            {
                foreach (var utterance in transcript.Utterances)
                {
                    var segment = new TranscriptionSegment
                    {
                        Text = utterance.Text,
                        SpeakerId = utterance.Speaker,
                        StartTime = utterance.Start / 1000.0, // Convert from milliseconds to seconds
                        EndTime = utterance.End / 1000.0
                    };

                    segments.Add(segment);
                    fullTextBuilder.Add($"[{segment.SpeakerId}]: {segment.Text}");
                }
            }

            result.Segments = segments;
            result.FullText = string.Join("\n", fullTextBuilder);

            _logger.LogInformation($"AssemblyAI transcription completed. {segments.Count} segments found with speaker identification");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AssemblyAI transcription");
            throw;
        }
    }


}
