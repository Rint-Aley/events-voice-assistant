import asyncio
import subprocess
import unittest
from unittest.mock import patch

import app


def _ffprobe_result(stdout: str) -> subprocess.CompletedProcess:
    return subprocess.CompletedProcess(args=["ffprobe"], returncode=0, stdout=stdout, stderr="")


def _fake_ffprobe(format_stdout: str, packet_stdout: str):
    def run(args, **kwargs):
        if "format=duration" in args:
            return _ffprobe_result(format_stdout)
        if "packet=pts_time,duration_time" in args:
            return _ffprobe_result(packet_stdout)
        raise AssertionError(f"unexpected ffprobe args: {args}")

    return run


class DurationProbeTests(unittest.TestCase):
    def test_webm_without_format_duration_uses_packet_fallback(self):
        # Arrange
        format_output = "N/A\n"
        packet_output = "4.439000,0.060000\n4.619000,0.060000\n4.679000,0.060000\n"

        # Act
        with patch(
            "app.subprocess.run",
            side_effect=_fake_ffprobe(format_output, packet_output),
        ):
            duration_ms = app._probe_duration_ms_sync("problem.webm")

        # Assert
        self.assertEqual(4739, duration_ms)

    def test_webm_without_packets_is_still_unreadable(self):
        # Arrange
        format_output = "N/A\n"
        packet_output = ""

        # Act / Assert
        with patch(
            "app.subprocess.run",
            side_effect=_fake_ffprobe(format_output, packet_output),
        ):
            with self.assertRaises(ValueError):
                app._probe_duration_ms_sync("broken.webm")

    def test_format_duration_is_used_without_packet_fallback(self):
        # Arrange
        format_output = "4.739000\n"
        packet_output = "0.000000,0.060000\n"

        # Act
        with patch(
            "app.subprocess.run",
            side_effect=_fake_ffprobe(format_output, packet_output),
        ) as run_mock:
            duration_ms = app._probe_duration_ms_sync("ok.webm")

        # Assert
        self.assertEqual(4739, duration_ms)
        self.assertEqual(1, run_mock.call_count)

    def test_async_probe_returns_none_for_unreadable_audio(self):
        # Arrange
        format_output = "N/A\n"
        packet_output = ""

        # Act
        with patch(
            "app.subprocess.run",
            side_effect=_fake_ffprobe(format_output, packet_output),
        ):
            duration_ms = asyncio.run(app._probe_duration_ms("broken.webm"))

        # Assert: None превращается в 422 AUDIO_UNREADABLE в _transcribe_impl.
        self.assertIsNone(duration_ms)


if __name__ == "__main__":
    unittest.main()
