import os
import sys
import time
import zipfile
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from io import BytesIO

from flask import Flask, request, jsonify

MODEL_NAME = os.environ.get("VOSK_MODEL_NAME", "vosk-model-small-en-us-0.15")
MODEL_DIR = os.environ.get("MODEL_DIR", f"/models/{MODEL_NAME}")
MODEL_URL = os.environ.get("VOSK_MODEL_URL", f"https://alphacephei.com/vosk/models/{MODEL_NAME}.zip")
PORT = int(os.environ.get("PORT", "8080"))


def ensure_model():
    if os.path.exists(MODEL_DIR) and os.listdir(MODEL_DIR):
        print(f"Vosk model already present at {MODEL_DIR}")
        return True

    os.makedirs(os.path.dirname(MODEL_DIR), exist_ok=True)
    zip_path = f"/tmp/{MODEL_NAME}.zip"
    try:
        import requests
        print(f"Downloading Vosk model from {MODEL_URL} to {zip_path}")
        r = requests.get(MODEL_URL, stream=True, timeout=300)
        r.raise_for_status()
        with open(zip_path, "wb") as f:
            for chunk in r.iter_content(chunk_size=8192):
                if chunk:
                    f.write(chunk)
        print("Extracting model...")
        with zipfile.ZipFile(zip_path, 'r') as z:
            z.extractall(os.path.dirname(MODEL_DIR))
        print("Model downloaded and extracted")
        return True
    except Exception as e:
        print("Failed to download/extract Vosk model:", e, file=sys.stderr)
        return False


app = Flask(__name__)


@app.route('/health')
def health():
    ok = os.path.exists(MODEL_DIR) and os.listdir(MODEL_DIR)
    return ("ok", 200) if ok else ("not ready", 503)


@app.route('/transcribe', methods=['POST'])
def transcribe():
    try:
        if 'file' not in request.files:
            return jsonify({"error": "no file"}), 400
        f = request.files['file']
        data = f.read()
        from vosk import Model, KaldiRecognizer
        import wave

        wf = BytesIO(data)
        # write to temp file because wave expects a file-like with seek
        tmp_path = '/tmp/input.wav'
        with open(tmp_path, 'wb') as tf:
            tf.write(data)
        wf2 = wave.open(tmp_path, 'rb')

        if not os.path.exists(MODEL_DIR):
            return jsonify({"error": "model missing"}), 500

        model = Model(MODEL_DIR)
        rec = KaldiRecognizer(model, wf2.getframerate())
        results = []
        while True:
            data_chunk = wf2.readframes(4000)
            if len(data_chunk) == 0:
                break
            if rec.AcceptWaveform(data_chunk):
                res = rec.Result()
                results.append(res)
        final = rec.FinalResult()
        # Extract text from JSON results
        import json
        texts = []
        for r in results + [final]:
            try:
                j = json.loads(r)
                if 'text' in j:
                    texts.append(j['text'])
            except Exception:
                pass
        text = ' '.join(t for t in texts if t)
        return jsonify({"text": text})
    except Exception as e:
        return jsonify({"error": str(e)}), 500


if __name__ == '__main__':
    ok = ensure_model()
    if not ok:
        print("Model download failed; exiting", file=sys.stderr)
        sys.exit(1)
    app.run(host='0.0.0.0', port=PORT)
