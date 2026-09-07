import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/core/device_identity_manager.dart';
import 'package:hinge/core/protocol_frame.dart';
import 'package:hinge/core/screen_stream_capturer.dart';
import 'package:hinge/core/screen_stream_manager.dart';
import 'package:hinge/core/screen_stream_model.dart';
import 'package:hinge/core/session_manager.dart';

void main() {
  group('Screen Stream Core Tests', () {
    test('ScreenStreamPacket binary serialization and tryParse roundtrip', () {
      final nalu = Uint8List.fromList([
        0x00,
        0x00,
        0x00,
        0x01,
        0x65,
        0x88,
        0x84,
        0x00,
      ]);
      final packet = ScreenStreamPacket(
        subtype: ScreenStreamSubtype.keyFrame,
        flags: ScreenStreamFlags.keyFrame | ScreenStreamFlags.endOfFrame,
        streamId: 5,
        sequenceNumber: 42,
        timestampUs: 9876543210,
        payload: nalu,
      );

      final bytes = packet.serialize();
      expect(bytes.length, equals(20 + nalu.length));

      final parsed = ScreenStreamPacket.tryParse(bytes);
      expect(parsed, isNotNull);
      expect(parsed!.subtype, equals(ScreenStreamSubtype.keyFrame));
      expect(
        parsed.flags,
        equals(ScreenStreamFlags.keyFrame | ScreenStreamFlags.endOfFrame),
      );
      expect(parsed.streamId, equals(5));
      expect(parsed.sequenceNumber, equals(42));
      expect(parsed.payloadLength, equals(nalu.length));
      expect(parsed.timestampUs, equals(9876543210));
      expect(parsed.payload, equals(nalu));
    });

    test('ScreenStreamPacket rejects truncated header', () {
      final truncated = Uint8List(19);
      final parsed = ScreenStreamPacket.tryParse(truncated);
      expect(parsed, isNull);
    });

    test('ScreenStreamPacket rejects truncated payload', () {
      final packet = ScreenStreamPacket(
        subtype: ScreenStreamSubtype.interFrame,
        streamId: 1,
        sequenceNumber: 1,
        timestampUs: 1000,
        payload: Uint8List(20),
      );

      final bytes = packet.serialize();
      // Pass only 10 bytes of payload instead of the 20 bytes written in the header
      final sliced = bytes.sublist(0, 20 + 10);
      final parsed = ScreenStreamPacket.tryParse(sliced);
      expect(parsed, isNull);
    });

    test('ScreenStreamControlMessage JSON serialization roundtrip', () {
      final msg = ScreenStreamControlMessage(
        action: 'start',
        streamId: 3,
        width: 1920,
        height: 1080,
        fps: 60,
        bitrate: 5000000,
        codec: 'H264',
      );

      final jsonStr = msg.serialize();
      final parsed = ScreenStreamControlMessage.fromJson(jsonStr);

      expect(parsed, isNotNull);
      expect(parsed!.action, equals('start'));
      expect(parsed.streamId, equals(3));
      expect(parsed.width, equals(1920));
      expect(parsed.height, equals(1080));
      expect(parsed.fps, equals(60));
      expect(parsed.bitrate, equals(5000000));
      expect(parsed.codec, equals('H264'));
    });

    test('MockScreenStreamCapturer generates config, keyframe and responds to requestKeyframe', () async {
      final capturer = MockScreenStreamCapturer();
      final receivedPackets = <ScreenStreamPacket>[];

      final sub = capturer.frameStream.listen((pkt) {
        receivedPackets.add(pkt);
      });

      const config = ScreenStreamConfig(width: 1280, height: 720, fps: 30);
      final started = await capturer.startCapture(config);
      expect(started, isTrue);
      expect(capturer.isCapturing, isTrue);

      // Wait a short moment to receive the initial config packet and the first keyframe
      await Future.delayed(const Duration(milliseconds: 80));

      expect(receivedPackets.isNotEmpty, isTrue);
      // First packet must be FrameConfig (SPS/PPS)
      expect(
        receivedPackets.first.subtype,
        equals(ScreenStreamSubtype.frameConfig),
      );
      // Second packet must be KeyFrame (IDR)
      expect(receivedPackets.length, greaterThanOrEqualTo(2));
      expect(receivedPackets[1].subtype, equals(ScreenStreamSubtype.keyFrame));

      // Request keyframe and verify
      capturer.requestKeyframe();
      await Future.delayed(const Duration(milliseconds: 60));

      final hasLaterKeyframe = receivedPackets
          .skip(2)
          .any((p) => p.subtype == ScreenStreamSubtype.keyFrame);
      expect(hasLaterKeyframe, isTrue);

      await capturer.stopCapture();
      expect(capturer.isCapturing, isFalse);

      await sub.cancel();
      capturer.dispose();
    });

    test(
      'ScreenStreamManager manages stream lifecycle over loopback connection',
      () async {
        final server = await ServerSocket.bind(InternetAddress.loopbackIPv4, 0);
        final serverPort = server.port;

        final serverSocketFuture = server.first;
        final clientSocket = await Socket.connect(
          InternetAddress.loopbackIPv4,
          serverPort,
        );
        final serverSocket = await serverSocketFuture;

        final idA = DeviceIdentity(deviceId: 'dev-a', name: 'Device A');
        final idB = DeviceIdentity(deviceId: 'dev-b', name: 'Device B');

        final senderConn = SessionConnection(
          socket: clientSocket,
          localIdentity: idA,
        );
        final receiverConn = SessionConnection(
          socket: serverSocket,
          localIdentity: idB,
        );

        final mockCapturer = MockScreenStreamCapturer();
        final streamManager = ScreenStreamManager(capturer: mockCapturer);

        final receivedFrames = <ProtocolFrame>[];
        final sub = receiverConn.frames.listen((frame) {
          receivedFrames.add(frame);
        });

        const config = ScreenStreamConfig(width: 1280, height: 720, fps: 30);
        final started = await streamManager.startStream(senderConn, config);
        expect(started, isTrue);
        expect(streamManager.state, equals(ScreenStreamState.streaming));

        // Wait for stream packets to arrive across TCP
        await Future.delayed(const Duration(milliseconds: 100));
        expect(receivedFrames.isNotEmpty, isTrue);

        // Verify first frame is a control request 'start'
        final firstPacket = ScreenStreamPacket.tryParse(
          receivedFrames.first.payload,
        );
        expect(firstPacket, isNotNull);
        expect(
          firstPacket!.subtype,
          equals(ScreenStreamSubtype.controlRequest),
        );

        // Verify subsequent video frames
        final videoFrames = receivedFrames.skip(1).toList();
        expect(videoFrames.isNotEmpty, isTrue);
        final firstVideoPacket = ScreenStreamPacket.tryParse(
          videoFrames.first.payload,
        );
        expect(firstVideoPacket, isNotNull);
        expect(
          firstVideoPacket!.subtype,
          equals(ScreenStreamSubtype.frameConfig),
        );

        // Stop stream
        await streamManager.stopStream('test_complete');
        expect(streamManager.state, equals(ScreenStreamState.stopped));

        await Future.delayed(const Duration(milliseconds: 50));
        final lastPacket = ScreenStreamPacket.tryParse(
          receivedFrames.last.payload,
        );
        expect(lastPacket, isNotNull);
        expect(lastPacket!.subtype, equals(ScreenStreamSubtype.controlRequest));
        final stopJson = utf8.decode(lastPacket.payload);
        final stopMsg = ScreenStreamControlMessage.fromJson(stopJson);
        expect(stopMsg?.action, equals('stop'));

        await sub.cancel();
        streamManager.dispose();
        await clientSocket.close();
        await serverSocket.close();
        await server.close();
      },
    );
  });
}
