import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/core/device_identity_manager.dart';
import 'package:hinge/core/notification_manager.dart';
import 'package:hinge/core/notification_model.dart';
import 'package:hinge/core/notification_history_model.dart';
import 'package:hinge/core/ocr_engine.dart';
import 'package:hinge/core/pdf_tools.dart';
import 'package:hinge/core/protocol_frame.dart';
import 'package:hinge/core/session_manager.dart';

void main() {
  group('Notification and Native Tools Core Tests', () {
    test('NotificationEventMessage JSON serialization roundtrip', () {
      final notif = NotificationEventMessage(
        notificationId: 'notif-42',
        packageName: 'com.slack',
        appName: 'Slack',
        title: 'Design Lead',
        content: 'Figma mockups updated',
        source: 'generic',
        timestamp: 1725450000000,
        canReply: true,
        actions: ['reply', 'dismiss'],
      );

      final jsonStr = notif.serialize();
      final parsed = NotificationEventMessage.fromJson(jsonStr);

      expect(parsed, isNotNull);
      expect(parsed!.notificationId, equals('notif-42'));
      expect(parsed.packageName, equals('com.slack'));
      expect(parsed.appName, equals('Slack'));
      expect(parsed.title, equals('Design Lead'));
      expect(parsed.content, equals('Figma mockups updated'));
      expect(parsed.source, equals('generic'));
      expect(parsed.timestamp, equals(1725450000000));
      expect(parsed.canReply, isTrue);
      expect(parsed.actions, equals(['reply', 'dismiss']));
    });

    test('SMS verification payload preserves local copy metadata', () {
      final notif = NotificationEventMessage(
        notificationId: 'sms-42',
        packageName: 'android.provider.Telephony.SMS',
        appName: '短信',
        title: '1069xxxx',
        content: '你的验证码是 123456，请勿泄露。',
        source: 'sms',
        isVerificationCode: true,
        verificationCode: '123456',
        actions: ['copy_code'],
      );

      final parsed = NotificationEventMessage.fromJson(notif.serialize());

      expect(parsed, isNotNull);
      expect(parsed!.source, equals('sms'));
      expect(parsed.isVerificationCode, isTrue);
      expect(parsed.verificationCode, equals('123456'));
      expect(parsed.actions, equals(['copy_code']));
    });

    test('Notification history page preserves paging and app metadata', () {
      final page = NotificationHistoryPage.fromJson({
        'access': true,
        'enabled': true,
        'total': 301,
        'items': [
          {
            'id': 'com.tencent.mm|key|1',
            'packageName': 'com.tencent.mm',
            'appName': '微信',
            'title': '联系人',
            'content': '通知正文',
            'timestamp': 1725450000000,
            'notificationKey': '0|com.tencent.mm|key',
          },
        ],
        'applications': [
          {
            'packageName': 'com.tencent.mm',
            'appName': '微信',
            'count': 301,
            'iconBase64': 'icon',
          },
        ],
      });

      expect(page.accessEnabled, isTrue);
      expect(page.enabled, isTrue);
      expect(page.total, equals(301));
      expect(page.items.single.content, equals('通知正文'));
      expect(page.applications.single.count, equals(301));
      expect(page.applications.single.iconBase64, equals('icon'));
    });

    test('NotificationActionMessage JSON serialization roundtrip', () {
      final action = NotificationActionMessage(
        notificationId: 'notif-42',
        actionKey: 'reply',
        replyText: 'Looks great!',
        timestamp: 1725450005000,
      );

      final jsonStr = action.serialize();
      final parsed = NotificationActionMessage.fromJson(jsonStr);

      expect(parsed, isNotNull);
      expect(parsed!.notificationId, equals('notif-42'));
      expect(parsed.actionKey, equals('reply'));
      expect(parsed.replyText, equals('Looks great!'));
      expect(parsed.timestamp, equals(1725450005000));
    });

    test('NotificationFilter handles blacklist, whitelist, and debouncing', () {
      final filter = NotificationFilter();
      filter.blacklistPackages.add('com.spam.ads');

      final spam = NotificationEventMessage(
        packageName: 'com.spam.ads',
        title: 'Ad',
        content: 'Promo',
      );
      final work1 = NotificationEventMessage(
        packageName: 'com.slack',
        title: 'Work',
        content: 'Standup',
      );
      final work2 = NotificationEventMessage(
        packageName: 'com.slack',
        title: 'Work',
        content: 'Standup',
      );
      final work3 = NotificationEventMessage(
        packageName: 'com.slack',
        title: 'Work',
        content: 'PR Review',
      );

      expect(filter.shouldAllow(spam), isFalse);
      expect(filter.shouldAllow(work1), isTrue);
      // Immediate duplicate suppressed by debounce
      expect(filter.shouldAllow(work2), isFalse);
      // Distinct message allowed
      expect(filter.shouldAllow(work3), isTrue);

      // Add whitelist
      filter.whitelistPackages.add('com.slack');
      final game = NotificationEventMessage(
        packageName: 'com.game',
        title: 'Game',
        content: 'Turn',
      );
      final work4 = NotificationEventMessage(
        packageName: 'com.slack',
        title: 'Work',
        content: 'Deployment',
      );

      expect(filter.shouldAllow(game), isFalse);
      expect(filter.shouldAllow(work4), isTrue);
    });

    test(
      'NotificationManager loopback relay dispatches and receives events',
      () async {
        final server = await ServerSocket.bind(InternetAddress.loopbackIPv4, 0);
        final serverPort = server.port;

        final serverSocketFuture = server.first;
        final clientSocket = await Socket.connect(
          InternetAddress.loopbackIPv4,
          serverPort,
        );
        final serverSocket = await serverSocketFuture;

        final idA = DeviceIdentity(deviceId: 'dev-phone', name: 'Phone');
        final idB = DeviceIdentity(deviceId: 'dev-pc', name: 'PC');

        final senderConn = SessionConnection(
          socket: clientSocket,
          localIdentity: idA,
        );
        final receiverConn = SessionConnection(
          socket: serverSocket,
          localIdentity: idB,
        );

        final senderManager = NotificationManager();
        final receiverManager = NotificationManager();

        senderManager.registerConnection(senderConn);

        final receivedNotifs = <NotificationEventMessage>[];
        receiverConn.frames.listen((frame) {
          if (frame.type == MessageType.notificationEvent) {
            final jsonStr = utf8.decode(frame.payload);
            final notif = NotificationEventMessage.fromJson(jsonStr);
            if (notif != null) receivedNotifs.add(notif);
          }
        });

        final testNotif = NotificationEventMessage(
          packageName: 'com.team.chat',
          appName: 'TeamChat',
          title: 'Alice',
          content: 'Code review ready',
        );

        final dispatched = await senderManager.dispatchNotification(testNotif);
        expect(dispatched, isTrue);

        await Future.delayed(const Duration(milliseconds: 100));
        expect(receivedNotifs.length, equals(1));
        expect(receivedNotifs.first.title, equals('Alice'));
        expect(receivedNotifs.first.content, equals('Code review ready'));

        senderManager.dispose();
        receiverManager.dispose();
        await clientSocket.close();
        await serverSocket.close();
        await server.close();
      },
    );

    test('PdfTools creates document and inspects metadata', () {
      final pages = ['Page 1 Intro', 'Page 2 Body', 'Page 3 Conclusion'];
      final pdfBytes = PdfTools.createDocument('Sample Document', pages);

      expect(pdfBytes.length, greaterThan(100));

      final meta = PdfTools.inspectMetadata(pdfBytes);
      expect(meta.version, equals('1.4'));
      expect(meta.pageCount, equals(3));
      expect(meta.fileSizeBytes, equals(pdfBytes.length));
    });

    test('PdfTools merges and splits documents', () {
      final doc1 = PdfTools.createDocument('Doc 1', ['P1', 'P2']);
      final doc2 = PdfTools.createDocument('Doc 2', ['P3', 'P4']);

      final merged = PdfTools.mergePdfs([doc1, doc2], 'Merged Doc');
      final mergedMeta = PdfTools.inspectMetadata(merged);
      expect(mergedMeta.pageCount, equals(4));

      final split = PdfTools.splitPdf(merged, 2, 4);
      final splitMeta = PdfTools.inspectMetadata(split);
      expect(splitMeta.pageCount, equals(3)); // pages 2, 3, 4 = 3 pages
    });

    test('MockOcrEngine recognizes text and handles empty input', () async {
      final ocr = MockOcrEngine();
      expect(ocr.isAvailable, isTrue);

      final sampleImg = Uint8List.fromList([
        0xFF,
        0xD8,
        0xFF,
        0xE0,
        0x00,
        0x10,
      ]);
      final result = await ocr.recognizeText(sampleImg);

      expect(result.success, isTrue);
      expect(result.text, contains('Hinge Native OCR'));
      expect(result.errorMessage, isNull);

      final emptyResult = await ocr.recognizeText(Uint8List(0));
      expect(emptyResult.success, isFalse);
      expect(emptyResult.errorMessage, isNotNull);
    });
  });
}
