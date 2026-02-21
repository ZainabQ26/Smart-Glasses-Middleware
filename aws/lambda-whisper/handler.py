import json
import os
import tempfile
import sys

try:
    import whisper
except Exception as e:
    print("Warning: whisper import failed:", e, file=sys.stderr)
    whisper = None

import boto3

MODEL_NAME = os.environ.get("WHISPER_MODEL", "tiny")
_s3 = boto3.client('s3')


def lambda_handler(event, context=None):
    """
    Synchronous, on-demand transcription.

    Expected payload:
        { "s3_bucket": "<bucket>", "s3_key": "<key>" }

    Returns:
        { "transcript": "<text>" }  on success
        { "error": "<message>" }    on failure
    """
    s3_bucket = event.get('s3_bucket')
    s3_key = event.get('s3_key')

    if not s3_bucket or not s3_key:
        return {'error': 's3_bucket and s3_key are required'}

    if whisper is None:
        return {'error': 'whisper not installed in image'}

    tmp_path = None
    try:
        with tempfile.NamedTemporaryFile(suffix='.wav', delete=False) as tmp:
            tmp_path = tmp.name

        _s3.download_file(s3_bucket, s3_key, tmp_path)

        model = whisper.load_model(MODEL_NAME)
        result = model.transcribe(tmp_path, condition_on_previous_text=False, fp16=False)
        transcript = result.get('text', '').strip()

        return {'transcript': transcript}

    except Exception as ex:
        print(f"Transcription error: {ex}", file=sys.stderr)
        return {'error': str(ex)}

    finally:
        if tmp_path:
            try:
                os.unlink(tmp_path)
            except Exception:
                pass
        # Clean up the source audio from S3 - it was a one-time temp upload
        try:
            _s3.delete_object(Bucket=s3_bucket, Key=s3_key)
        except Exception as ex:
            print(f"Warning: failed to delete S3 object {s3_key}: {ex}", file=sys.stderr)


if __name__ == '__main__':
    # Local smoke-test: echo '{"s3_bucket":"b","s3_key":"k"}' | python handler.py
    payload = json.loads(sys.stdin.read())
    print(json.dumps(lambda_handler(payload)))