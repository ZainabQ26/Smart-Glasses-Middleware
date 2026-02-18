import base64
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

def lambda_handler(event):
    # If invoked synchronously with s3_bucket/s3_key payload, handle single job and return transcript
    body_top = event if isinstance(event, dict) else {}
    if isinstance(body_top, dict) and body_top.get('s3_bucket') and body_top.get('s3_key') and 'Records' not in body_top:
        s3_bucket = body_top.get('s3_bucket')
        s3_key = body_top.get('s3_key')
        job_id = body_top.get('job_id')

        tmp_path = None
        try:
            with tempfile.NamedTemporaryFile(suffix='.wav', delete=False) as tmp:
                tmp_path = tmp.name
            _s3.download_file(s3_bucket, s3_key, tmp_path)

            if whisper is None:
                return { 'statusCode': 500, 'body': json.dumps({ 'error': 'whisper not installed in image' }) }

            model = whisper.load_model(MODEL_NAME)
            res = model.transcribe(tmp_path)
            transcript = res.get('text', '')

            result_key = None
            if job_id:
                result_key = f"transcribe-results/{job_id}.json"
                _s3.put_object(Bucket=s3_bucket, Key=result_key, Body=json.dumps({ 'transcript': transcript }))

            # delete original object
            try:
                _s3.delete_object(Bucket=s3_bucket, Key=s3_key)
            except Exception as ex:
                print('Warning: failed to delete s3 object', ex, file=sys.stderr)

            return { 'statusCode': 200, 'body': json.dumps({ 'transcript': transcript, 'result_key': result_key }) }
        except Exception as ex:
            return { 'statusCode': 500, 'body': json.dumps({ 'error': str(ex) }) }
        finally:
            if tmp_path:
                try:
                    os.unlink(tmp_path)
                except Exception:
                    pass

    # SQS event: Records array
    if isinstance(event, dict) and 'Records' in event:
        results = []
        for rec in event['Records']:
            try:
                body = json.loads(rec.get('body', '{}'))
            except Exception:
                body = {}

            s3_bucket = body.get('s3_bucket')
            s3_key = body.get('s3_key')
            job_id = body.get('job_id')

            tmp_path = None
            try:
                if s3_bucket and s3_key:
                    with tempfile.NamedTemporaryFile(suffix='.wav', delete=False) as tmp:
                        tmp_path = tmp.name
                    _s3.download_file(s3_bucket, s3_key, tmp_path)
                else:
                    results.append({ 'error': 's3_bucket and s3_key required' })
                    continue

                if whisper is None:
                    results.append({ 'error': 'whisper not available' })
                    continue

                model = whisper.load_model(MODEL_NAME)
                res = model.transcribe(tmp_path)
                transcript = res.get('text', '')

                # write transcript result to S3
                if job_id:
                    result_key = f"transcribe-results/{job_id}.json"
                    _s3.put_object(Bucket=s3_bucket, Key=result_key, Body=json.dumps({ 'transcript': transcript }))

                # delete original object
                try:
                    _s3.delete_object(Bucket=s3_bucket, Key=s3_key)
                except Exception as ex:
                    print('Warning: failed to delete s3 object', ex, file=sys.stderr)

                results.append({ 'job_id': job_id, 'transcript': transcript })
            except Exception as ex:
                results.append({ 'error': str(ex) })
            finally:
                if tmp_path:
                    try:
                        os.unlink(tmp_path)
                    except Exception:
                        pass

        return { 'statusCode': 200, 'body': json.dumps(results) }

    # Fallback for single synchronous invocation (API GW style)
    body = event if isinstance(event, dict) else {}
    if 'body' in body and body['body']:
        try:
            body = json.loads(body['body'])
        except Exception:
            body = body['body']

    audio_b64 = body.get('audio_base64') if isinstance(body, dict) else None
    if not audio_b64:
        return { 'statusCode': 400, 'body': json.dumps({ 'error': 'audio_base64 required' }) }

    audio_bytes = base64.b64decode(audio_b64)
    with tempfile.NamedTemporaryFile(suffix='.wav', delete=False) as tmp:
        tmp.write(audio_bytes)
        tmp_path = tmp.name

    try:
        if whisper is None:
            return { 'statusCode': 500, 'body': json.dumps({ 'error': 'whisper not installed in image' }) }

        model = whisper.load_model(MODEL_NAME)
        result = model.transcribe(tmp_path)
        transcript = result.get('text', '')

        return { 'statusCode': 200, 'body': json.dumps({ 'transcript': transcript }) }
    except Exception as ex:
        return { 'statusCode': 500, 'body': json.dumps({ 'error': str(ex) }) }
    finally:
        try:
            os.unlink(tmp_path)
        except Exception:
            pass


if __name__ == '__main__':
    # allow running locally for testing: read base64 from stdin
    raw = sys.stdin.read()
    try:
        payload = json.loads(raw)
    except Exception:
        payload = { 'audio_base64': raw }

    out = lambda_handler(payload)
    print(json.dumps(out))
