import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/app/desktop_gesture_surface.dart';

void main() {
  final absolute = <Offset>[], relative = <Offset>[];
  final clicks = <bool>[], drags = <bool>[];
  final wheels = <int>[];
  Future<void> mount(
    WidgetTester tester, {
    bool direct = true,
    bool enabled = true,
    double viewScale = 1,
    int viewRevision = 0,
    ValueNotifier<bool>? mouseButtonsHeld,
  }) async {
    absolute.clear();
    relative.clear();
    clicks.clear();
    drags.clear();
    wheels.clear();
    await tester.pumpWidget(
      MaterialApp(
        home: Align(
          alignment: Alignment.topLeft,
          child: SizedBox(
            width: 400,
            height: 300,
            child: DesktopGestureSurface(
              aspect: 2,
              enabled: enabled,
              direct: direct,
              zoomOnly: false,
              dragging: false,
              mouseButtonsHeld: mouseButtonsHeld,
              sensitivity: 3,
              viewScale: viewScale,
              viewRevision: viewRevision,
              onAbsolute: absolute.add,
              onRelative: relative.add,
              onClick: clicks.add,
              onDrag: drags.add,
              onScroll: wheels.add,
              child: const ColoredBox(
                key: ValueKey('video'),
                color: Colors.black,
              ),
            ),
          ),
        ),
      ),
    );
  }

  testWidgets('direct touch ignores letterbox and maps content', (
    tester,
  ) async {
    await mount(tester);
    await tester.tapAt(const Offset(200, 20));
    expect(absolute, isEmpty);
    expect(clicks, isEmpty);
    await tester.tapAt(const Offset(200, 150));
    expect(absolute.single.dx, closeTo(.5, .01));
    expect(absolute.single.dy, closeTo(.5, .01));
    expect(clicks, [false]);
  });
  for (final direct in [false, true]) {
    testWidgets(
      'repeated ordinary swipes only move the mouse (direct=$direct)',
      (tester) async {
        await mount(tester, direct: direct);
        for (var n = 0; n < 3; n++) {
          final finger = await tester.startGesture(const Offset(160, 150));
          await finger.moveBy(const Offset(40, 0));
          await finger.up();
        }
        expect(drags, isEmpty);
        expect(clicks, isEmpty);
        expect(direct ? absolute : relative, hasLength(3));
      },
    );
    testWidgets(
      'click then swipe does not silently hold left (direct=$direct)',
      (tester) async {
        await mount(tester, direct: direct);
        await tester.tapAt(const Offset(160, 150));
        final finger = await tester.startGesture(const Offset(160, 150));
        await finger.moveBy(const Offset(40, 0));
        await finger.up();
        expect(drags, isEmpty);
        expect(clicks, [false]);
      },
    );
    testWidgets(
      'two quick taps stay clicks rather than a drag mode (direct=$direct)',
      (tester) async {
        await mount(tester, direct: direct);
        await tester.tapAt(const Offset(160, 150));
        await tester.pump(const Duration(milliseconds: 100));
        await tester.tapAt(const Offset(160, 150));
        expect(clicks, [false, false]);
        expect(drags, isEmpty);
      },
    );
    testWidgets(
      'holding still does not press left until dragging (direct=$direct)',
      (tester) async {
        await mount(tester, direct: direct);
        final finger = await tester.startGesture(const Offset(160, 150));
        await tester.pump(const Duration(milliseconds: 600));
        expect(drags, isEmpty);
        await finger.moveBy(const Offset(40, 0));
        expect(drags, [true]);
        await finger.up();
        expect(drags, [true, false]);
        expect(clicks, isEmpty);
        final next = await tester.startGesture(const Offset(160, 150));
        await next.moveBy(const Offset(40, 0));
        await next.up();
        expect(drags, [
          true,
          false,
        ], reason: 'lifting ends drag; the next swipe is movement');
      },
    );
  }
  testWidgets('view presets resize the same display child without input', (
    tester,
  ) async {
    await mount(tester);
    final element = tester.element(find.byKey(const ValueKey('video')));
    await mount(tester, viewScale: 2, viewRevision: 1);
    expect(tester.element(find.byKey(const ValueKey('video'))), same(element));
    expect(
      tester.getRect(find.byKey(const ValueKey('video'))),
      const Rect.fromLTWH(-200, -150, 800, 600),
    );
    expect(absolute, isEmpty);
    expect(clicks, isEmpty);
    await mount(tester, viewRevision: 2);
    expect(
      tester.getRect(find.byKey(const ValueKey('video'))),
      const Rect.fromLTWH(0, 0, 400, 300),
    );
  });
  testWidgets(
    'direct long press drag releases left button and does not click',
    (tester) async {
      await mount(tester);
      final finger = await tester.startGesture(const Offset(160, 150));
      await tester.pump(const Duration(milliseconds: 460));
      await finger.moveBy(const Offset(40, 0));
      await finger.up();
      expect(drags, [true, false]);
      expect(clicks, isEmpty);
    },
  );
  testWidgets('pointer long press drags and cancels safely', (tester) async {
    await mount(tester, direct: false);
    final finger = await tester.startGesture(const Offset(160, 150));
    await tester.pump(const Duration(milliseconds: 460));
    await finger.moveBy(const Offset(10, 0));
    await finger.cancel();
    expect(drags, [true, false]);
    expect(relative.single.dx, 30);
    expect(absolute, isEmpty);
    expect(clicks, isEmpty);
  });
  testWidgets(
    'held mapped mouse drags relatively without jumping or clicking',
    (tester) async {
      final held = ValueNotifier(false);
      await mount(tester, mouseButtonsHeld: held);
      final finger = await tester.startGesture(const Offset(160, 150));
      held.value = true;
      await tester.pump(const Duration(milliseconds: 500));
      await finger.moveBy(const Offset(20, 10));
      held.value = false;
      await finger.up();
      expect(relative, [const Offset(60, 30)]);
      expect(absolute, isEmpty);
      expect(
        drags,
        isEmpty,
        reason: 'must not add a left button while right is held',
      );
      expect(
        clicks,
        isEmpty,
        reason: 'releasing the mapped button first is not a canvas tap',
      );
      await tester.pumpWidget(const SizedBox());
      held.dispose();
    },
  );
  testWidgets('mapped click while a finger rests cancels its pending tap', (
    tester,
  ) async {
    final held = ValueNotifier(false);
    await mount(tester, mouseButtonsHeld: held);
    final finger = await tester.startGesture(const Offset(160, 150));
    held.value = true;
    held.value = false;
    await finger.up();
    expect(clicks, isEmpty);
    expect(drags, isEmpty);
    await tester.pumpWidget(const SizedBox());
    held.dispose();
  });
  testWidgets(
    'pointer movement also works in letterbox while direct taps stay bounded',
    (tester) async {
      await mount(tester, direct: false);
      final finger = await tester.startGesture(const Offset(200, 20));
      await finger.moveBy(const Offset(10, 0));
      await finger.up();
      expect(relative, [const Offset(30, 0)]);
      expect(absolute, isEmpty);
      expect(clicks, isEmpty);
    },
  );
  testWidgets('pinch locks zoom without sending wheel or clicks', (
    tester,
  ) async {
    await mount(tester);
    final a = await tester.startGesture(const Offset(120, 150), pointer: 1);
    final b = await tester.startGesture(const Offset(280, 150), pointer: 2);
    await a.moveBy(const Offset(-40, 0));
    await b.moveBy(const Offset(40, 0));
    await tester.pump();
    await a.up();
    await b.up();
    expect(wheels, isEmpty);
    expect(drags, isEmpty);
    expect(clicks, isEmpty);
    absolute.clear();
    await tester.tapAt(const Offset(120, 150));
    // The tap is inverse-mapped through the enlarged viewport.
    expect(absolute.single.dx, greaterThan(.30));
    expect(absolute.single.dx, lessThan(.50));
  });
  testWidgets('continuous pinch keeps display geometry stable across frames', (
    tester,
  ) async {
    await mount(tester);
    Rect bounds() => tester.getRect(find.byKey(const ValueKey('video')));
    expect(bounds(), const Rect.fromLTWH(0, 0, 400, 300));
    final a = await tester.startGesture(const Offset(120, 150), pointer: 1);
    final b = await tester.startGesture(const Offset(280, 150), pointer: 2);
    for (final distance in [20.0, 40.0, 60.0, 40.0, 20.0, 0.0]) {
      await a.moveTo(Offset(120 - distance, 150));
      await tester.pump();
      await b.moveTo(Offset(280 + distance, 150));
      await tester.pump();
      final scale = (160 + distance * 2) / 160;
      expect(bounds().width, closeTo(400 * scale, .001));
      expect(bounds().center, const Offset(200, 150));
    }
    expect(bounds(), const Rect.fromLTWH(0, 0, 400, 300));
    await a.up();
    await b.up();
  });
  testWidgets('parallel fingers scroll, two finger tap right clicks', (
    tester,
  ) async {
    await mount(tester, direct: false);
    var a = await tester.startGesture(const Offset(120, 130), pointer: 1);
    var b = await tester.startGesture(const Offset(280, 130), pointer: 2);
    await a.moveBy(const Offset(0, 25));
    await b.moveBy(const Offset(0, 25));
    await a.up();
    await b.up();
    expect(wheels, isNotEmpty);
    expect(clicks, isEmpty);
    expect(relative, isEmpty);
    a = await tester.startGesture(const Offset(120, 130), pointer: 1);
    b = await tester.startGesture(const Offset(280, 130), pointer: 2);
    await a.up();
    await b.up();
    expect(clicks, [true]);
  });
  testWidgets('watching sends no mouse actions', (tester) async {
    await mount(tester, enabled: false);
    await tester.dragFrom(const Offset(160, 150), const Offset(50, 0));
    await tester.tapAt(const Offset(200, 150));
    expect(absolute, isEmpty);
    expect(relative, isEmpty);
    expect(drags, isEmpty);
    expect(clicks, isEmpty);
  });
}
