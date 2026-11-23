"""Encode the application's real scene renderer as a captioned YouTube overview."""
from pathlib import Path
import subprocess
import sys
from fractions import Fraction

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / '.tools/python'))
import av
import numpy as np

OUT = ROOT / '.tools/video-output'
STAGE = ROOT / '.tools/video-stage'
OUT.mkdir(parents=True, exist_ok=True)
captions = ROOT / 'media/youtube/captions.tsv'

def stamp(seconds):
    return f'00:00:{int(seconds):02d},000'

srt = '\n\n'.join(f'{i}\n{stamp(start)} --> {stamp(end)}\n{text}'
                   for i, (start, end, text) in enumerate(
                       (line.split('\t') for line in captions.read_text(encoding='utf-8').splitlines()), 1)) + '\n'
(ROOT / 'media/youtube/review.ru.srt').write_text(srt, encoding='utf-8')

output = OUT / 'Kremlin-Lake-Wallpapers-Review-1080p.mp4'
with (OUT / 'render.log').open('wb') as log:
    proc = subprocess.Popen([str(STAGE / 'RenderReview.exe'), str(captions), str(OUT)], stdout=subprocess.PIPE, stderr=log)
    try:
        with av.open(str(output), 'w', options={'movflags': '+faststart'}) as container:
            container.metadata['title'] = 'Кремль и отдых у озера — интерактивные обои для Windows'
            container.metadata['comment'] = 'Краткий обзор с русскими субтитрами. Без озвучки.'
            stream = container.add_stream('libx264', rate=30)
            stream.width, stream.height, stream.pix_fmt = 1920, 1080, 'yuv420p'
            stream.options = {'crf': '18', 'preset': 'medium', 'profile': 'high'}
            for index in range(1350):
                size = 1920 * 1080 * 4
                data = bytearray()
                while len(data) < size:
                    chunk = proc.stdout.read(size-len(data))
                    if not chunk:
                        raise RuntimeError(f'Renderer ended at frame {index}; see render.log')
                    data.extend(chunk)
                frame = av.VideoFrame.from_ndarray(np.frombuffer(data, dtype=np.uint8).reshape(1080, 1920, 4), format='bgra')
                frame.pts, frame.time_base = index, Fraction(1, 30)
                for packet in stream.encode(frame):
                    container.mux(packet)
                if index % 150 == 0:
                    print(f'Encoded {index}/1350', flush=True)
            for packet in stream.encode():
                container.mux(packet)
        if proc.wait() != 0:
            raise RuntimeError('Renderer failed; see render.log')
    finally:
        if proc.poll() is None:
            proc.terminate()
print(output)
