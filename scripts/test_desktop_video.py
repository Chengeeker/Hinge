"""Validate a bounded synthetic HDV1 recording without accessing a screen or phone."""
import argparse
import re
import struct
from pathlib import Path


def verify(path: Path, expected_fps: int) -> dict:
    count = 0
    times = []
    nal_types = set()
    dimensions = None
    with path.open('rb') as stream:
        while True:
            header = stream.read(32)
            if not header:
                break
            if len(header) != 32:
                raise ValueError('Truncated HDV1 header')
            magic, codec, flags, reserved, seq, pts, width, height, size = struct.unpack('>4sBBHIqIII', header)
            if magic != b'HDV1' or codec != 1 or flags & ~3 or reserved != 0:
                raise ValueError('Invalid H264 HDV1 header')
            if seq != count % (2**32) or pts < 0 or not 1 <= size <= 4 * 1024 * 1024:
                raise ValueError('Invalid sequence, timestamp or bounded payload length')
            if not (2 <= width <= 8192 and 2 <= height <= 8192 and width % 2 == height % 2 == 0):
                raise ValueError('Invalid dimensions')
            if dimensions is not None and dimensions != (width, height):
                raise ValueError('Unexpected size change in synthetic test')
            dimensions = (width, height)
            payload = stream.read(size)
            if len(payload) != size:
                raise ValueError('Truncated access unit')
            starts = list(re.finditer(b'\x00\x00(?:\x00)?\x01', payload))
            if not starts or starts[0].start() != 0:
                raise ValueError('Expected Annex B access unit')
            for start in starts:
                if start.end() >= len(payload):
                    raise ValueError('Empty NAL unit')
                nal_types.add(payload[start.end()] & 31)
            if not flags & 1 or flags & 2:
                if times and pts <= times[-1]:
                    raise ValueError('Non-monotonic no-B-frame timestamps')
                times.append(pts)
            count += 1
            if count > 100000:
                raise ValueError('Recording exceeds test packet limit')
    if len(times) < 2 or not {5, 7, 8}.issubset(nal_types):
        raise ValueError(f'Missing frames or SPS/PPS/IDR: {nal_types}')
    fps = (len(times) - 1) * 1000000 / (times[-1] - times[0])
    if not expected_fps * .9 <= fps <= expected_fps * 1.1:
        raise ValueError(f'Encoded cadence {fps:.2f} does not meet target {expected_fps}')
    return dict(packets=count, frames=len(times), dimensions=dimensions,
                fps=round(fps, 2), max_gap_ms=round(max(b-a for a,b in zip(times,times[1:])) / 1000, 2),
                nal_types=sorted(nal_types))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('file', type=Path)
    parser.add_argument('--fps', type=int, default=60)
    args = parser.parse_args()
    print(verify(args.file, args.fps))
