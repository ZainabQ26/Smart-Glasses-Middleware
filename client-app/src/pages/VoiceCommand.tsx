import { useState, useRef } from 'react';

export default function VoiceCommand() {
  const [recording, setRecording] = useState(false);
  const [transcript, setTranscript] = useState('');
  const [response, setResponse] = useState('');
  const [loading, setLoading] = useState(false);
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const chunksRef = useRef<Blob[]>([]);

  const startRecording = async () => {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const mediaRecorder = new MediaRecorder(stream);
      mediaRecorderRef.current = mediaRecorder;
      chunksRef.current = [];

      mediaRecorder.ondataavailable = (e) => {
        if (e.data.size > 0) {
          chunksRef.current.push(e.data);
        }
      };

      mediaRecorder.onstop = async () => {
        const audioBlob = new Blob(chunksRef.current, { type: 'audio/wav' });
        await processAudio(audioBlob);
        stream.getTracks().forEach(track => track.stop());
      };

      mediaRecorder.start();
      setRecording(true);
    } catch (err) {
      console.error('Failed to start recording:', err);
      alert('Microphone access denied or unavailable');
    }
  };

  const stopRecording = () => {
    if (mediaRecorderRef.current && recording) {
      mediaRecorderRef.current.stop();
      setRecording(false);
    }
  };

  const processAudio = async (audioBlob: Blob) => {
    setLoading(true);
    setTranscript('');
    setResponse('');

    try {
      // Step 1: Transcribe audio
      const formData = new FormData();
      formData.append('audio', audioBlob, 'recording.wav');

      const transcriptRes = await fetch('/api/transcribe', {
        method: 'POST',
        body: formData,
      });

      if (!transcriptRes.ok) throw new Error('Transcription failed');
      
      const { text } = await transcriptRes.json();
      setTranscript(text);

      // Step 2: Send to AI
      const aiRes = await fetch('/api/chat/ask', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ prompt: text }),
      });

      if (!aiRes.ok) throw new Error('AI request failed');
      
      const { response: aiResponse } = await aiRes.json();
      setResponse(aiResponse);
    } catch (err) {
      console.error('Processing failed:', err);
      setResponse('❌ Error: ' + (err as Error).message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="px-4">
      <h1 className="text-3xl font-bold text-gray-900 mb-8">Voice Commands</h1>

      <div className="bg-white rounded-lg shadow p-6 mb-6">
        <div className="flex flex-col items-center space-y-4">
          <button
            onClick={recording ? stopRecording : startRecording}
            disabled={loading}
            className={`w-24 h-24 rounded-full flex items-center justify-center text-white text-3xl transition-all ${
              recording 
                ? 'bg-red-500 hover:bg-red-600 animate-pulse' 
                : loading 
                ? 'bg-gray-400 cursor-not-allowed'
                : 'bg-blue-500 hover:bg-blue-600'
            }`}
          >
            {loading ? '⏳' : recording ? '⏹️' : '🎤'}
          </button>
          
          <p className="text-gray-600 text-center">
            {loading ? 'Processing...' : recording ? 'Recording... Click to stop' : 'Click to start recording'}
          </p>
        </div>
      </div>

      {transcript && (
        <div className="bg-blue-50 rounded-lg p-6 mb-6">
          <h2 className="text-lg font-semibold text-gray-900 mb-2">You said:</h2>
          <p className="text-gray-700">{transcript}</p>
        </div>
      )}

      {response && (
        <div className="bg-green-50 rounded-lg p-6">
          <h2 className="text-lg font-semibold text-gray-900 mb-2">Response:</h2>
          <p className="text-gray-700 whitespace-pre-wrap">{response}</p>
        </div>
      )}
    </div>
  );
}
