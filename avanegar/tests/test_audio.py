import unittest
from unittest.mock import MagicMock, patch
import numpy as np

from support import ensure_audio_import
ensure_audio_import()
from avanegar.audio import AudioClip, Recorder, sd  # noqa: E402


class RecorderTests(unittest.TestCase):
    def test_clip_duration(self):
        self.assertEqual(AudioClip(np.zeros(48000), 48000).duration, 1)

    def test_empty_stop(self):
        clip = Recorder().stop()
        self.assertEqual(clip.duration, 0)
        self.assertEqual(clip.samples.dtype, np.float32)

    def test_callback_copies_and_stop_clears(self):
        recorder = Recorder()
        data = np.full((160, 1), 0.1, dtype=np.float32)
        recorder._callback(data, 160, None, False)
        data[:] = 0
        clip = recorder.stop()
        self.assertTrue(np.allclose(clip.samples, 0.1))
        self.assertEqual(recorder.stop().duration, 0)

    def test_frame_limit_is_enforced_without_extra_memory(self):
        recorder = Recorder()
        recorder.sample_rate = 2
        with patch("avanegar.audio.MAX_RECORDING_SECONDS", 1):
            with self.assertRaises(sd.CallbackStop):
                recorder._callback(np.ones((8, 1), dtype=np.float32), 8, None, False)
        self.assertTrue(recorder.limit_reached)
        self.assertEqual(len(recorder.stop().samples), 2)

    def test_overflow_is_reported(self):
        recorder = Recorder()
        recorder._callback(np.zeros((10, 1), dtype=np.float32), 10, None, True)
        self.assertTrue(recorder.had_overflow)

    def test_missing_saved_device_does_not_silently_change_microphone(self):
        with patch.object(Recorder, "devices", return_value=[(0, "Other")]):
            with self.assertRaisesRegex(RuntimeError, "متصل نیست"):
                Recorder().start("Missing")

    def test_native_sample_rate_fallback_and_stream_cleanup(self):
        stream = MagicMock()
        with patch.object(sd, "check_input_settings", side_effect=sd.PortAudioError("unsupported")), \
             patch.object(sd, "query_devices", return_value={"default_samplerate": 48000}), \
             patch.object(sd, "InputStream", return_value=stream) as create:
            recorder = Recorder()
            recorder.start()
            self.assertEqual(recorder.sample_rate, 48000)
            self.assertEqual(create.call_args.kwargs["samplerate"], 48000)
            recorder.stop()
            stream.abort.assert_called_once()
            stream.stop.assert_not_called()
            stream.close.assert_called_once()

    def test_failed_start_closes_stream(self):
        stream = MagicMock()
        stream.start.side_effect = RuntimeError("permission denied")
        with patch.object(sd, "check_input_settings"), patch.object(sd, "InputStream", return_value=stream):
            recorder = Recorder()
            with self.assertRaises(RuntimeError):
                recorder.start()
            stream.close.assert_called_once()
            self.assertIsNone(recorder._stream)
