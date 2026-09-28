import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/core/cloud_relay.dart';

CloudRelayProgress progress(int percentage) => CloudRelayProgress(
  transferId: 'relay-transfer',
  fileName: 'large-file.apk',
  bytesTransferred: percentage * 100,
  totalBytes: 10000,
  stage: CloudRelayProgressStage.downloading,
);

void main() {
  test('progress stays monotonic when callbacks arrive out of order', () {
    final guard = CloudRelayProgressGuard();

    final percentages = [21, 48, 22, 49]
        .map(
          (value) => guard
              .normalize(progress(value), direction: 'download')
              .percentage
              .round(),
        )
        .toList();

    expect(percentages, [21, 48, 48, 49]);
  });

  test('directions are isolated and a finished transfer can retry', () {
    final guard = CloudRelayProgressGuard();
    guard.normalize(progress(48), direction: 'download');

    expect(
      guard.normalize(progress(21), direction: 'upload').percentage.round(),
      21,
    );
    guard.clear(transferId: 'relay-transfer', direction: 'download');
    expect(
      guard.normalize(progress(21), direction: 'download').percentage.round(),
      21,
    );
  });
}
