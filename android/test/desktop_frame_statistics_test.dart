import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/core/desktop_frame_statistics.dart';

FrameTiming frame(int start, {int span = 4000}) => FrameTiming(
  vsyncStart: start,
  buildStart: start,
  buildFinish: start + span ~/ 2,
  rasterStart: start + span ~/ 2,
  rasterFinish: start + span,
  rasterFinishWallTime: start + span,
);

void main() {
  test(
    'batched delivery uses engine timestamps rather than callback arrival',
    () {
      final together = DesktopFrameStatistics(),
          split = DesktopFrameStatistics();
      final frames = List.generate(121, (i) => frame(1000000 + i * 8333));
      together.add(frames, 2000000);
      split.add(frames.sublist(0, 60), 1000000);
      split.add(frames.sublist(60), 2000000);
      expect(together.sample(2000000), split.sample(2000000));
      expect(together.sample(2000000)!.count, 121);
      expect(together.sample(2000000)!.p95Ms, 4);
    },
  );
  test(
    'p95 exposes expensive rebuilt UI frames without claiming texture fps',
    () {
      final stats = DesktopFrameStatistics();
      final frames = List.generate(
        21,
        (i) => frame(
          1000000 + i * 10000 + (i >= 10 ? 60000 : 0),
          span: i >= 18 ? 30000 : 4000,
        ),
      );
      stats.add(frames, 2000000);
      final sample = stats.sample(2000000)!;
      expect(sample.p95Ms, 30);
    },
  );
  test(
    'stale batches, reset and duplicate timestamps cannot fabricate fresh UI samples',
    () {
      final stats = DesktopFrameStatistics();
      final frames = [frame(-20000), frame(-10000)];
      stats.add(frames, 1000);
      stats.add(frames, 3000000);
      expect(stats.sample(3001001), isNull);
      stats.reset();
      expect(stats.sample(0), isNull);
      stats.add([frame(100)], 1);
      expect(stats.sample(2), isNull);
    },
  );
  test('frame window stays bounded and drops old frame times', () {
    final stats = DesktopFrameStatistics();
    stats.add(List.generate(2000, (i) => frame(i)), 1);
    expect(stats.sample(1)!.count, 512);
    stats.reset();
    stats.add(List.generate(2000, (i) => frame(i * 10000)), 1);
    expect(stats.sample(1)!.count, lessThanOrEqualTo(102));
    expect(stats.sample(1)!.count, greaterThanOrEqualTo(100));
  });
}
