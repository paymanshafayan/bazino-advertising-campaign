import io
import unittest
from unittest.mock import MagicMock
import wave

import numpy as np
import requests
from support import ensure_audio_import
ensure_audio_import()
from avanegar.audio import AudioClip  # noqa: E402
from avanegar.cloud import BASE_URL, CLOUD_MODEL, CloudClient, CloudError, encode_audio  # noqa: E402


def response(status=200, payload=None, retry="1"):
    result = MagicMock()
    result.status_code = status
    result.headers = {"Retry-After": retry}
    result.json.return_value = {"text": "متن خروجی"} if payload is None else payload
    return result


class CloudTests(unittest.TestCase):
    def setUp(self):
        self.session = MagicMock()
        self.session.request.return_value = response()
        self.sleep = MagicMock()
        self.client = CloudClient(self.session, sleep=self.sleep)
        self.clip = AudioClip(np.full(48000, 0.1, dtype=np.float32), 48000)
        self.progress = MagicMock()

    def call(self):
        return self.client.transcribe(self.clip, "sk-fake-test-key", "fa", "آوانگار", self.progress)

    def test_wav_resampled_to_mono_16k_without_files(self):
        data = encode_audio(self.clip)
        with wave.open(io.BytesIO(data)) as wav:
            self.assertEqual(wav.getframerate(), 16000)
            self.assertEqual(wav.getnchannels(), 1)
            self.assertEqual(wav.getnframes(), 16000)
        self.assertLess(len(data), 33000)

    def test_max_duration_stays_under_upload_limit(self):
        clip = AudioClip(np.full(16000 * 300, 0.1, dtype=np.float32), 16000)
        self.assertLess(len(encode_audio(clip)), 10_000_000)

    def test_secure_fixed_endpoint_and_persian_hint(self):
        self.assertEqual(self.call(), "متن خروجی")
        args, kwargs = self.session.request.call_args
        self.assertEqual(args, ("POST", BASE_URL + "/audio/transcriptions"))
        self.assertFalse(kwargs["allow_redirects"])
        self.assertEqual(kwargs["timeout"], (10, 60))
        self.assertNotEqual(kwargs.get("verify"), False)
        self.assertEqual(kwargs["data"]["language"], "fa")
        self.assertEqual(kwargs["data"]["prompt"], "آوانگار")
        self.assertEqual(kwargs["data"]["model"], CLOUD_MODEL)
        self.assertEqual(kwargs["files"]["file"][0], "dictation.wav")

    def test_auto_language_omits_language_and_persian_hint(self):
        self.client.transcribe(self.clip, "fake-test-key", "auto", "آوانگار", self.progress)
        data = self.session.request.call_args.kwargs["data"]
        self.assertNotIn("language", data)
        self.assertNotIn("prompt", data)

    def test_connect_timeout_retries_once(self):
        self.session.request.side_effect = [requests.ConnectTimeout(), response()]
        self.call()
        self.assertEqual(self.session.request.call_count, 2)
        self.sleep.assert_called_once()

    def test_rate_limit_retry_is_bounded_and_reuses_full_audio(self):
        self.session.request.side_effect = [response(429), response()]
        self.call()
        self.assertEqual(self.session.request.call_count, 2)
        uploads = [call.kwargs["files"]["file"][1] for call in self.session.request.call_args_list]
        self.assertEqual(uploads[0], uploads[1])
        self.session.reset_mock()
        self.session.request.side_effect = [response(429), response(429)]
        with self.assertRaises(CloudError):
            self.call()
        self.assertEqual(self.session.request.call_count, 2)

    def test_quota_does_not_retry(self):
        self.session.request.return_value = response(429, {"error": {"code": "insufficient_quota"}})
        with self.assertRaisesRegex(CloudError, "اعتبار"):
            self.call()
        self.session.request.assert_called_once()

    def test_read_timeout_and_connection_loss_are_not_replayed(self):
        for error in (requests.ReadTimeout(), requests.ConnectionError()):
            self.session.reset_mock()
            self.session.request.side_effect = error
            with self.assertRaises(CloudError):
                self.call()
            self.session.request.assert_called_once()

    def test_server_auth_redirect_and_ssl_failures_not_retried(self):
        for status in (401, 403, 500, 302):
            self.session.reset_mock()
            self.session.request.return_value = response(status, {"error": {"message": "sk-secret private text"}})
            with self.assertRaises(CloudError) as failure:
                self.call()
            self.assertNotIn("sk-secret", str(failure.exception))
            self.session.request.assert_called_once()
        self.session.request.side_effect = requests.exceptions.SSLError()
        with self.assertRaisesRegex(CloudError, "TLS"):
            self.call()

    def test_malformed_response_fails_without_replay(self):
        for payload in ([], {}, {"text": 42}):
            self.session.reset_mock()
            self.session.request.return_value = response(200, payload)
            with self.assertRaises(CloudError):
                self.call()
            self.session.request.assert_called_once()

    def test_nonfinite_audio_rejected_before_request(self):
        self.clip.samples[0] = np.nan
        with self.assertRaises(CloudError):
            self.call()
        self.session.request.assert_not_called()

    def test_connection_check_is_get_without_audio(self):
        self.session.request.return_value = response(200, {"id": CLOUD_MODEL})
        self.client.check("fake-key-test", self.progress)
        call = self.session.request.call_args
        self.assertEqual(call.args[0], "GET")
        self.assertNotIn("files", call.kwargs)
