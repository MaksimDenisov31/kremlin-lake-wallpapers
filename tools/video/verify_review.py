"""Decode the entire deliverable and check timing, stream settings and lake motion."""
from pathlib import Path
import json
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / '.tools/python'))
import av
import numpy as np

path = ROOT / '.tools/video-output/Kremlin-Lake-Wallpapers-Review-1080p.mp4'
samples = {}
with av.open(str(path)) as container:
    stream = container.streams.video[0]
    assert (stream.width, stream.height) == (1920, 1080)
    assert stream.average_rate == 30
    assert stream.codec_context.name == 'h264'
    assert stream.codec_context.format.name == 'yuv420p'
    assert abs(float(container.duration / av.time_base) - 45) < .04
    count = 0
    for frame in container.decode(video=0):
        assert abs(float(frame.time) - count / 30) < .001
        if count in (840, 870):
            samples[count] = frame.to_ndarray(format='rgb24')
        count += 1
    assert count == 1350, count
water = np.abs(samples[840][550:850, 100:800].astype('int16') - samples[870][550:850, 100:800])
assert np.count_nonzero(water > 8) > 1000, 'Lake water must move'
srt = (ROOT / 'media/youtube/review.ru.srt').read_text(encoding='utf-8')
assert srt.count('-->') == 10
report = {'file': path.name, 'duration_seconds': 45, 'decoded_frames': count,
          'resolution': '1920x1080', 'fps': 30, 'codec': 'H.264', 'pixel_format': 'yuv420p',
          'caption_blocks': 10, 'lake_motion': 'PASS', 'bytes': path.stat().st_size,
          'audio': 'none', 'full_decode': 'PASS'}
(path.parent / 'verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(report, ensure_ascii=False, indent=2))
