# Voice Transcription API

A .NET Web API that transcribes voice files using Azure Cognitive Services Speech-to-Text.

## Setup

1. **Azure Speech Service Configuration:**
   - Create an Azure Speech Service resource in the Azure portal
   - Update `appsettings.json` with your Azure Speech service key and region:
   ```json
   "AzureSpeech": {
     "Key": "your-azure-speech-key",
     "Region": "your-azure-region"
   }
   ```

2. **Run the API:**
   ```bash
   dotnet run
   ```

## Usage

### Transcribe Audio File

**Endpoint:** `POST /api/transcription/transcribe`

**Content-Type:** `multipart/form-data`

**Parameters:**
- `audioFile` (form file): The audio file to transcribe

**Supported formats:** WAV, MP3, FLAC, OGG, M4A

**Example using curl:**
```bash
curl -X POST \
  http://localhost:5000/api/transcription/transcribe \
  -H "Content-Type: multipart/form-data" \
  -F "audioFile=@path/to/your/audio.wav"
```

**Response:**
```json
{
  "transcription": "Hello, this is the transcribed text from the audio file.",
  "fileName": "audio.wav"
}
```

## Features

- Supports multiple audio formats
- File validation and error handling
- Azure Speech Services integration
- Temporary file cleanup
- Comprehensive logging
